namespace DeepWiki.Data.Abstractions.Entities;

/// <summary>
/// Represents the current status of a <see cref="WikiPageEntity"/>.
/// </summary>
public enum PageStatus
{
    OK,
    Error,
    Generating
}
