using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Postgres.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL EF Core implementation of IVectorStore.
/// Provides vector similarity search operations using pgvector extension.
/// Uses the <=> operator for cosine distance calculations.
/// </summary>
public class PostgresVectorStore : IVectorStore
{
    private readonly PostgresVectorDbContext _context;

    public PostgresVectorStore(PostgresVectorDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (document.Embedding != null && document.Embedding.Value.Length != 1536)
            throw new ArgumentException("Embedding must be exactly 1536 dimensions", nameof(document));

        var exists = await _context.Documents.AnyAsync(d => d.Id == document.Id, cancellationToken);
        
        if (exists)
        {
            document.UpdatedAt = DateTime.UtcNow;
            _context.Documents.Update(document);
        }
        else
        {
            document.Id = Guid.NewGuid();
            document.CreatedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            _context.Documents.Add(document);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DocumentEntity>> QueryNearestAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k = 10,
        string? repoUrlFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.IsEmpty) throw new ArgumentNullException(nameof(queryEmbedding));
        if (queryEmbedding.Length != 1536) throw new ArgumentException("Query embedding must be exactly 1536 dimensions", nameof(queryEmbedding));
        if (k < 1) throw new ArgumentException("k must be >= 1", nameof(k));

        // Fetch all documents (in future, use pgvector <=> operator directly in SQL)
        var query = _context.Documents.AsQueryable();

        if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            query = query.Where(d => d.RepoUrl == repoUrlFilter);
        }

        var allDocuments = await query.ToListAsync(cancellationToken);

        // Calculate cosine similarity and return top K
        // Using the same algorithm as SQL Server for test parity
        var scored = allDocuments
            .Select(d => new { Document = d, Score = CosineSimilarity(queryEmbedding, d.Embedding ?? default) })
            .OrderByDescending(x => x.Score)
            .Take(k)
            .Select(x => x.Document)
            .ToList();

        return scored;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));

        var documents = await _context.Documents
            .Where(d => d.RepoUrl == repoUrl)
            .ToListAsync(cancellationToken);

        if (documents.Count > 0)
        {
            _context.Documents.RemoveRange(documents);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CountAsync(string? repoUrlFilter = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();

        if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            query = query.Where(d => d.RepoUrl == repoUrlFilter);
        }

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.IsEmpty || b.IsEmpty) return 0f;
        if (a.Length != b.Length) return 0f;

        var aSpan = a.Span;
        var bSpan = b.Span;

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < aSpan.Length; i++)
        {
            dotProduct += aSpan[i] * bSpan[i];
            normA += aSpan[i] * aSpan[i];
            normB += bSpan[i] * bSpan[i];
        }

        normA = MathF.Sqrt(normA);
        normB = MathF.Sqrt(normB);

        if (normA == 0 || normB == 0) return 0f;
        return dotProduct / (normA * normB);
    }
}
