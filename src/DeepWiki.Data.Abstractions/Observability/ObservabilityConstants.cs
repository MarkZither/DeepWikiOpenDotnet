namespace DeepWiki.Data.Abstractions.Observability;

/// <summary>
/// Shared observability constants (meter names, versions) used across services.
/// </summary>
public static class ObservabilityConstants
{
    public const string GenerationMeterName = "DeepWiki.Rag.Generation";
    public const string GenerationMeterVersion = "1.0.0";

    public const string WikiMeterName = "DeepWiki.Rag.Wiki";
    public const string WikiMeterVersion = "1.0.0";
}
