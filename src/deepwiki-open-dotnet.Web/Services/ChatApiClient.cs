using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;
using Microsoft.Extensions.Logging;

namespace deepwiki_open_dotnet.Web.Services;

public class ChatApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatApiClient> _logger;

    public ChatApiClient(HttpClient httpClient, ILogger<ChatApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SessionResponseDto> CreateSessionAsync(SessionRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating generation session for owner {Owner}", request.Owner);
        try
        {
            var resp = await _httpClient.PostAsJsonAsync("/api/generation/session", request, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var dto = await resp.Content.ReadFromJsonAsync<SessionResponseDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = dto ?? throw new InvalidOperationException("Empty session response");
            _logger.LogInformation("Session created: {SessionId}", result.SessionId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create generation session");
            throw;
        }
    }

    /// <summary>
    /// Calls the streaming generation endpoint and returns the HttpResponseMessage so the caller can read the response stream.
    /// </summary>
    public async Task<HttpResponseMessage> StreamGenerationAsync(GenerationRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting streaming generation for session {SessionId}, prompt length {Length}",
            request.SessionId, request.Prompt.Length);
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/generation/stream", request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Streaming generation returned non-success status {StatusCode} for session {SessionId}",
                    (int)response.StatusCode, request.SessionId);
            }
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming generation canceled for session {SessionId}", request.SessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming generation failed for session {SessionId}", request.SessionId);
            throw;
        }
    }

    /// <summary>
    /// Fetches available document collections from GET /api/documents/collections.
    /// </summary>
    public async Task<DocumentListResponseDto> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching document collections");
        try
        {
            var result = await _httpClient.GetFromJsonAsync<DocumentListResponseDto>("/api/documents/collections", cancellationToken).ConfigureAwait(false);
            return result ?? new DocumentListResponseDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch document collections");
            throw;
        }
    }
}
