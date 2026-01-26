using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Data.Postgres.VectorStore;

/// <summary>
/// Adapter that implements the Abstractions IVectorStore and delegates to the provider-level IPersistenceVectorStore.
/// Maps between Abstractions DocumentDto and provider Entities.DocumentEntity.
/// </summary>
public class PostgresVectorStoreAdapter : DeepWiki.Data.Abstractions.IVectorStore
{
    private readonly IPersistenceVectorStore _inner;
    private readonly ILogger<PostgresVectorStoreAdapter> _logger;

    public PostgresVectorStoreAdapter(IPersistenceVectorStore inner, ILogger<PostgresVectorStoreAdapter> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger;
    }

    public async Task<IReadOnlyList<VectorQueryResult>> QueryAsync(float[] embedding, int k, Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
    {
        if (embedding == null) throw new ArgumentNullException(nameof(embedding));
        if (embedding.Length != 1536) throw new ArgumentException("Embedding must be 1536 dimensions", nameof(embedding));
        if (k < 1) throw new ArgumentException("k must be >= 1", nameof(k));

        string? repoFilter = null;
        if (filters != null && filters.TryGetValue("repoUrl", out var r)) repoFilter = r;

        string? filePathFilter = null;
        if (filters != null && filters.TryGetValue("filePath", out var f)) filePathFilter = f;

        var results = await _inner.QueryNearestAsync(new ReadOnlyMemory<float>(embedding), k, repoFilter, filePathFilter, cancellationToken);

        var list = results.Select(d => new VectorQueryResult
        {
            Document = MapToAbstraction(d),
            SimilarityScore = 0f
        }).ToList().AsReadOnly();

        return list;
    }

    public async Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (document.Embedding != null && document.Embedding.Length != 1536)
            throw new ArgumentException("Embedding must be exactly 1536 dimensions", nameof(document));

        var entity = MapToEntity(document);
        await _inner.UpsertAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _inner.DeleteAsync(id, cancellationToken);
    }

    public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        await _inner.RebuildIndexAsync(cancellationToken);
    }

    private static DocumentDto MapToAbstraction(DeepWiki.Data.Entities.DocumentEntity e)
    {
        var emb = e.Embedding?.ToArray() ?? Array.Empty<float>();
        return new DocumentDto
        {
            Id = e.Id,
            RepoUrl = e.RepoUrl,
            FilePath = e.FilePath,
            Title = e.Title ?? string.Empty,
            Text = e.Text ?? string.Empty,
            Embedding = emb,
            MetadataJson = e.MetadataJson ?? "{}",
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            TokenCount = e.TokenCount,
            FileType = e.FileType ?? string.Empty,
            IsCode = e.IsCode,
            IsImplementation = e.IsImplementation
        };
    }

    private static DeepWiki.Data.Entities.DocumentEntity MapToEntity(DocumentDto d)
    {
        var ent = new DeepWiki.Data.Entities.DocumentEntity
        {
            Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id,
            RepoUrl = d.RepoUrl,
            FilePath = d.FilePath,
            Title = d.Title,
            Text = d.Text,
            Embedding = d.Embedding != null && d.Embedding.Length > 0 ? new ReadOnlyMemory<float>(d.Embedding) : null,
            MetadataJson = d.MetadataJson ?? "{}",
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
            TokenCount = d.TokenCount,
            FileType = d.FileType,
            IsCode = d.IsCode,
            IsImplementation = d.IsImplementation
        };
        return ent;
    }
}
