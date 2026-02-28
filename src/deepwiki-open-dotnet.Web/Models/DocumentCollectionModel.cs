using System;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Represents a document collection returned from GET /api/documents.
/// Matches the DocumentCollection schema in DocumentListResponse.schema.json.
/// </summary>
public class DocumentCollectionModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("document_count")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }
}
