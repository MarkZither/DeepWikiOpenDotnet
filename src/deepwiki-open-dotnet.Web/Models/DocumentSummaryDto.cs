using System;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Summary of a single document as returned by GET /api/documents (paginated list).
/// Excludes full text and embedding vectors for performance.
/// </summary>
public class DocumentSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("repoUrl")]
    public string RepoUrl { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("tokenCount")]
    public int TokenCount { get; set; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("isCode")]
    public bool IsCode { get; set; }
}
