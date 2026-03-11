using System.Text.Json;
using System.Text.Unicode;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Parses the Markdown body and RELATED_PAGES footer produced by the wiki page prompt.
/// </summary>
public class WikiPageParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string RelatedPagesMarker = "RELATED_PAGES:";

    /// <summary>
    /// Splits the LLM page response into Markdown content and a list of related page titles.
    /// </summary>
    /// <param name="llmResponse">The full LLM response text.</param>
    /// <returns>A tuple of (MarkdownContent, RelatedPageTitles).</returns>
    public (string Content, IReadOnlyList<string> RelatedPageTitles) Parse(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return (string.Empty, []);

        // Find last occurrence of the marker to handle edge case where marker appears in content
        var markerIndex = llmResponse.LastIndexOf(RelatedPagesMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return (llmResponse.Trim(), []);

        var content = llmResponse[..markerIndex].TrimEnd();
        var relatedPart = llmResponse[(markerIndex + RelatedPagesMarker.Length)..].Trim();

        var titles = ParseRelatedPagesList(relatedPart);
        return (content, titles);
    }

    private static IReadOnlyList<string> ParseRelatedPagesList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            // Normalise: find the first [ and last ]
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start)
                return [];

            var arrayJson = json[start..(end + 1)];
            var titles = JsonSerializer.Deserialize<List<string>>(arrayJson, _jsonOptions);
            if (titles is null)
                return [];

            return titles
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToList()
                .AsReadOnly();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
