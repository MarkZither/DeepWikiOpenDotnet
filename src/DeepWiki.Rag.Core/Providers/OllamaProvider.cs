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

    public OllamaProvider(HttpClient httpClient, ILogger<OllamaProvider> logger, TimeSpan? stallTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_stallTimeout);

        var request = new { prompt = promptText, system_prompt = systemPrompt };
        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.PostAsJsonAsync("/api/generate", request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Ollama provider stalled or request timed out");
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
                while (true)
                {
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Ollama provider stalled while streaming");
                    }

                    if (read == 0)
                        break;

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

                            if (root.TryGetProperty("token", out var tokenProp))
                            {
                                var text = tokenProp.GetString() ?? string.Empty;
                                await channel.Writer.WriteAsync(new GenerationDelta
                                {
                                    PromptId = string.Empty,
                                    Role = "assistant",
                                    Type = "token",
                                    Seq = seq++,
                                    Text = text
                                }, cts.Token);
                            }
                            else if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "done")
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
                            else if (root.TryGetProperty("error", out var errorProp))
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
                        if (root.TryGetProperty("token", out var tokenProp))
                        {
                            var text = tokenProp.GetString() ?? string.Empty;
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
            // nothing extra
        }
    }
}
