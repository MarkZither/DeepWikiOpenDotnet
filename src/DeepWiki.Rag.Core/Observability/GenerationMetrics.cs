using System.Diagnostics;
using System.Diagnostics.Metrics;
using DeepWiki.Data.Abstractions.Observability;

namespace DeepWiki.Rag.Core.Observability;

/// <summary>
/// OpenTelemetry metrics instrumentation for generation service.
/// Tracks time-to-first-token (TTF), token throughput, token counts, and error rates.
/// </summary>
public class GenerationMetrics
{
    private readonly Meter _meter;
    private readonly Histogram<double> _ttfHistogram;
    private readonly Counter<long> _tokenCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _tokensPerSecond;

    public GenerationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(ObservabilityConstants.GenerationMeterName, ObservabilityConstants.GenerationMeterVersion);

        // Time-to-first-token histogram (milliseconds)
        _ttfHistogram = _meter.CreateHistogram<double>(
            "generation.ttf",
            unit: "ms",
            description: "Time from prompt submission to first token received");

        // Token counter (total tokens generated)
        _tokenCounter = _meter.CreateCounter<long>(
            "generation.tokens",
            unit: "tokens",
            description: "Total tokens generated");

        // Error counter (by error type)
        _errorCounter = _meter.CreateCounter<long>(
            "generation.errors",
            unit: "errors",
            description: "Total generation errors");

        // Tokens per second histogram
        _tokensPerSecond = _meter.CreateHistogram<double>(
            "generation.tokens_per_second",
            unit: "tokens/s",
            description: "Token generation throughput");
    }

    /// <summary>
    /// Records time-to-first-token measurement.
    /// </summary>
    /// <param name="elapsedMs">Time in milliseconds from prompt submission to first token.</param>
    /// <param name="provider">Provider name (e.g., "Ollama", "OpenAI").</param>
    private long _totalTokens = 0;
    private long _totalErrors = 0;
    private double _lastTtfMs = 0;

    public void RecordTimeToFirstToken(double elapsedMs, string provider)
    {
        _ttfHistogram.Record(elapsedMs, new KeyValuePair<string, object?>("provider", provider));
        _lastTtfMs = elapsedMs;
    }

    /// <summary>
    /// Increments token counter.
    /// </summary>
    /// <param name="tokenCount">Number of tokens generated.</param>
    /// <param name="provider">Provider name.</param>
    public void RecordTokens(long tokenCount, string provider)
    {
        _tokenCounter.Add(tokenCount, new KeyValuePair<string, object?>("provider", provider));
        System.Threading.Interlocked.Add(ref _totalTokens, tokenCount);
    }

    /// <summary>
    /// Records token throughput (tokens/second).
    /// </summary>
    /// <param name="tokensPerSec">Throughput rate.</param>
    /// <param name="provider">Provider name.</param>
    public void RecordTokensPerSecond(double tokensPerSec, string provider)
    {
        _tokensPerSecond.Record(tokensPerSec, new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Increments error counter.
    /// </summary>
    /// <param name="errorType">Error type (e.g., "timeout", "unavailable", "cancelled").</param>
    /// <param name="provider">Provider name.</param>
    public void RecordError(string errorType, string provider)
    {
        _errorCounter.Add(1,
            new KeyValuePair<string, object?>("error_type", errorType),
            new KeyValuePair<string, object?>("provider", provider));
        System.Threading.Interlocked.Increment(ref _totalErrors);
    }

    /// <summary>
    /// Exports a minimal Prometheus-format text snapshot suitable for tests.
    /// </summary>
    public string ExportPrometheusMetrics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# HELP generation_tokens_total Total tokens generated");
        sb.AppendLine("# TYPE generation_tokens_total counter");
        sb.AppendLine($"generation_tokens_total {_totalTokens}");
        sb.AppendLine("# HELP generation_errors_total Total generation errors");
        sb.AppendLine("# TYPE generation_errors_total counter");
        sb.AppendLine($"generation_errors_total {_totalErrors}");
        sb.AppendLine("# HELP generation_ttf_last_ms Last recorded time-to-first-token (ms)");
        sb.AppendLine("# TYPE generation_ttf_last_ms gauge");
        sb.AppendLine($"generation_ttf_last_ms {_lastTtfMs}");
        return sb.ToString();
    }
    /// <summary>
    /// Creates a stopwatch for measuring time-to-first-token.
    /// Call RecordTimeToFirstToken with the elapsed time when first token arrives.
    /// </summary>
    public Stopwatch StartTtfMeasurement()
    {
        return Stopwatch.StartNew();
    }
}
