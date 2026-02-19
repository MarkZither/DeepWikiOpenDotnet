using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Response DTO from GET /api/documents.
/// Matches DocumentListResponse.schema.json.
/// </summary>
public class DocumentListResponseDto
{
    [JsonPropertyName("collections")]
    public List<DocumentCollectionModel> Collections { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}
