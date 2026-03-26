using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace deepwiki_open_dotnet.Web.Models;

/// <summary>
/// Represents a node in the wiki sidebar tree.
/// Section nodes are non-clickable folders; page nodes are clickable leaves.
/// </summary>
public class WikiPageNode
{
    /// <summary>Display label for this node.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>True when this node represents a page (leaf), false for a section folder.</summary>
    public bool IsPage { get; init; }

    /// <summary>Non-null only when <see cref="IsPage"/> is true.</summary>
    public Guid? PageId { get; init; }

    /// <summary>Whether a section node is expanded. Pages are always expanded (N/A).</summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>Child nodes under this section folder.</summary>
    public List<WikiPageNode> Children { get; init; } = [];

    /// <summary>
    /// Builds a tree from a flat list of page DTOs using SectionPath as the folder hierarchy
    /// and Title as the leaf node label. A page with SectionPath "Architecture" and Title
    /// "Data Model" becomes a leaf "Data Model" inside a folder "Architecture".
    /// SectionPath may contain "/" for deeper nesting (e.g. "Architecture/Patterns").
    /// Pages with an empty SectionPath are placed at the root level.
    /// </summary>
    public static List<WikiPageNode> BuildTree(IEnumerable<WikiPageDto> pages)
    {
        var root = new List<WikiPageNode>();
        var sectionIndex = new Dictionary<string, WikiPageNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pages)
        {
            // SectionPath is the folder hierarchy; Title is the leaf node label.
            // Combine them so "Architecture" + "Data Model" → "Architecture/Data Model",
            // which renders as a folder "Architecture" containing a leaf "Data Model".
            var section = page.SectionPath?.Trim('/');
            var path = string.IsNullOrEmpty(section)
                ? page.Title
                : $"{section}/{page.Title}";
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                segments = [page.Title];

            if (segments.Length == 1)
            {
                // Top-level page — no section wrapper
                root.Add(new WikiPageNode
                {
                    Label  = segments[0],
                    IsPage = true,
                    PageId = page.Id
                });
                continue;
            }

            // Navigate / create section nodes for all but the last segment
            var currentList = root;
            var keyBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (keyBuilder.Length > 0) keyBuilder.Append('/');
                keyBuilder.Append(segments[i]);
                var key = keyBuilder.ToString();

                if (!sectionIndex.TryGetValue(key, out var sectionNode))
                {
                    sectionNode = new WikiPageNode
                    {
                        Label      = segments[i],
                        IsPage     = false,
                        IsExpanded = true
                    };
                    currentList.Add(sectionNode);
                    sectionIndex[key] = sectionNode;
                }

                currentList = sectionNode.Children;
            }

            // Add the leaf page
            currentList.Add(new WikiPageNode
            {
                Label  = segments[^1],
                IsPage = true,
                PageId = page.Id
            });
        }

        return root;
    }
}
