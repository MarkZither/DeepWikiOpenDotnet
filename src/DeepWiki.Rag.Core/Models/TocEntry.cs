using System.Text.Json.Serialization;

namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Represents a single entry from the LLM-generated Table of Contents.
/// </summary>
public class TocEntry
{
    /// <summary>Section path (e.g., "Architecture/Data Model").</summary>
    public string SectionPath { get; set; } = string.Empty;

    /// <summary>Page title within the section.</summary>
    public string PageTitle { get; set; } = string.Empty;

    /// <summary>Keywords used for RAG-scoped retrieval during page content generation.</summary>
    public IReadOnlyList<string> Keywords { get; set; } = [];
}

/// <summary>Internal JSON shape emitted by the TOC prompt.</summary>
internal class TocJsonRoot
{
    [JsonPropertyName("sections")]
    public List<TocJsonSection>? Sections { get; set; }
}

internal class TocJsonSection
{
    [JsonPropertyName("sectionPath")]
    public string SectionPath { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public List<TocJsonPage>? Pages { get; set; }
}

internal class TocJsonPage
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }
}
