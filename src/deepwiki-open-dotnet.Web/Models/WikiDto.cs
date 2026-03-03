using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Full wiki response DTO for the web layer.
/// Mirrors DeepWiki.ApiService.Models.WikiResponse returned by GET /api/wiki/{id}.
/// </summary>
public class WikiDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("collectionId")]
    public string CollectionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public IReadOnlyList<WikiPageDto> Pages { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Wiki page DTO for the web layer.
/// Mirrors DeepWiki.ApiService.Models.WikiPageResponse.
/// </summary>
public class WikiPageDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("wikiId")]
    public Guid WikiId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("sectionPath")]
    public string SectionPath { get; set; } = string.Empty;

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("parentPageId")]
    public Guid? ParentPageId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("relatedPages")]
    public IReadOnlyList<RelatedPageSummaryDto> RelatedPages { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Lightweight related-page reference.</summary>
public class RelatedPageSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}
