using System.Text.Json.Serialization;

namespace DeepWiki.Rag.Core.Snapshots;

/// <summary>
/// An LLM interaction record captured during wiki generation for auditing and reproducibility.
/// Matches the canonical snapshot JSON schema defined in llm-snapshots/wiki/README.md.
/// </summary>
public sealed record WikiSnapshotEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("model")]
    public string Model { get; init; } = "unknown";

    [JsonPropertyName("feature")]
    public string Feature { get; init; } = string.Empty;

    [JsonPropertyName("request")]
    public WikiSnapshotRequest Request { get; init; } = new();

    [JsonPropertyName("stream")]
    public IReadOnlyList<string> Stream { get; init; } = [];

    [JsonPropertyName("response_hash")]
    public string ResponseHash { get; init; } = string.Empty;

    [JsonPropertyName("redacted")]
    public bool Redacted { get; init; }

    [JsonPropertyName("retention_policy")]
    public string RetentionPolicy { get; init; } = "indefinite";
}

/// <summary>The prompt that was sent to the LLM.</summary>
public sealed record WikiSnapshotRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;
}
