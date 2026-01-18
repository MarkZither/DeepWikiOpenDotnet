using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;

namespace DeepWiki.Rag.Core.Tests.Tools.EmbeddingFixtureGenerator;

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
        // Ollama: POST /api/embed?model=<model> or POST /api/embed with model in body
        var requestUri = $"/api/embed?model={Uri.EscapeDataString(_model)}";
        var payload = new { input };
        var resp = await _http.PostAsJsonAsync(requestUri, payload);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        // Ollama may return { embedding: [...] } or { embeddings: [...] } - be permissive
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

        throw new InvalidOperationException("Ollama response missing embedding");
    }

    public void Dispose() => _http.Dispose();
}
