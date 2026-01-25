using System;
using Microsoft.Extensions.VectorData;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Document record for use with Microsoft.Extensions.VectorData abstractions.
/// This model uses VectorStore attributes for automatic mapping to vector store backends.
/// </summary>
public class DocumentRecord
{
    /// <summary>
    /// Unique identifier for the document.
    /// </summary>
    [VectorStoreKey]
    public Guid Id { get; set; }

    /// <summary>
    /// Repository URL where the document originated.
    /// Supports multiple providers: GitHub, GitLab, Forgejo, Gitea, Bitbucket, etc.
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string RepoUrl { get; set; } = string.Empty;

    /// <summary>
    /// Relative file path within the repository.
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Document title (typically the filename or a descriptive name).
    /// </summary>
    [VectorStoreData(IsFullTextIndexed = true)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Full text content of the document.
    /// </summary>
    [VectorStoreData(IsFullTextIndexed = true)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Vector embedding for semantic similarity search (1536 dimensions for OpenAI ada-002/text-embedding-3-small).
    /// </summary>
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float>? Embedding { get; set; }

    /// <summary>
    /// JSON-serialized metadata for additional document properties.
    /// </summary>
    [VectorStoreData]
    public string MetadataJson { get; set; } = "{}";

    /// <summary>
    /// File type/extension (e.g., "cs", "py", "md").
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this document contains code.
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public bool IsCode { get; set; }

    /// <summary>
    /// Whether this is an implementation file (vs test/config).
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public bool IsImplementation { get; set; }

    /// <summary>
    /// Token count for the document text.
    /// </summary>
    [VectorStoreData]
    public int TokenCount { get; set; }

    /// <summary>
    /// When the document was first created.
    /// </summary>
    [VectorStoreData]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the document was last updated.
    /// </summary>
    [VectorStoreData]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Converts this record to the legacy DocumentDto format.
    /// </summary>
    public DocumentDto ToDto()
    {
        return new DocumentDto
        {
            Id = Id,
            RepoUrl = RepoUrl,
            FilePath = FilePath,
            Title = Title,
            Text = Text,
            Embedding = Embedding?.ToArray() ?? Array.Empty<float>(),
            MetadataJson = MetadataJson,
            FileType = FileType,
            IsCode = IsCode,
            IsImplementation = IsImplementation,
            TokenCount = TokenCount,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    /// <summary>
    /// Creates a DocumentRecord from a legacy DocumentDto.
    /// </summary>
    public static DocumentRecord FromDto(DocumentDto dto)
    {
        return new DocumentRecord
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            RepoUrl = dto.RepoUrl,
            FilePath = dto.FilePath,
            Title = dto.Title,
            Text = dto.Text,
            Embedding = dto.Embedding is { Length: > 0 } ? new ReadOnlyMemory<float>(dto.Embedding) : null,
            MetadataJson = dto.MetadataJson ?? "{}",
            FileType = dto.FileType ?? string.Empty,
            IsCode = dto.IsCode,
            IsImplementation = dto.IsImplementation,
            TokenCount = dto.TokenCount,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt
        };
    }
}
