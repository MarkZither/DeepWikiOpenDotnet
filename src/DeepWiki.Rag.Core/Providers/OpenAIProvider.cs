using System.Text.Json;
using System.Text.Json.Serialization;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace DeepWiki.Rag.Core.Providers;

/// <summary>
/// OpenAI provider adapter that supports OpenAI-style HTTP streaming (NDJSON)
/// and can be pointed at OpenAI-compatible endpoints (e.g., Ollama, Foundry, OpenAI).
/// Configuration keys:
/// - OpenAI:BaseUrl (optional, defaults to https://api.openai.com)
/// - OpenAI:Provider (optional, "openai" | "ollama" | "foundry")
/// - OpenAI:ModelId (optional)
/// - OpenAI:ApiKey (optional)
/// </summary>
public class OpenAIProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _modelId;
    private readonly string _providerType;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(HttpClient httpClient, string? apiKey, string providerType, string modelId, ILogger<OpenAIProvider> logger)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey;
        _providerType = providerType ?? "openai";
        _modelId = modelId ?? "gpt-4o-mini";
        _logger = logger;
    }

    public string Name => "OpenAI";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Perform a lightweight availability check depending on provider type
            if (string.Equals(_providerType, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                // Ollama commonly exposes /api/tags or /api/ping; try /api/tags
                var resp = await _http.GetAsync("/api/tags", cancellationToken);
                return resp.IsSuccessStatusCode;
            }
            else
            {
                // Try OpenAI-compatible /v1/models endpoint
                var req = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
                if (!string.IsNullOrEmpty(_apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                var resp = await _http.SendAsync(req, cancellationToken);
                return resp.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "OpenAI provider availability check failed");
            return false;
        }
    }

    public async IAsyncEnumerable<GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build OpenAI chat completion request body compatible with OpenAI streaming
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");

        var messages = new List<object>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = promptText });

        var body = new
        {
            model = _modelId,
            messages = messages,
            stream = true
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // If this is a true OpenAI deployment we require an API key; Ollama or other local
        // OpenAI-compatible servers may not require an API key (allow empty when providerType=="ollama").
        if (string.Equals(_providerType, "openai", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("OpenAI provider is not configured (API key missing)");

        using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        int seq = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break; // EOF
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // OpenAI streaming uses lines like: "data: {json}" or "data: [DONE]"
            string? tokenText = null;
            bool finish = false;

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line.Substring("data:".Length).Trim();
                if (payload == "[DONE]")
                {
                    yield return new GenerationDelta { PromptId = string.Empty, Type = "done", Role = "assistant", Seq = seq++ };
                    yield break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var content))
                        {
                            tokenText = content.GetString();
                        }

                        if (choices[0].TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null)
                        {
                            finish = true;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming payload from OpenAI provider");
                }
            }
            else
            {
                // Some providers may emit raw NDJSON objects (without data: prefix)
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var content))
                        {
                            tokenText = content.GetString();
                        }
                    }
                }
                catch (JsonException) { }
            }

            if (!string.IsNullOrEmpty(tokenText))
            {
                yield return new GenerationDelta { PromptId = string.Empty, Type = "token", Text = tokenText, Role = "assistant", Seq = seq++ };
            }

            if (finish)
            {
                yield return new GenerationDelta { PromptId = string.Empty, Type = "done", Role = "assistant", Seq = seq++ };
                yield break;
            }
        }
    }
}
