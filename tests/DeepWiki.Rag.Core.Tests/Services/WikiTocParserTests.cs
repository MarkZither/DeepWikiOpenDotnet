using System.Text.Json;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

/// <summary>
/// Snapshot-based tests for <see cref="WikiTocParser"/>.
/// Tests replay the toc-v1.0.0.json snapshot stream and verify correct extraction.
/// </summary>
public class WikiTocParserTests
{
    private readonly WikiTocParser _parser = new();

    // ── Snapshot fixture (mirrors llm-snapshots/wiki/toc-v1.0.0.json) ──────

    private const string TocSnapshotStream = """
        {
          "sections": [
            {
              "sectionPath": "Getting Started",
              "pages": [
                { "title": "Overview", "keywords": ["introduction", "setup", "quickstart"] },
                { "title": "Configuration", "keywords": ["config", "appsettings", "environment"] }
              ]
            },
            {
              "sectionPath": "Architecture",
              "pages": [
                { "title": "Data Layer", "keywords": ["entity", "database", "repository", "postgres", "sql"] },
                { "title": "Service Layer", "keywords": ["service", "dependency injection", "rag"] }
              ]
            }
          ]
        }
        """;

    // ── Happy-path snapshot replay ──────────────────────────────────────────

    [Fact]
    public void Parse_SnapshotTocStream_ExtractsFourPages()
    {
        var result = _parser.Parse(TocSnapshotStream);

        result.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_SnapshotTocStream_FirstEntryHasCorrectSectionAndTitle()
    {
        var result = _parser.Parse(TocSnapshotStream);

        result[0].SectionPath.Should().Be("Getting Started");
        result[0].PageTitle.Should().Be("Overview");
    }

    [Fact]
    public void Parse_SnapshotTocStream_SecondSectionPagesHaveCorrectSectionPath()
    {
        var result = _parser.Parse(TocSnapshotStream);

        result[2].SectionPath.Should().Be("Architecture");
        result[2].PageTitle.Should().Be("Data Layer");
        result[3].SectionPath.Should().Be("Architecture");
        result[3].PageTitle.Should().Be("Service Layer");
    }

    [Fact]
    public void Parse_SnapshotTocStream_KeywordsArePopulated()
    {
        var result = _parser.Parse(TocSnapshotStream);

        result[0].Keywords.Should().Contain("introduction");
        result[2].Keywords.Should().Contain("entity");
    }

    // ── Streaming token replay (joins token array like the orchestrator does) ──

    [Fact]
    public void Parse_StreamTokensJoined_ProducesValidResult()
    {
        // Replay the stream as the orchestrator would — by joining all tokens
        var tokens = new[]
        {
            "{",
            "\n  \"sections\": [",
            "\n    {",
            "\n      \"sectionPath\": \"Getting Started\",",
            "\n      \"pages\": [",
            "\n        { \"title\": \"Overview\", \"keywords\": [\"introduction\", \"setup\", \"quickstart\"] },",
            "\n        { \"title\": \"Configuration\", \"keywords\": [\"config\", \"appsettings\", \"environment\"] }",
            "\n      ]",
            "\n    }",
            "\n  ]",
            "\n}"
        };
        var joined = string.Join("", tokens);

        var result = _parser.Parse(joined);

        result.Should().HaveCount(2);
        result[0].PageTitle.Should().Be("Overview");
    }

    // ── Parse failure cases ─────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ThrowsWikiTocParseException()
    {
        var act = () => _parser.Parse("");

        act.Should().Throw<WikiTocParseException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsWikiTocParseException()
    {
        var act = () => _parser.Parse("{ this is not valid json }");

        act.Should().Throw<WikiTocParseException>();
    }

    [Fact]
    public void Parse_MissingSectionsArray_ThrowsWikiTocParseException()
    {
        var json = """{ "result": "ok" }""";

        var act = () => _parser.Parse(json);

        act.Should().Throw<WikiTocParseException>()
            .WithMessage("*sections*");
    }

    [Fact]
    public void Parse_EmptySectionsArray_ThrowsWikiTocParseException()
    {
        var json = """{ "sections": [] }""";

        var act = () => _parser.Parse(json);

        act.Should().Throw<WikiTocParseException>()
            .WithMessage("*sections*");
    }

    [Fact]
    public void Parse_SectionsWithNullPages_ThrowsWikiTocParseException()
    {
        var json = """{ "sections": [{ "sectionPath": "S1" }] }""";

        var act = () => _parser.Parse(json);

        // All sections have no pages → no entries → should throw
        act.Should().Throw<WikiTocParseException>()
            .WithMessage("*no pages*");
    }

    [Fact]
    public void Parse_ResponseWrappedInCodeFences_StillParses()
    {
        var fenced = $"```json\n{TocSnapshotStream}\n```";

        var result = _parser.Parse(fenced);

        result.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_ResponseWithLeadingProseBeforeJson_ExtractsJsonObject()
    {
        var withProse = "Here is the TOC:\n\n" + TocSnapshotStream;

        var result = _parser.Parse(withProse);

        result.Should().HaveCount(4);
    }

    // ── Retry-trigger contract ──────────────────────────────────────────────

    [Theory]
    [InlineData("Sorry, I cannot generate a TOC.")]
    [InlineData("{ \"error\": \"rate limited\" }")]
    public void Parse_NonTocResponses_ThrowWikiTocParseException(string response)
    {
        var act = () => _parser.Parse(response);

        act.Should().Throw<WikiTocParseException>();
    }
}
