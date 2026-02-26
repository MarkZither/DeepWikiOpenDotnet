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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("PostgresVectorStoreAdapter constructed. InnerType={InnerType}", inner?.GetType().Name);
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

        var queryMemory = new ReadOnlyMemory<float>(embedding);
        var results = await _inner.QueryNearestAsync(queryMemory, k, repoFilter, filePathFilter, cancellationToken);

        var list = results.Select(d => new VectorQueryResult
        {
            Document = MapToAbstraction(d),
            SimilarityScore = CosineSimilarity(queryMemory, d.Embedding ?? default)
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

    public async Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
    {
        await _inner.DeleteChunksAsync(repoUrl, filePath, cancellationToken);
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
            IsImplementation = e.IsImplementation,
            ChunkIndex = e.ChunkIndex,
            TotalChunks = e.TotalChunks
        };
    }

    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.IsEmpty || b.IsEmpty) return 0f;
        if (a.Length != b.Length) return 0f;
        var aSpan = a.Span;
        var bSpan = b.Span;
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < aSpan.Length; i++)
        {
            dot += aSpan[i] * bSpan[i];
            normA += aSpan[i] * aSpan[i];
            normB += bSpan[i] * bSpan[i];
        }
        normA = MathF.Sqrt(normA);
        normB = MathF.Sqrt(normB);
        return (normA == 0 || normB == 0) ? 0f : dot / (normA * normB);
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
            IsImplementation = d.IsImplementation,
            ChunkIndex = d.ChunkIndex,
            TotalChunks = d.TotalChunks
        };
        return ent;
    }
}
