namespace DeepWiki.Data.Abstractions.Entities;

/// <summary>
/// Represents the current generation/lifecycle status of a <see cref="WikiEntity"/>.
/// </summary>
public enum WikiStatus
{
    Generating,
    Complete,
    Partial,
    Error
}
