using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Response DTO from GET /api/documents.
/// Supports both the collections view (for scope selector) and the paginated
/// document list view (for document management).
/// </summary>
public class DocumentListResponseDto
{
    // ── Collections view (used by ChatApiClient.GetCollectionsAsync) ──────────
    [JsonPropertyName("collections")]
    public List<DocumentCollectionModel> Collections { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    // ── Paginated document list (used by DocumentsApiClient.ListAsync) ────────
    [JsonPropertyName("items")]
    public List<DocumentSummaryDto> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int DocumentTotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
