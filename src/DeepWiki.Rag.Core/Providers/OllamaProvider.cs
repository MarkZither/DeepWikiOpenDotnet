using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DeepWiki.Rag.Core.Providers;

public class OllamaProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaProvider> _logger;
    private readonly TimeSpan _stallTimeout;
    private readonly string _model;

    public OllamaProvider(HttpClient httpClient, ILogger<OllamaProvider> logger, string model, TimeSpan? stallTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _model = !string.IsNullOrWhiteSpace(model) ? model : throw new ArgumentException("Model cannot be empty", nameof(model));
        _stallTimeout = stallTimeout ?? TimeSpan.FromMinutes(5);
    }

    public string Name => "Ollama";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _httpClient.GetAsync("/api/tags", cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama IsAvailableAsync check failed");
            return false;
        }
    }

    public async IAsyncEnumerable<GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            throw new ArgumentException("promptText cannot be empty", nameof(promptText));

        var startTime = DateTime.UtcNow;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_stallTimeout);

        var request = new 
        { 
            model = _model,
            prompt = promptText, 
            system = systemPrompt,
            stream = true
        };
        
        // Use SendAsync with ResponseHeadersRead to enable true streaming
        // (PostAsJsonAsync buffers the entire response, which hangs on streaming endpoints)
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = JsonContent.Create(request)
        };
        
        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // Distinguish between timeout and user cancellation by checking elapsed time
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed >= _stallTimeout.Subtract(TimeSpan.FromSeconds(1))) // Allow 1 second tolerance
            {
                throw new TimeoutException($"Ollama provider timed out after {elapsed.TotalSeconds:F1}s", ex);
            }
            throw; // User cancelled, rethrow as-is
        }

        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        var buffer = new byte[4096];
        var seq = 0;
        var leftover = new List<byte>();

        var channel = System.Threading.Channels.Channel.CreateUnbounded<GenerationDelta>();

        var producer = Task.Run(async () =>
        {
            try
            {
                // Track when the last bytes arrived so we can report true stall duration,
                // not total elapsed time.  Reset after every successful read.
                var lastActivity = DateTime.UtcNow;

                while (true)
                {
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex)
                    {
                        // Check stall duration (time since last received bytes), not total elapsed.
                        // This distinguishes a genuine stall from a long but active generation.
                        var sinceLastActivity = DateTime.UtcNow - lastActivity;
                        if (sinceLastActivity >= _stallTimeout.Subtract(TimeSpan.FromSeconds(1)))
                        {
                            throw new TimeoutException(
                                $"Ollama provider stalled for {sinceLastActivity.TotalSeconds:F1}s (no data) while streaming", ex);
                        }
                        throw; // User cancelled, rethrow as-is
                    }

                    if (read == 0)
                        break;

                    // Reset the stall-timeout deadline — as long as bytes keep arriving
                    // the stream is healthy and should never be cancelled.
                    lastActivity = DateTime.UtcNow;
                    cts.CancelAfter(_stallTimeout);

                    leftover.AddRange(buffer.Take(read));

                    // Split by newline
                    int newlineIndex;
                    while ((newlineIndex = leftover.FindIndex(b => b == (byte)'\n')) >= 0)
                    {
                        var lineBytes = leftover.Take(newlineIndex).ToArray();
                        leftover = leftover.Skip(newlineIndex + 1).ToList();

                        if (lineBytes.Length == 0)
                            continue;

                        try
                        {
                            using var doc = JsonDocument.Parse(lineBytes);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("response", out var responseProp))
                            {
                                var text = responseProp.GetString() ?? string.Empty;
                                await channel.Writer.WriteAsync(new GenerationDelta
                                {
                                    PromptId = string.Empty,
                                    Role = "assistant",
                                    Type = "token",
                                    Seq = seq++,
                                    Text = text
                                }, cts.Token);
                            }
                            
                            if (root.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean())
                            {
                                await channel.Writer.WriteAsync(new GenerationDelta
                                {
                                    PromptId = string.Empty,
                                    Role = "assistant",
                                    Type = "done",
                                    Seq = seq++,
                                    Text = null
                                }, cts.Token);
                            }
                            
                            if (root.TryGetProperty("error", out var errorProp))
                            {
                                await channel.Writer.WriteAsync(new GenerationDelta
                                {
                                    PromptId = string.Empty,
                                    Role = "assistant",
                                    Type = "error",
                                    Seq = seq++,
                                    Text = null,
                                    Metadata = new { message = errorProp.GetString() }
                                }, cts.Token);
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse NDJSON line from Ollama provider");
                            // Emit error delta
                            await channel.Writer.WriteAsync(new GenerationDelta
                            {
                                PromptId = string.Empty,
                                Role = "assistant",
                                Type = "error",
                                Seq = seq++,
                                Text = null,
                                Metadata = new { message = "Invalid NDJSON from provider" }
                            }, cts.Token);
                        }
                    }
                }

                // If leftover has data, attempt to parse final line
                if (leftover.Count > 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(leftover.ToArray());
                        var root = doc.RootElement;
                        if (root.TryGetProperty("response", out var responseProp))
                        {
                            var text = responseProp.GetString() ?? string.Empty;
                            await channel.Writer.WriteAsync(new GenerationDelta
                            {
                                PromptId = string.Empty,
                                Role = "assistant",
                                Type = "token",
                                Seq = seq++,
                                Text = text
                            }, cts.Token);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse final NDJSON chunk");
                    }
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        });

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }

            if (producer.IsFaulted)
                await producer; // rethrow
        }
        finally
        {
            // Ensure the producer task is cancelled and completed before we exit
            cts.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when we cancel
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Producer task failed during cleanup");
            }
        }
    }
}
