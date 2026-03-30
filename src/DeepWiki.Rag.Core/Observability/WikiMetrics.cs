using System.Diagnostics.Metrics;
using DeepWiki.Data.Abstractions.Observability;

namespace DeepWiki.Rag.Core.Observability;

/// <summary>
/// OpenTelemetry metrics instrumentation for the wiki subsystem.
/// Tracks wiki creation, page generation, generation duration, and export counts.
/// </summary>
public class WikiMetrics
{
    private readonly Counter<long> _wikiCreated;
    private readonly Counter<long> _pagesGenerated;
    private readonly Histogram<double> _generationDurationSeconds;
    private readonly Counter<long> _exportCount;

    public WikiMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ObservabilityConstants.WikiMeterName, ObservabilityConstants.WikiMeterVersion);

        _wikiCreated = meter.CreateCounter<long>(
            "deepwiki.wiki.created",
            description: "Number of wikis created");

        _pagesGenerated = meter.CreateCounter<long>(
            "deepwiki.wiki.pages_generated",
            description: "Number of wiki pages generated");

        _generationDurationSeconds = meter.CreateHistogram<double>(
            "deepwiki.wiki.generation_duration_seconds",
            unit: "s",
            description: "Duration of the wiki generation pipeline from start to completion");

        _exportCount = meter.CreateCounter<long>(
            "deepwiki.wiki.export_count",
            description: "Number of wiki exports performed");
    }

    /// <summary>Records that a new wiki was created.</summary>
    public void RecordWikiCreated() => _wikiCreated.Add(1);

    /// <summary>Records that a wiki page was generated.</summary>
    /// <param name="status">Outcome status: "ok" or "error".</param>
    public void RecordPageGenerated(string status) =>
        _pagesGenerated.Add(1, new KeyValuePair<string, object?>("status", status));

    /// <summary>Records the total duration of a wiki generation run.</summary>
    /// <param name="seconds">Elapsed time in seconds.</param>
    public void RecordGenerationDuration(double seconds) =>
        _generationDurationSeconds.Record(seconds);

    /// <summary>Records that a wiki was exported.</summary>
    /// <param name="format">Export format: "markdown" or "json".</param>
    public void RecordExport(string format) =>
        _exportCount.Add(1, new KeyValuePair<string, object?>("format", format));
}
