using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

/// <summary>
/// Typed HTTP client for the wiki management API:
///   GET  /api/wiki/projects
///   GET  /api/wiki/{id}
///   POST /api/wiki
///   POST /api/wiki/{id}/pages
///   PUT  /api/wiki/{id}/pages/{pageId}
///   DELETE /api/wiki/{id}
///   DELETE /api/wiki/{id}/pages/{pageId}
/// </summary>
public class WikiApiClient
{
    private readonly HttpClient _httpClient;

    public WikiApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Returns a paginated list of all wiki projects via GET /api/wiki/projects.
    /// </summary>
    public async Task<PagedResultDto<WikiSummaryDto>> GetProjectsAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["page"] = page.ToString();
        query["pageSize"] = pageSize.ToString();

        var url = $"/api/wiki/projects?{query}";

        var result = await _httpClient
            .GetFromJsonAsync<PagedResultDto<WikiSummaryDto>>(url, cancellationToken)
            .ConfigureAwait(false);

        return result ?? new PagedResultDto<WikiSummaryDto> { Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// Returns a full wiki (with pages) by ID via GET /api/wiki/{id}.
    /// Returns null if not found (404).
    /// </summary>
    public async Task<WikiDto?> GetWikiByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient
                .GetFromJsonAsync<WikiDto>($"/api/wiki/{id}", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new wiki via POST /api/wiki.
    /// Returns the created wiki summary or null on failure.
    /// </summary>
    public async Task<WikiSummaryDto?> CreateWikiAsync(
        object request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync("/api/wiki", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<WikiSummaryDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a wiki by ID via DELETE /api/wiki/{id}.
    /// Returns true on success (204), false on 404.
    /// </summary>
    public async Task<bool> DeleteWikiAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .DeleteAsync($"/api/wiki/{id}", cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == System.Net.HttpStatusCode.NoContent;
    }

    /// <summary>
    /// Adds a page to a wiki via POST /api/wiki/{id}/pages.
    /// Returns the created page or null on failure.
    /// </summary>
    public async Task<WikiPageDto?> AddPageAsync(
        Guid wikiId,
        object request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync($"/api/wiki/{wikiId}/pages", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<WikiPageDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a wiki page via PUT /api/wiki/{wikiId}/pages/{pageId}.
    /// Returns the updated page or null on failure.
    /// </summary>
    public async Task<WikiPageDto?> UpdatePageAsync(
        Guid wikiId,
        Guid pageId,
        object request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PutAsJsonAsync($"/api/wiki/{wikiId}/pages/{pageId}", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content
            .ReadFromJsonAsync<WikiPageDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a wiki page via DELETE /api/wiki/{wikiId}/pages/{pageId}.
    /// Returns true on 204, false on 404.
    /// </summary>
    public async Task<bool> DeletePageAsync(
        Guid wikiId,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .DeleteAsync($"/api/wiki/{wikiId}/pages/{pageId}", cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == System.Net.HttpStatusCode.NoContent;
    }
}
