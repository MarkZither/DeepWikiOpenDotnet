using System.Text.Json.Serialization;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Standard error response (Python API parity).
/// </summary>
public sealed record ErrorResponse
{
    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}
