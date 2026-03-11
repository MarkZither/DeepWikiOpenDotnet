using System;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Web-layer DTO mirroring <c>DeepWiki.Rag.Core.Models.WikiGenerationProgress</c>.
/// Deserialized from NDJSON streamed by POST /api/wiki/generate.
/// </summary>
public class WikiGenerationProgressDto
{
    // ── Event type constants ──────────────────────────────────────────────────
    public const string WikiCreated        = "wiki_created";
    public const string TocComplete        = "toc_complete";
    public const string PageStart          = "page_start";
    public const string PageToken          = "page_token";
    public const string PageComplete       = "page_complete";
    public const string PageError          = "page_error";
    public const string GenerationComplete = "generation_complete";
    public const string GenerationCancelled = "generation_cancelled";

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("wikiId")]
    public Guid WikiId { get; set; }

    [JsonPropertyName("pageIndex")]
    public int PageIndex { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("pageTitle")]
    public string? PageTitle { get; set; }

    [JsonPropertyName("tokenText")]
    public string? TokenText { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
