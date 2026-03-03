using System;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Lightweight wiki summary DTO for list views.
/// Mirrors DeepWiki.ApiService.Models.WikiSummaryResponse returned by GET /api/wiki/projects.
/// </summary>
public class WikiSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("collectionSource")]
    public string CollectionSource { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
}
