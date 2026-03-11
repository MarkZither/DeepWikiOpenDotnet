using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

/// <summary>
/// Snapshot-based tests for <see cref="WikiPageParser"/>.
/// Tests replay the page-v1.0.0.json snapshot stream and verify correct splitting.
/// </summary>
public class WikiPageParserTests
{
    private readonly WikiPageParser _parser = new();

    // ── Snapshot fixture (mirrors llm-snapshots/wiki/page-v1.0.0.json) ──────

    private const string PageSnapshotResponse =
        "## Data Layer\n\n" +
        "The data layer is the foundation of the DeepWiki system, providing a unified persistence API across multiple database providers.\n\n" +
        "### Overview\n\n" +
        "DeepWiki uses **Entity Framework Core** with a dual-provider architecture supporting both PostgreSQL and SQL Server. The design separates entity definitions from provider-specific configurations.\n\n" +
        "### Key Components\n\n" +
        "- **`DeepWiki.Data.Abstractions`** — entity classes, interfaces, and shared enumerations\n" +
        "- **`DeepWiki.Data.Postgres`** — PostgreSQL-specific EF Core configurations and migrations\n\n" +
        "### Entities\n\n" +
        "| Entity | Purpose |\n" +
        "|---|---|\n" +
        "| `WikiEntity` | Wiki aggregate root |\n\n" +
        "\nRELATED_PAGES: [\"Service Layer\", \"Configuration\"]";

    // ── Snapshot replay: happy path ──────────────────────────────────────────

    [Fact]
    public void Parse_SnapshotPageStream_ExtractsTwoRelatedPages()
    {
        var (_, relatedPages) = _parser.Parse(PageSnapshotResponse);

        relatedPages.Should().HaveCount(2);
        relatedPages.Should().Contain("Service Layer");
        relatedPages.Should().Contain("Configuration");
    }

    [Fact]
    public void Parse_SnapshotPageStream_ContentDoesNotContainRelatedPagesMarker()
    {
        var (content, _) = _parser.Parse(PageSnapshotResponse);

        content.Should().NotContain("RELATED_PAGES:");
    }

    [Fact]
    public void Parse_SnapshotPageStream_ContentStartsWithHeading()
    {
        var (content, _) = _parser.Parse(PageSnapshotResponse);

        content.Should().StartWith("## Data Layer");
    }

    [Fact]
    public void Parse_StreamTokensJoined_SplitsCorrectly()
    {
        // Replay as the orchestrator does — join all streamed tokens
        var tokens = new[]
        {
            "## Data Layer\n\n",
            "The data layer is the foundation.\n\n",
            "### Overview\n\n",
            "Details here.\n",
            "\nRELATED_PAGES: [\"Service Layer\", \"Configuration\"]"
        };
        var joined = string.Join("", tokens);

        var (content, related) = _parser.Parse(joined);

        content.Should().Contain("## Data Layer");
        content.Should().NotContain("RELATED_PAGES:");
        related.Should().HaveCount(2);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NoRelatedPagesMarker_ReturnsFullContentAndEmptyList()
    {
        var response = "## My Page\n\nThis page has no related pages listed.";

        var (content, related) = _parser.Parse(response);

        content.Should().Be("## My Page\n\nThis page has no related pages listed.");
        related.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyRelatedPagesArray_ReturnsEmptyList()
    {
        var response = "## My Page\n\nContent here.\nRELATED_PAGES: []";

        var (_, related) = _parser.Parse(response);

        related.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RelatedPagesWithSpecialCharacterTitles_PreservesTitles()
    {
        var response = "## Page\n\nContent.\nRELATED_PAGES: [\"C# Basics\", \"I/O & Streams\", \"F#/ML\"]";

        var (_, related) = _parser.Parse(response);

        related.Should().HaveCount(3);
        related.Should().Contain("C# Basics");
        related.Should().Contain("I/O & Streams");
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyContentAndEmptyList()
    {
        var (content, related) = _parser.Parse("");

        content.Should().BeEmpty();
        related.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RelatedPagesMarkerAppearsInContent_UsesLastOccurrence()
    {
        // Marker appears in the content text AND as the footer
        var response =
            "## Page\n\nNote: the footer line starts with RELATED_PAGES: here.\n\nMore content.\n" +
            "RELATED_PAGES: [\"Final Page\"]";

        var (content, related) = _parser.Parse(response);

        // Only the LAST marker is used for splitting
        related.Should().HaveCount(1);
        related.Should().Contain("Final Page");
        content.Should().Contain("RELATED_PAGES: here");
    }

    [Fact]
    public void Parse_MalformedRelatedPagesJson_ReturnsEmptyList()
    {
        var response = "## Page\n\nContent.\nRELATED_PAGES: not valid json";

        var (content, related) = _parser.Parse(response);

        content.Should().Contain("## Page");
        related.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RelatedPagesWithWhitespaceOnlyEntries_FiltersEmptyTitles()
    {
        var response = "## Page\n\nContent.\nRELATED_PAGES: [\"Valid Title\", \"\", \"  \"]";

        var (_, related) = _parser.Parse(response);

        related.Should().HaveCount(1);
        related.Should().Contain("Valid Title");
    }

    [Fact]
    public void Parse_MarkerIsCaseInsensitive_StillSplits()
    {
        var response = "## Page\n\nContent.\nrelated_pages: [\"Other Page\"]";

        var (_, related) = _parser.Parse(response);

        related.Should().HaveCount(1);
        related.Should().Contain("Other Page");
    }
}
