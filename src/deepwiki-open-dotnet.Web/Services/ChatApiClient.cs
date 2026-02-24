using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

public class ChatApiClient
{
    private readonly HttpClient _httpClient;

    public ChatApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SessionResponseDto> CreateSessionAsync(SessionRequestDto request, CancellationToken cancellationToken = default)
    {
        var resp = await _httpClient.PostAsJsonAsync("/api/generation/session", request, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SessionResponseDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("Empty session response");
    }

    /// <summary>
    /// Calls the streaming generation endpoint and returns the HttpResponseMessage so the caller can read the response stream.
    /// </summary>
    public Task<HttpResponseMessage> StreamGenerationAsync(GenerationRequestDto request, CancellationToken cancellationToken = default)
        => _httpClient.PostAsJsonAsync("/api/generation/stream", request, cancellationToken);

    /// <summary>
    /// Fetches available document collections from GET /api/documents/collections.
    /// </summary>
    public async Task<DocumentListResponseDto> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _httpClient.GetFromJsonAsync<DocumentListResponseDto>("/api/documents/collections", cancellationToken).ConfigureAwait(false);
        return result ?? new DocumentListResponseDto();
    }
}
