namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Represents the current status of a generation session.
/// </summary>
public enum SessionStatus
{
    /// <summary>
    /// Session is active and accepting prompts.
    /// </summary>
    Active,

    /// <summary>
    /// Session completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Session was cancelled by client.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Session encountered an error.
    /// </summary>
    Error
}

/// <summary>
/// Represents the current status of a prompt within a session.
/// </summary>
public enum PromptStatus
{
    /// <summary>
    /// Prompt is currently being processed and streaming tokens.
    /// </summary>
    InFlight,

    /// <summary>
    /// Prompt generation completed successfully.
    /// </summary>
    Done,

    /// <summary>
    /// Prompt was cancelled by client.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Prompt generation encountered an error.
    /// </summary>
    Error
}
