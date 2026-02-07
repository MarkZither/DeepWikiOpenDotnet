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

    private readonly ConcurrentDictionary<string, List<GenerationDelta>> _idempotencyCache = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _promptCancellations = new();

    private readonly DeepWiki.Rag.Core.Observability.GenerationMetrics _metrics;

    public GenerationService(IModelProvider provider, SessionManager sessionManager, DeepWiki.Rag.Core.Observability.GenerationMetrics metrics, ILogger<GenerationService> logger)
    {
        _provider = provider;
        _sessionManager = sessionManager;
        _logger = logger;
        _metrics = metrics;
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

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var delta in _provider.StreamAsync(promptText, null, cts.Token))
                {
                    await channel.Writer.WriteAsync(delta, cts.Token);
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

        try
        {
            var normalizer = new DeepWiki.Rag.Core.Streaming.StreamNormalizer(prompt.PromptId, "assistant");

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    if (item.Type == "token" && item.Text != null)
                    {
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
