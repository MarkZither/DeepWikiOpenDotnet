using System.Text;
using System.Text.Json;
using DeepWiki.Rag.Core.Models;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Parses the JSON Table of Contents produced by the wiki TOC prompt.
/// </summary>
public class WikiTocParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Parses the TOC from a completed LLM response string.
    /// </summary>
    /// <param name="llmResponse">The full LLM response text (may include leading/trailing whitespace or code fences).</param>
    /// <returns>A flat list of <see cref="TocEntry"/> items ordered as sections then pages.</returns>
    /// <exception cref="WikiTocParseException">Thrown when the response cannot be parsed into a valid TOC.</exception>
    public IReadOnlyList<TocEntry> Parse(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            throw new WikiTocParseException("LLM response is empty.");

        var json = ExtractJson(llmResponse);

        TocJsonRoot? root;
        try
        {
            root = JsonSerializer.Deserialize<TocJsonRoot>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new WikiTocParseException($"TOC response is not valid JSON: {ex.Message}", ex);
        }

        if (root?.Sections is null || root.Sections.Count == 0)
            throw new WikiTocParseException("TOC JSON parsed but 'sections' array is missing or empty.");

        var entries = new List<TocEntry>();
        foreach (var section in root.Sections)
        {
            if (section.Pages is null)
                continue;

            foreach (var page in section.Pages)
            {
                if (string.IsNullOrWhiteSpace(page.Title))
                    continue;

                entries.Add(new TocEntry
                {
                    SectionPath = section.SectionPath ?? string.Empty,
                    PageTitle = page.Title.Trim(),
                    Keywords = page.Keywords?.AsReadOnly() ?? []
                });
            }
        }

        // Deduplicate: the LLM may emit multiple section objects with the same sectionPath,
        // or repeated page titles within a section. Keep only the first occurrence of each
        // (SectionPath, PageTitle) pair so the persisted stubs never produce duplicate TOC headings.
        var seen = new HashSet<(string Section, string Title)>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<TocEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (seen.Add((entry.SectionPath, entry.PageTitle)))
                deduped.Add(entry);
        }

        if (deduped.Count == 0)
            throw new WikiTocParseException("TOC contains no pages after parsing.");

        return deduped.AsReadOnly();
    }

    /// <summary>
    /// Strips optional markdown code fences and extracts the JSON object.
    /// </summary>
    private static string ExtractJson(string response)
    {
        var trimmed = response.Trim();

        // Strip ```json ... ``` or ``` ... ``` fences
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                trimmed = trimmed[..lastFence];
        }

        // Find the first { and last } to extract the JSON object
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
            throw new WikiTocParseException("Could not locate a JSON object in the LLM response.");

        return trimmed[start..(end + 1)];
    }
}

/// <summary>
/// Thrown when the TOC LLM response cannot be parsed into a valid <see cref="TocEntry"/> list.
/// </summary>
public class WikiTocParseException : Exception
{
    public WikiTocParseException(string message) : base(message) { }
    public WikiTocParseException(string message, Exception inner) : base(message, inner) { }
}
