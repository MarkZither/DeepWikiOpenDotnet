using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Services;
using DeepWiki.Rag.Core.Providers;
using DeepWiki.Rag.Core.Models;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Services;

public class GenerationService : IGenerationService
{
    private readonly IList<IModelProvider> _providers;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<GenerationService> _logger;
    private readonly DeepWiki.Data.Abstractions.IVectorStore? _vectorStore;
    private readonly DeepWiki.Data.Abstractions.IEmbeddingService? _embeddingService;

    private readonly ConcurrentDictionary<string, List<GenerationDelta>> _idempotencyCache = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _promptCancellations = new();
    private readonly PromptCancellationRegistry? _promptRegistry;
    // Circuit breaker state: failure counts and open-until timestamps per provider name
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();
    private readonly ConcurrentDictionary<string, DateTime> _circuitOpenUntil = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _circuitBreakDuration;

    private readonly DeepWiki.Rag.Core.Observability.GenerationMetrics _metrics;
    private readonly TimeSpan _providerStallTimeout;

    public GenerationService(IEnumerable<IModelProvider> providers, SessionManager sessionManager, DeepWiki.Rag.Core.Observability.GenerationMetrics metrics, ILogger<GenerationService> logger, DeepWiki.Data.Abstractions.IVectorStore? vectorStore = null, DeepWiki.Data.Abstractions.IEmbeddingService? embeddingService = null, TimeSpan? providerStallTimeout = null, int failureThreshold = 3, TimeSpan? circuitBreakDuration = null, PromptCancellationRegistry? promptRegistry = null)
    {
        _providers = providers.ToList();
        _sessionManager = sessionManager;
        _logger = logger;
        _metrics = metrics;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _providerStallTimeout = providerStallTimeout ?? TimeSpan.FromMinutes(5);
        _failureThreshold = failureThreshold;
        _circuitBreakDuration = circuitBreakDuration ?? TimeSpan.FromSeconds(30);
        _promptRegistry = promptRegistry;
    }

    public async IAsyncEnumerable<GenerationDelta> GenerateAsync(string sessionId, string promptText, int topK = 5, Dictionary<string, string>? filters = null, string? idempotencyKey = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || _sessionManager.GetSession(sessionId) == null)
            throw new ArgumentException("Invalid sessionId", nameof(sessionId));

        if (string.IsNullOrWhiteSpace(promptText))
            throw new ArgumentException("Prompt text cannot be empty", nameof(promptText));

        var cacheKey = !string.IsNullOrEmpty(idempotencyKey) ? $"{sessionId}:{idempotencyKey}" : null;
        if (cacheKey != null && _idempotencyCache.TryGetValue(cacheKey, out var cached))
        {
            foreach (var d in cached)
            {
                yield return d;
            }
            yield break;
        }

        var prompt = _sessionManager.CreatePrompt(sessionId, promptText, idempotencyKey);

        // Create linked cancellation source so controller can cancel by calling CancelAsync
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _promptCancellations[prompt.PromptId] = cts;
        // Also register globally so the host can cancel all in-flight prompts on shutdown
        try { _promptRegistry?.Register(prompt.PromptId, cts); } catch { }
        var recorded = new List<GenerationDelta>();

