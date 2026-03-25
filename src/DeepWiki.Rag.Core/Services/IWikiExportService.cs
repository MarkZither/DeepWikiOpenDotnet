using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Exports a wiki to a single downloadable file (Markdown or JSON).
/// </summary>
public interface IWikiExportService
{
    /// <summary>
    /// Writes the wiki as a Markdown document (with TOC, headings, and Related Pages sections)
    /// to <paramref name="output"/>.
    /// </summary>
    /// <param name="wiki">The wiki metadata (name, description, etc.).</param>
    /// <param name="pages">Ordered list of pages to include.</param>
    /// <param name="relatedPages">Map of page ID → related page entities.</param>
    /// <param name="output">Destination stream (must be writable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExportAsMarkdownAsync(
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages,
        Stream output,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the wiki as a structured JSON document (metadata + pages array)
    /// to <paramref name="output"/> using streaming <see cref="System.Text.Json.Utf8JsonWriter"/>.
    /// </summary>
    /// <param name="wiki">The wiki metadata.</param>
    /// <param name="pages">Ordered list of pages to include.</param>
    /// <param name="relatedPages">Map of page ID → related page entities.</param>
    /// <param name="output">Destination stream (must be writable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExportAsJsonAsync(
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages,
        Stream output,
        CancellationToken cancellationToken = default);
}
