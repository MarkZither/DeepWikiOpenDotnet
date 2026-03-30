using System.Text;
using System.Text.Json;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Rag.Core.Observability;

namespace DeepWiki.Rag.Core.Services;

/// <inheritdoc cref="IWikiExportService"/>
public sealed class WikiExportService : IWikiExportService
{
    private readonly WikiMetrics? _metrics;

    public WikiExportService(WikiMetrics? metrics = null)
    {
        _metrics = metrics;
    }
    /// <inheritdoc/>
    public async Task ExportAsMarkdownAsync(
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        // ── Header ──────────────────────────────────────────────────────────
        await writer.WriteLineAsync($"# {wiki.Name}");
        if (!string.IsNullOrWhiteSpace(wiki.Description))
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(wiki.Description);
        }
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"*Collection: {wiki.CollectionId} | Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC*");
        await writer.WriteLineAsync();

        if (pages.Count == 0)
        {
            await writer.WriteLineAsync("> No pages have been generated for this wiki.");
            await writer.FlushAsync(cancellationToken);
            return;
        }

        // ── Table of Contents ────────────────────────────────────────────────
        await writer.WriteLineAsync("## Table of Contents");
        await writer.WriteLineAsync();
        foreach (var page in pages.OrderBy(p => p.SortOrder))
        {
            var anchor = ToAnchor(page.Title);
            var indent = GetIndentLevel(page.SectionPath);
            await writer.WriteLineAsync($"{indent}- [{page.Title}](#{anchor})");
        }
        await writer.WriteLineAsync();

        // ── Pages ────────────────────────────────────────────────────────────
        foreach (var page in pages.OrderBy(p => p.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteLineAsync($"## {page.Title}");
            await writer.WriteLineAsync();

            if (!string.IsNullOrWhiteSpace(page.SectionPath))
            {
                await writer.WriteLineAsync($"*Section: {page.SectionPath}*");
                await writer.WriteLineAsync();
            }

            await writer.WriteLineAsync(page.Content);
            await writer.WriteLineAsync();

            // Related pages section
            if (relatedPages.TryGetValue(page.Id, out var related) && related.Count > 0)
            {
                await writer.WriteLineAsync("### Related Pages");
                await writer.WriteLineAsync();
                foreach (var rel in related)
                {
                    if (rel is null || rel.Id == Guid.Empty)
                        await writer.WriteLineAsync("- *(page removed)*");
                    else
                        await writer.WriteLineAsync($"- [{rel.Title}](#{ToAnchor(rel.Title)})");
                }
                await writer.WriteLineAsync();
            }

            await writer.WriteLineAsync("---");
            await writer.WriteLineAsync();
        }

        await writer.FlushAsync(cancellationToken);
        _metrics?.RecordExport("markdown");
    }

    /// <inheritdoc/>
    public async Task ExportAsJsonAsync(
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        var writerOptions = new JsonWriterOptions { Indented = false };
        await using var jsonWriter = new Utf8JsonWriter(output, writerOptions);

        jsonWriter.WriteStartObject();

        // ── Metadata ─────────────────────────────────────────────────────────
        jsonWriter.WriteStartObject("metadata");
        jsonWriter.WriteString("name", wiki.Name);
        jsonWriter.WriteString("description", wiki.Description);
        jsonWriter.WriteString("collectionId", wiki.CollectionId);
        jsonWriter.WriteString("exportDate", DateTime.UtcNow.ToString("O"));
        jsonWriter.WriteNumber("pageCount", pages.Count);
        jsonWriter.WriteString("status", wiki.Status.ToString());
        jsonWriter.WriteEndObject();

        // ── Pages ─────────────────────────────────────────────────────────────
        jsonWriter.WriteStartArray("pages");
        foreach (var page in pages.OrderBy(p => p.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("id", page.Id.ToString());
            jsonWriter.WriteString("title", page.Title);
            jsonWriter.WriteString("content", page.Content);
            jsonWriter.WriteString("sectionPath", page.SectionPath);
            jsonWriter.WriteNumber("sortOrder", page.SortOrder);
            jsonWriter.WriteString("status", page.Status.ToString());

            // Related pages
            jsonWriter.WriteStartArray("relatedPages");
            if (relatedPages.TryGetValue(page.Id, out var related))
            {
                foreach (var rel in related)
                {
                    if (rel is null || rel.Id == Guid.Empty)
                    {
                        jsonWriter.WriteStartObject();
                        jsonWriter.WriteNull("id");
                        jsonWriter.WriteString("title", "(page removed)");
                        jsonWriter.WriteEndObject();
                    }
                    else
                    {
                        jsonWriter.WriteStartObject();
                        jsonWriter.WriteString("id", rel.Id.ToString());
                        jsonWriter.WriteString("title", rel.Title);
                        jsonWriter.WriteString("sectionPath", rel.SectionPath);
                        jsonWriter.WriteEndObject();
                    }
                }
            }
            jsonWriter.WriteEndArray();

            jsonWriter.WriteEndObject();
        }
        jsonWriter.WriteEndArray();

        jsonWriter.WriteEndObject();

        await jsonWriter.FlushAsync(cancellationToken);
        _metrics?.RecordExport("json");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a page title to a GitHub-Flavored Markdown anchor ID.
    /// e.g. "Getting Started" → "getting-started"
    /// </summary>
    private static string ToAnchor(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == ' ' || ch == '-')
                sb.Append('-');
            // skip other special characters
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Returns Markdown list indentation based on SectionPath depth.
    /// A path "A/B/C" has depth 3, so indent = "  " (2 spaces per level beyond depth 1).
    /// </summary>
    private static string GetIndentLevel(string sectionPath)
    {
        if (string.IsNullOrWhiteSpace(sectionPath))
            return string.Empty;

        var depth = sectionPath.Count(c => c == '/');
        return depth == 0 ? string.Empty : new string(' ', depth * 2);
    }
}
