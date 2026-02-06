namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Represents an active generation session with context and lifecycle tracking.
/// </summary>
public class Session
{
    /// <summary>
    /// Unique session identifier (GUID).
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Optional organization/repository identifier (reserved for future use).
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Session creation timestamp (UTC).
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last activity timestamp (UTC).
    /// </summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// Expiration timestamp (UTC). Session becomes invalid after this time.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Current session status.
    /// </summary>
    public SessionStatus Status { get; set; }
}