        // Use a bounded channel with backpressure to prevent memory bloat if consumer is slow.
        // BoundedChannelFullMode.Wait causes the provider to wait if the buffer fills, applying backpressure.
        // Capacity of 100 deltas provides reasonable buffering while preventing unbounded memory growth.
        var channelOptions = new System.Threading.Channels.BoundedChannelOptions(100)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
        };
        var channel = System.Threading.Channels.Channel.CreateBounded<GenerationDelta>(channelOptions);

        // Build RAG system prompt if vector store + embedding service available and topK > 0
        string? systemPrompt = null;
        if (_vectorStore != null && _embeddingService != null && topK > 0)
        {
            try
            {
                var embedding = await _embeddingService.EmbedAsync(promptText, cancellationToken);
                var rawResults = await _vectorStore.QueryAsync(embedding, topK * 3, filters, cancellationToken);

                // Deduplicate: keep only the highest-scoring chunk per (RepoUrl, FilePath) pair
                var maxContextDocs = 5; // Generation:MaxContextDocuments default
                var deduped = rawResults
                    .GroupBy(r => (r.Document.RepoUrl, r.Document.FilePath))
                    .Select(g => g.OrderByDescending(r => r.SimilarityScore).First())
                    .OrderByDescending(r => r.SimilarityScore)
                    .Take(maxContextDocs)
                    .ToList();
                var results = (IReadOnlyList<VectorQueryResult>)deduped;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Context documents:");
                foreach (var r in results.Take(topK))
                {
                    sb.AppendLine($"- Title: {r.Document.Title}");
                    var excerpt = r.Document.Text;
                    if (excerpt?.Length > 500) excerpt = excerpt.Substring(0, 500) + "...";
                    sb.AppendLine($"  Excerpt: {excerpt}");
                    sb.AppendLine();
                }

                systemPrompt = sb.ToString();
                _logger.LogInformation("Built RAG system prompt with {Count} documents for prompt {PromptId}", results.Count, prompt.PromptId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build RAG context for prompt {PromptId}; continuing without context", prompt.PromptId);
            }
        }

        // NOTE: We explicitly copy provider deltas into new GenerationDelta instances using the
        // service-owned PromptId so that clients can reliably cancel using the prompt id we
        // assign. Providers may emit their own prompt ids (or none), so copying ensures a
        // consistent identifier surface across the service boundary.
        //
        // Additionally, when the prompt is cancelled we emit a final "done" delta and attempt
        // to complete the channel. This ensures the HTTP streaming response terminates promptly
        // instead of leaving clients waiting for more data from the provider (which may still be
        // producing or blocked). The TryWrite/TryComplete calls are best-effort to avoid throwing
        // during cancellation cleanup.
        cts.Token.Register(() =>
        {
            try
            {
                var done = new GenerationDelta { PromptId = prompt.PromptId, Type = "done", Seq = recorded.Count, Role = "assistant" };
                channel.Writer.TryWrite(done);
                channel.Writer.TryComplete();
            }
            catch { }
        });

        var producer = Task.Run(async () =>
        {
            Exception? lastEx = null;

            foreach (var provider in _providers)
            {
                // Skip provider if circuit is open
                if (_circuitOpenUntil.TryGetValue(provider.Name, out var until) && until > DateTime.UtcNow)
                {
                    _logger.LogWarning("Skipping provider {Provider} due to open circuit until {Until}", provider.Name, until);
                    continue;
                }

                try
                {
                    var isAvail = await provider.IsAvailableAsync(cts.Token);
                    if (!isAvail)
                    {
                        _logger.LogWarning("Provider {Provider} is not available", provider.Name);
                        RegisterFailure(provider.Name);
                        continue;
                    }

                    await foreach (var delta in provider.StreamAsync(promptText, systemPrompt, cts.Token))
                    {
                        var outDelta = new GenerationDelta
                        {
                            PromptId = prompt.PromptId,
                            Type = delta.Type,
                            Seq = delta.Seq,
                            Text = delta.Text,
                            Role = delta.Role,
                            // Attach provider information so downstream consumers (metrics/health) can attribute events
                            Metadata = new { provider = provider.Name, original = delta.Metadata }
                        };

                        await channel.Writer.WriteAsync(outDelta, cts.Token);
                    }

                    // success -> reset failure count
                    ResetFailures(provider.Name);

                    channel.Writer.Complete();
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogInformation(ex, "Generation canceled for prompt {PromptId} by provider {Provider}", prompt.PromptId, provider.Name);
                    _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Cancelled, recorded.Count);
                    channel.Writer.Complete(ex);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Provider {Provider} failed during generation", provider.Name);
                    RegisterFailure(provider.Name);
                    lastEx = ex;
                    // try next provider
                }
            }

            // If reached here, no provider succeeded
            if (lastEx != null)
            {
                _logger.LogError(lastEx, "All providers failed for prompt {PromptId}", prompt.PromptId);
                _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Error, recorded.Count);
                try
                {
                    var err = new GenerationDelta { PromptId = prompt.PromptId, Type = "error", Seq = recorded.Count, Role = "assistant", Metadata = new { code = "provider_error", message = "All providers failed" } };
                    channel.Writer.TryWrite(err);
                    channel.Writer.TryComplete();
                }
                catch { }
            }
            else
            {
                channel.Writer.TryComplete();
            }
        });

        Task? stallMonitor = null;
        try
        {
            var normalizer = new DeepWiki.Rag.Core.Streaming.StreamNormalizer(prompt.PromptId, "assistant");

            // Monitor for provider stall (no tokens within configured timeout)
            var lastTokenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastTokenTime = DateTime.UtcNow;

            stallMonitor = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);
                    if (DateTime.UtcNow - lastTokenTime > _providerStallTimeout)
                    {
                        // Emit error delta and cancel
                        try
                        {
                            var err = new GenerationDelta { PromptId = prompt.PromptId, Type = "error", Seq = recorded.Count, Role = "assistant", Metadata = new { code = "timeout", message = "Provider stalled" } };
                            channel.Writer.TryWrite(err);
                            _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Error, recorded.Count);
                            channel.Writer.TryComplete();
                        }
                        catch { }

                        try { cts.Cancel(); } catch { }
                        break;
                    }
                }
            });

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    if (item.Type == "token" && item.Text != null)
                    {
                        // update last token time
                        lastTokenTime = DateTime.UtcNow;

                        var chunks = new System.Collections.Generic.List<byte[]> { System.Text.Encoding.UTF8.GetBytes(item.Text) };
                        var timer = _metrics.StartTtfMeasurement();
                        var first = true;
                        foreach (var nd in normalizer.Normalize(chunks))
                        {
                            recorded.Add(nd);
                            _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.InFlight, recorded.Count);

                            // On first token, record TTF
                            if (first)
                            {
                                first = false;
                                var providerNameForMetrics = GetProviderNameFromMetadata(nd.Metadata) ?? "unknown";
                                _metrics.RecordTimeToFirstToken(timer.Elapsed.TotalMilliseconds, providerNameForMetrics);
                            }

                            var providerName = GetProviderNameFromMetadata(nd.Metadata) ?? "unknown";
                            _metrics.RecordTokens(1, providerName);
                            yield return nd;
                        }
                    }
                    else
                    {
                        recorded.Add(item);
                        _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.InFlight, recorded.Count);
                        yield return item;
                    }
                }
            }

            // Ensure producer completed successfully
            if (producer.IsFaulted)
                await producer; // will rethrow

            _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Done, recorded.Count);

            if (cacheKey != null)
            {
                _idempotencyCache[cacheKey] = recorded;
            }
        }
        finally
        {
            // Ensure background tasks are cancelled and completed
            try { cts.Cancel(); } catch { }
            
            if (stallMonitor != null && !stallMonitor.IsCompleted)
            {
                try { await stallMonitor; } catch { }
            }
            
            if (!producer.IsCompleted)
            {
                try { await producer; } catch { }
            }
            
            _promptCancellations.TryRemove(prompt.PromptId, out _);
            try { _promptRegistry?.Unregister(prompt.PromptId); } catch { }
            cts.Dispose();
        }
    }

    public Task CancelAsync(string sessionId, string promptId)
    {
        if (_promptCancellations.TryGetValue(promptId, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
        else if (_promptRegistry != null)
        {
            // Try to cancel via the global registry (covers other request scopes)
            try { _promptRegistry.TryCancel(promptId); } catch { }
        }

        _sessionManager.UpdatePromptStatus(sessionId, promptId, PromptStatus.Cancelled);
        return Task.CompletedTask;
    }

    public Task GracefulShutdownAsync()
    {
        foreach (var kv in _promptCancellations)
        {
            try { kv.Value.Cancel(); } catch { }
        }
        return Task.CompletedTask;
    }

    private void RegisterFailure(string providerName)
    {
        _failureCounts.AddOrUpdate(providerName, 1, (_, cur) => cur + 1);
        if (_failureCounts.TryGetValue(providerName, out var cnt) && cnt >= _failureThreshold)
        {
            _circuitOpenUntil[providerName] = DateTime.UtcNow.Add(_circuitBreakDuration);
            _logger.LogWarning("Circuit opened for provider {Provider} due to {Count} failures. Open until {Until}", providerName, cnt, _circuitOpenUntil[providerName]);
        }
    }

    private void ResetFailures(string providerName)
    {
        _failureCounts.TryRemove(providerName, out _);
        _circuitOpenUntil.TryRemove(providerName, out _);
    }

    private string? GetProviderNameFromMetadata(object? metadata)
    {
        if (metadata == null) return null;
        try
        {
            var prop = metadata.GetType().GetProperty("provider");
            if (prop != null)
            {
                return prop.GetValue(metadata)?.ToString();
            }

            var prop2 = metadata.GetType().GetProperty("Provider");
            if (prop2 != null)
            {
                return prop2.GetValue(metadata)?.ToString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

