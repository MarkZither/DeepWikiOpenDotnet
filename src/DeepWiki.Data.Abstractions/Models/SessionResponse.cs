using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Response containing newly created session identifier.
/// </summary>
public class SessionResponse
{
    /// <summary>
    /// Unique session identifier (GUID).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }
}
