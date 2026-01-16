using System;
using System.ComponentModel.DataAnnotations;

namespace DeepWiki.Data.Entities;

/// <summary>
/// Represents a document in the knowledge base with its text content, vector embedding, and metadata.
/// </summary>
public class DocumentEntity
{
    /// <summary>
    /// Unique identifier for the document.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Source repository URL (e.g., https://github.com/org/repo). Required.
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public required string RepoUrl { get; set; }

    /// <summary>
    /// Relative file path within the repository (e.g., src/Program.cs). Required.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public required string FilePath { get; set; }

    /// <summary>
    /// Document title or filename. Optional, max 500 characters.
    /// </summary>
    [MaxLength(500)]
    public string? Title { get; set; }

    /// <summary>
    /// Full document text content. Required, max 50MB enforced at database level.
    /// </summary>
    [Required]
    public required string Text { get; set; }

    /// <summary>
    /// Vector embedding representing semantic meaning. 
    /// Optional, exactly 1536 dimensions when non-null.
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// File extension without dot (e.g., "cs", "md", "py"). Optional, max 10 characters.
    /// </summary>
    [MaxLength(10)]
    public string? FileType { get; set; }

    /// <summary>
    /// True if the document is source code.
    /// </summary>
    public bool IsCode { get; set; }

    /// <summary>
    /// True if the document contains implementation code (vs comments/docs).
    /// </summary>
    public bool IsImplementation { get; set; }

    /// <summary>
    /// Number of tokens in the text for chunking and cost estimation.
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// UTC timestamp when the document was first created. Auto-set on creation.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last update. Auto-updated on modification. Used for optimistic concurrency.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata stored as JSON for extensibility. Optional, max 1MB.
    /// </summary>
    [MaxLength(1048576)]
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Validates that the embedding has exactly 1536 dimensions if non-null.
    /// </summary>
    /// <exception cref="ArgumentException">If embedding is not null and dimensions != 1536.</exception>
    public void ValidateEmbedding()
    {
        if (Embedding != null && Embedding.Length != 1536)
        {
            throw new ArgumentException(
                $"Embedding must be exactly 1536 dimensions (got {Embedding.Length})",
                nameof(Embedding));
        }
    }
}
