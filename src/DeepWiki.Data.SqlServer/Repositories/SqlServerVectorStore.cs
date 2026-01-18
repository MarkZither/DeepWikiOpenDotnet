using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.SqlServer.Repositories;

/// <summary>
/// SQL Server EF Core implementation of IVectorStore.
/// Provides vector similarity search operations using SQL Server 2025 vector type.
/// </summary>
public class SqlServerVectorStore : IVectorStore
{
    private readonly SqlServerVectorDbContext _context;

    public SqlServerVectorStore(SqlServerVectorDbContext context)
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

        // Fetch all documents (in future, use SQL Server vector distance functions)
        var query = _context.Documents.AsQueryable();

        if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            query = query.Where(d => d.RepoUrl == repoUrlFilter);
        }

        var allDocuments = await query.ToListAsync(cancellationToken);

        // Calculate cosine similarity and return top K
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

    public async Task BulkUpsertAsync(IEnumerable<DocumentEntity> documents, CancellationToken cancellationToken = default)
    {
        if (documents == null) throw new ArgumentNullException(nameof(documents));
        
        var docList = documents.ToList();
        if (docList.Count == 0) throw new ArgumentException("Documents collection must not be empty", nameof(documents));

        // Validate all embeddings before any database operations
        foreach (var doc in docList)
        {
            if (doc.Embedding != null && doc.Embedding.Value.Length != 1536)
                throw new ArgumentException("All embeddings must be exactly 1536 dimensions", nameof(documents));
        }

        try
        {
            var now = DateTime.UtcNow;
            
            // Get existing document IDs
            var existingIds = await _context.Documents
                .Where(d => docList.Select(nd => nd.Id).Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);

            // Separate documents into new and existing
            var newDocuments = docList.Where(d => !existingIds.Contains(d.Id)).ToList();
            var existingDocuments = docList.Where(d => existingIds.Contains(d.Id)).ToList();

            // Process new documents
            foreach (var doc in newDocuments)
            {
                doc.Id = Guid.NewGuid();
                doc.CreatedAt = now;
                doc.UpdatedAt = now;
                _context.Documents.Add(doc);
            }

            // Process existing documents
            foreach (var doc in existingDocuments)
            {
                doc.UpdatedAt = now;
                _context.Documents.Update(doc);
            }

            // Save all changes in a single transaction
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Ensure any failed operation rolls back the entire transaction
            throw new Microsoft.EntityFrameworkCore.DbUpdateException(
                $"Bulk upsert failed after processing {docList.Count} documents. All changes have been rolled back.",
                ex);
        }
    }

    /// <summary>
    /// Helper: Calculate cosine similarity between two embeddings.
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
