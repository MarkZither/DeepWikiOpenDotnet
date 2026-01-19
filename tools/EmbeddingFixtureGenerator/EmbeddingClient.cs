using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;

namespace DeepWiki.EmbeddingFixtureGenerator;

internal class EmbeddingClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _useOllama;
    private readonly string _model;

    public EmbeddingClient(string host, int port, bool useOllama, string model)
    {
        _useOllama = useOllama;
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri(GetBaseUrl(host, port)) };
    }

    private static string GetBaseUrl(string host, int port)
        => $"http://{host}:{port}";

    public async Task<float[]> GetEmbeddingAsync(string input)
    {
        if (_useOllama)
            return await CallOllamaAsync(input);
        else
            return await CallFoundryAsync(input);
    }

    private async Task<float[]> CallFoundryAsync(string input)
    {
        // Assumes a Foundry-compatible /v1/embeddings endpoint (OpenAI-compatible)
        var payload = new { model = _model, input };
        var resp = await _http.PostAsJsonAsync("/v1/embeddings", payload);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        // Look for data[0].embedding
        if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var embElem = data[0].GetProperty("embedding");
            return embElem.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        }

        throw new InvalidOperationException("Foundry response missing embedding");
    }

    private async Task<float[]> CallOllamaAsync(string input)
    {
        // Try common Ollama embed endpoints with a small retry/backoff
        var endpoints = new[]
        {
            (uri: "/api/embed", bodyModelInPayload: true),
            (uri: $"/api/embed?model={Uri.EscapeDataString(_model)}", bodyModelInPayload: false),
            (uri: "/embed", bodyModelInPayload: true),
            (uri: "/v1/embeddings", bodyModelInPayload: true)
        };

        var attempts = 0;
        foreach (var ep in endpoints)
        {
            attempts++;
            try
            {
                object payload = ep.bodyModelInPayload ? new { model = _model, input } : new { input };
                var resp = await _http.PostAsJsonAsync(ep.uri, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    // on 404/5xx try next endpoint
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (doc.RootElement.TryGetProperty("embedding", out var single))
                {
                    return single.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                }

                if (doc.RootElement.TryGetProperty("embeddings", out var arr) && arr.GetArrayLength() > 0)
                {
                    var first = arr[0];
                    return first.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                }

                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    var embElem = data[0].GetProperty("embedding");
                    return embElem.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                }

                // If no expected property, continue to next endpoint
            }
            catch (HttpRequestException)
            {
                // network error, try next endpoint
            }

            // small backoff between endpoint tries
            await Task.Delay(250 * attempts);
        }

        throw new InvalidOperationException("Ollama endpoints tried but none returned an embedding");
    }

    public void Dispose() => _http.Dispose();
}
