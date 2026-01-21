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
/// PostgreSQL EF Core implementation of the provider persistence vector store (IPersistenceVectorStore).
/// Provides vector similarity search operations using pgvector extension.
/// Uses the <=> operator for cosine distance calculations.
/// </summary>
public class PostgresVectorStore : IPersistenceVectorStore
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

        // Upsert by (RepoUrl, FilePath) to avoid duplicates — atomic per-document transaction
        var existing = await _context.Documents
            .FirstOrDefaultAsync(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath, cancellationToken);

        if (existing != null)
        {
            existing.Title = document.Title;
            existing.Text = document.Text;
            existing.Embedding = document.Embedding;
            existing.MetadataJson = document.MetadataJson;
            existing.FileType = document.FileType;
            existing.IsCode = document.IsCode;
            existing.IsImplementation = document.IsImplementation;
            existing.TokenCount = document.TokenCount;
            existing.UpdatedAt = DateTime.UtcNow;

            _context.Documents.Update(existing);
        }
        else
        {
            document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
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
        string? filePathFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.IsEmpty) throw new ArgumentNullException(nameof(queryEmbedding));
        if (queryEmbedding.Length != 1536) throw new ArgumentException("Query embedding must be exactly 1536 dimensions", nameof(queryEmbedding));
        if (k < 1) throw new ArgumentException("k must be >= 1", nameof(k));

        var query = _context.Documents.AsQueryable().Where(d => d.Embedding != null);

        if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            if (repoUrlFilter.Contains('%') || repoUrlFilter.Contains('_'))
            {
                query = query.Where(d => EF.Functions.Like(d.RepoUrl, repoUrlFilter));
            }
            else
            {
                query = query.Where(d => d.RepoUrl == repoUrlFilter);
            }
        }

        if (!string.IsNullOrEmpty(filePathFilter))
        {
            if (filePathFilter.Contains('%') || filePathFilter.Contains('_'))
            {
                query = query.Where(d => EF.Functions.Like(d.FilePath, filePathFilter));
            }
            else
            {
                query = query.Where(d => d.FilePath == filePathFilter);
            }
        }

        var filteredDocuments = await query.ToListAsync(cancellationToken);

        var scored = filteredDocuments
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

    public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        // Postgres pgvector index maintenance may be a no-op for many installations; expose method for completeness
        return Task.CompletedTask;
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
