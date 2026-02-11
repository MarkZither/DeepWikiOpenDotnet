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
    private readonly IModelProvider _provider;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<GenerationService> _logger;
    private readonly DeepWiki.Data.Abstractions.IVectorStore? _vectorStore;
    private readonly DeepWiki.Data.Abstractions.IEmbeddingService? _embeddingService;

    private readonly ConcurrentDictionary<string, List<GenerationDelta>> _idempotencyCache = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _promptCancellations = new();

    private readonly DeepWiki.Rag.Core.Observability.GenerationMetrics _metrics;
    private readonly TimeSpan _providerStallTimeout;

    public GenerationService(IModelProvider provider, SessionManager sessionManager, DeepWiki.Rag.Core.Observability.GenerationMetrics metrics, ILogger<GenerationService> logger, DeepWiki.Data.Abstractions.IVectorStore? vectorStore = null, DeepWiki.Data.Abstractions.IEmbeddingService? embeddingService = null, TimeSpan? providerStallTimeout = null)
    {
        _provider = provider;
        _sessionManager = sessionManager;
        _logger = logger;
        _metrics = metrics;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _providerStallTimeout = providerStallTimeout ?? TimeSpan.FromMinutes(5);
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

        var recorded = new List<GenerationDelta>();

        // Use a producer task to consume the provider stream and write into a channel.
        var channel = System.Threading.Channels.Channel.CreateUnbounded<GenerationDelta>();

        // Build RAG system prompt if vector store + embedding service available and topK > 0
        string? systemPrompt = null;
        if (_vectorStore != null && _embeddingService != null && topK > 0)
        {
            try
            {
                var embedding = await _embeddingService.EmbedAsync(promptText, cancellationToken);
                var results = await _vectorStore.QueryAsync(embedding, topK, filters, cancellationToken);

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
            try
            {
                await foreach (var delta in _provider.StreamAsync(promptText, systemPrompt, cts.Token))
                {
                    var outDelta = new GenerationDelta
                    {
                        PromptId = prompt.PromptId,
                        Type = delta.Type,
                        Seq = delta.Seq,
                        Text = delta.Text,
                        Role = delta.Role,
                        Metadata = delta.Metadata
                    };

                    await channel.Writer.WriteAsync(outDelta, cts.Token);
                }

                channel.Writer.Complete();
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation(ex, "Generation canceled for prompt {PromptId}", prompt.PromptId);
                _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Cancelled, recorded.Count);
                channel.Writer.Complete(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider failed during generation");
                _sessionManager.UpdatePromptStatus(sessionId, prompt.PromptId, PromptStatus.Error, recorded.Count);
                channel.Writer.Complete(ex);
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
                                _metrics.RecordTimeToFirstToken(timer.Elapsed.TotalMilliseconds, _provider.Name);
                            }

                            _metrics.RecordTokens(1, _provider.Name);
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
            cts.Dispose();
        }
    }

    public Task CancelAsync(string sessionId, string promptId)
    {
        if (_promptCancellations.TryGetValue(promptId, out var cts))
        {
            cts.Cancel();
        }

        _sessionManager.UpdatePromptStatus(sessionId, promptId, PromptStatus.Cancelled);
        return Task.CompletedTask;
    }
}
