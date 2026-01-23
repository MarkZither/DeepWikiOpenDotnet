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
/// SQL Server EF Core implementation of the provider persistence vector store (IPersistenceVectorStore).
/// Provides vector similarity search operations using SQL Server 2025 vector type.
/// </summary>
public class SqlServerVectorStore : IPersistenceVectorStore
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

        // Upsert by (RepoUrl, FilePath) to avoid duplicates — atomic per-document transaction
        var existing = await _context.Documents
            .FirstOrDefaultAsync(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath, cancellationToken);

        if (existing != null)
        {
            // Update fields on existing entity to preserve identity
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

    /// <summary>
    /// Query for nearest neighbors using SQL Server VECTOR_DISTANCE function.
    /// Falls back to in-memory cosine similarity if native vector queries fail.
    /// </summary>
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

        try
        {
            // Try native SQL Server VECTOR_DISTANCE query via FromSqlInterpolated
            return await QueryNearestNativeAsync(queryEmbedding, k, repoUrlFilter, filePathFilter, cancellationToken);
        }
        catch (Exception)
        {
            // Fallback to in-memory cosine similarity (for compatibility with test databases / older SQL Server)
            return await QueryNearestFallbackAsync(queryEmbedding, k, repoUrlFilter, filePathFilter, cancellationToken);
        }
    }

    /// <summary>
    /// Native SQL Server 2025 VECTOR_DISTANCE query using FromSqlInterpolated for k-NN search.
    /// </summary>
    private async Task<List<DocumentEntity>> QueryNearestNativeAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k,
        string? repoUrlFilter,
        string? filePathFilter,
        CancellationToken cancellationToken)
    {
        // Convert embedding to SQL Server vector literal format: '[0.1, 0.2, ...]'
        var vectorLiteral = FormatVectorLiteral(queryEmbedding);

        // Build WHERE clause fragments for LIKE pattern support
        var whereClause = "WHERE Embedding IS NOT NULL";
        if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            if (repoUrlFilter.Contains('%') || repoUrlFilter.Contains('_'))
            {
                whereClause += $" AND RepoUrl LIKE {{0}}";
            }
            else
            {
                whereClause += $" AND RepoUrl = {{0}}";
            }
        }
        if (!string.IsNullOrEmpty(filePathFilter))
        {
            var paramIndex = string.IsNullOrEmpty(repoUrlFilter) ? 0 : 1;
            if (filePathFilter.Contains('%') || filePathFilter.Contains('_'))
            {
                whereClause += $" AND FilePath LIKE {{{paramIndex}}}";
            }
            else
            {
                whereClause += $" AND FilePath = {{{paramIndex}}}";
            }
        }

        // Build parameterized SQL using VECTOR_DISTANCE (cosine distance, lower = more similar)
        // Note: VECTOR_DISTANCE requires vector literal in query, parameters for filters
        FormattableString sql;
        if (!string.IsNullOrEmpty(repoUrlFilter) && !string.IsNullOrEmpty(filePathFilter))
        {
            sql = $@"SELECT TOP({k}) Id, RepoUrl, FilePath, Title, Text, Embedding, MetadataJson, 
                            FileType, IsCode, IsImplementation, TokenCount, CreatedAt, UpdatedAt
                     FROM Documents
                     WHERE Embedding IS NOT NULL
                       AND (RepoUrl LIKE {repoUrlFilter} OR RepoUrl = {repoUrlFilter})
                       AND (FilePath LIKE {filePathFilter} OR FilePath = {filePathFilter})
                     ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST({vectorLiteral} AS vector(1536))) ASC";
        }
        else if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            sql = $@"SELECT TOP({k}) Id, RepoUrl, FilePath, Title, Text, Embedding, MetadataJson,
                            FileType, IsCode, IsImplementation, TokenCount, CreatedAt, UpdatedAt
                     FROM Documents
                     WHERE Embedding IS NOT NULL
                       AND (RepoUrl LIKE {repoUrlFilter} OR RepoUrl = {repoUrlFilter})
                     ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST({vectorLiteral} AS vector(1536))) ASC";
        }
        else if (!string.IsNullOrEmpty(filePathFilter))
        {
            sql = $@"SELECT TOP({k}) Id, RepoUrl, FilePath, Title, Text, Embedding, MetadataJson,
                            FileType, IsCode, IsImplementation, TokenCount, CreatedAt, UpdatedAt
                     FROM Documents
                     WHERE Embedding IS NOT NULL
                       AND (FilePath LIKE {filePathFilter} OR FilePath = {filePathFilter})
                     ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST({vectorLiteral} AS vector(1536))) ASC";
        }
        else
        {
            sql = $@"SELECT TOP({k}) Id, RepoUrl, FilePath, Title, Text, Embedding, MetadataJson,
                            FileType, IsCode, IsImplementation, TokenCount, CreatedAt, UpdatedAt
                     FROM Documents
                     WHERE Embedding IS NOT NULL
                     ORDER BY VECTOR_DISTANCE('cosine', Embedding, CAST({vectorLiteral} AS vector(1536))) ASC";
        }

        return await _context.Documents
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Fallback in-memory cosine similarity calculation for environments without VECTOR_DISTANCE support.
    /// </summary>
    private async Task<List<DocumentEntity>> QueryNearestFallbackAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k,
        string? repoUrlFilter,
        string? filePathFilter,
        CancellationToken cancellationToken)
    {
        // Server-side filtering (supports SQL LIKE patterns when pattern contains % or _)
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

        // Calculate cosine similarity in-memory and return top K
        var scored = filteredDocuments
            .Select(d => new { Document = d, Score = CosineSimilarity(queryEmbedding, d.Embedding ?? default) })
            .OrderByDescending(x => x.Score)
            .Take(k)
            .Select(x => x.Document)
            .ToList();

        return scored;
    }

    /// <summary>
    /// Formats a vector as a SQL Server vector literal: '[0.1, 0.2, ...]'
    /// </summary>
    private static string FormatVectorLiteral(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        var sb = new System.Text.StringBuilder(span.Length * 12); // approximate size
        sb.Append('[');
        for (int i = 0; i < span.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(span[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
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

    public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Rebuild the embedding index if it exists. Use safe IF EXISTS check to avoid errors on empty databases.
            var sql = @"IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_Document_Embedding')
                BEGIN
                    ALTER INDEX IX_Document_Embedding ON Documents REBUILD;
                END";

            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch
        {
            // Swallow provider-specific maintenance errors to avoid breaking higher-level flows.
            // Logging can be added if a logger is injected in future.
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
