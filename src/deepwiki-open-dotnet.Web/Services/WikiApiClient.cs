using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Starts wiki generation via POST /api/wiki/generate.
    /// Returns the <see cref="Guid"/> wiki ID from the 202 Accepted response; all
    /// subsequent progress events arrive via SignalR (<see cref="WikiProgressHubClient"/>).
    /// Throws <see cref="HttpRequestException"/> with <see cref="System.Net.HttpStatusCode.Conflict"/>
    /// when a generation is already in progress for the same collection + name.
    /// </summary>
    public async Task<Guid> StartGenerationAsync(
        object request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync("/api/wiki/generate", request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new HttpRequestException("Conflict", null, System.Net.HttpStatusCode.Conflict);

        response.EnsureSuccessStatusCode();

        var body = await response.Content
            .ReadFromJsonAsync<StartGenerationResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return body?.WikiId ?? throw new InvalidOperationException("API returned 202 but no wikiId in response body.");
    }

    /// <summary>
    /// Exports a wiki as Markdown or JSON via POST /api/wiki/export.
    /// Returns the raw response stream for download; the caller is responsible for disposing it.
    /// </summary>
    /// <param name="wikiId">ID of the wiki to export.</param>
    /// <param name="format">Export format: <c>"markdown"</c> or <c>"json"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw content stream, or <c>null</c> if the wiki was not found (404).</returns>
    public async Task<Stream?> ExportWikiAsync(
        Guid wikiId,
        string format,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync("/api/wiki/export", new { wikiId, format }, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record StartGenerationResponse(Guid WikiId);
}
