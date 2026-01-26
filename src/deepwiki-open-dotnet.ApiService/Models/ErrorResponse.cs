namespace DeepWiki.ApiService.Models;

/// <summary>
/// Standard error response (Python API parity).
/// </summary>
public sealed record ErrorResponse
{
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Detail { get; init; }
}
