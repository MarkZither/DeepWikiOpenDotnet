using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

/// <summary>
/// HTTP client for the documents management API:
///   POST /api/documents/ingest
///   GET  /api/documents?page=&pageSize=&repoUrl=
///   GET  /api/documents/{id}
///   DELETE /api/documents/{id}
/// </summary>
public class DocumentsApiClient
{
    private readonly HttpClient _httpClient;

    public DocumentsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Ingests a batch of documents via POST /api/documents/ingest.
    /// </summary>
    public async Task<IngestResponseDto> IngestAsync(
        IngestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync("/api/documents/ingest", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<IngestResponseDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return dto ?? new IngestResponseDto();
    }

    /// <summary>
    /// Lists documents with optional pagination and repository URL filter
    /// via GET /api/documents?page=&pageSize=&repoUrl=.
    /// </summary>
    public async Task<DocumentListResponseDto> ListAsync(
        int page = 1,
        int pageSize = 10,
        string? repoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var queryBuilder = HttpUtility.ParseQueryString(string.Empty);
        queryBuilder["page"] = page.ToString();
        queryBuilder["pageSize"] = pageSize.ToString();

        if (!string.IsNullOrWhiteSpace(repoUrl))
        {
            queryBuilder["repoUrl"] = repoUrl;
        }

        var url = $"/api/documents?{queryBuilder}";

        var result = await _httpClient
            .GetFromJsonAsync<DocumentListResponseDto>(url, cancellationToken)
            .ConfigureAwait(false);

        return result ?? new DocumentListResponseDto { Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// Deletes a document by ID via DELETE /api/documents/{id}.
    /// Returns true on 204 No Content, false on 404 Not Found.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .DeleteAsync($"/api/documents/{id}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return true;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return false;
    }

    /// <summary>
    /// Gets a single document by ID via GET /api/documents/{id}.
    /// Returns null when not found (404).
    /// </summary>
    public async Task<DocumentSummaryDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/documents/{id}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<DocumentSummaryDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
