using System.Text.Json.Serialization;

namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// A single progress event emitted by <see cref="Services.IWikiGenerationService.GenerateAsync"/>
/// and streamed to clients as NDJSON lines.
/// </summary>
public class WikiGenerationProgress
{
    // ── Event types (string constants for JSON compatibility) ──────────────

    /// <summary>Wiki shell created and persisted (Status: Generating).</summary>
    public const string EventWikiCreated = "wiki_created";

    /// <summary>TOC generation complete — page stubs persisted.</summary>
    public const string EventTocComplete = "toc_complete";

    /// <summary>A page generation pass has started.</summary>
    public const string EventPageStart = "page_start";

    /// <summary>A streaming token chunk from the TOC LLM call (carries cumulative token count).</summary>
    public const string EventTocToken = "toc_token";

    /// <summary>A streaming token chunk from the page generation LLM call.</summary>
    public const string EventPageToken = "page_token";

    /// <summary>A page has been generated and persisted successfully.</summary>
    public const string EventPageComplete = "page_complete";

    /// <summary>A page generation failed; pipeline continues with next page.</summary>
    public const string EventPageError = "page_error";

    /// <summary>All pages processed — wiki status updated to Complete or Partial.</summary>
    public const string EventGenerationComplete = "generation_complete";

    /// <summary>Generation cancelled by the caller — partial results persisted.</summary>
    public const string EventGenerationCancelled = "generation_cancelled";

    // ── Properties ─────────────────────────────────────────────────────────

    /// <summary>
    /// Event classification.
    /// One of: wiki_created, toc_complete, page_start, page_token,
    /// page_complete, page_error, generation_complete, generation_cancelled.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>The ID of the wiki being generated.</summary>
    [JsonPropertyName("wikiId")]
    public Guid WikiId { get; set; }

    /// <summary>0-based index of the page this event relates to (null for wiki-level events).</summary>
    [JsonPropertyName("pageIndex")]
    public int? PageIndex { get; set; }

    /// <summary>Total page count (set once TOC is complete).</summary>
    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; set; }

    /// <summary>Title of the page this event relates to.</summary>
    [JsonPropertyName("pageTitle")]
    public string? PageTitle { get; set; }

    /// <summary>Streaming token text (only for page_token events).</summary>
    [JsonPropertyName("tokenText")]
    public string? TokenText { get; set; }

    /// <summary>Error message (only for page_error and generation_cancelled events).</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>Wiki status string at completion (only for generation_complete / generation_cancelled).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Cumulative token count (only for toc_token events).</summary>
    [JsonPropertyName("tokenCount")]
    public int? TokenCount { get; set; }
}
