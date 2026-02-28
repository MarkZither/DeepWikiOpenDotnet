using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Postgres.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL EF Core implementation of the provider persistence vector store (IPersistenceVectorStore).
/// Provides vector similarity search operations using pgvector extension.
/// Uses the <=> operator for cosine distance calculations.
/// </summary>
public class PostgresVectorStore : IPersistenceVectorStore
{
    private readonly PostgresVectorDbContext _context;
    private readonly Microsoft.Extensions.Logging.ILogger<PostgresVectorStore> _logger;

    // SECURITY: Maximum number of results to prevent resource exhaustion via unbounded queries
    private const int MaxK = 1000;

    // SECURITY: Maximum length for LIKE patterns to prevent regex-like DoS patterns
    private const int MaxLikePatternLength = 500;

    public PostgresVectorStore(PostgresVectorDbContext context, Microsoft.Extensions.Logging.ILogger<PostgresVectorStore> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("PostgresVectorStore constructed. DbContextType={DbContextType}", context?.GetType().Name);
    }

    // Back-compat convenience ctor for tests that don't provide a logger - uses NullLogger
    public PostgresVectorStore(PostgresVectorDbContext context)
        : this(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresVectorStore>.Instance)
    {
    }

    /// <summary>
    /// SECURITY: Validates LIKE patterns to prevent performance abuse.
    /// Rejects patterns that are too long or contain excessive wildcards.
    /// </summary>
    private static void ValidateLikePattern(string? pattern, string parameterName)
    {
        if (string.IsNullOrEmpty(pattern)) return;

        if (pattern.Length > MaxLikePatternLength)
        {
            throw new ArgumentException(
                $"Filter pattern exceeds maximum length of {MaxLikePatternLength} characters",
                parameterName);
        }

        // Count wildcards - excessive wildcards can cause slow queries
        var wildcardCount = pattern.Count(c => c == '%' || c == '_');
        if (wildcardCount > 10)
        {
            throw new ArgumentException(
                "Filter pattern contains too many wildcards (maximum 10 allowed)",
                parameterName);
        }

        // Reject leading wildcards which cause full table scans
        if (pattern.StartsWith('%') || pattern.StartsWith('_'))
        {
            throw new ArgumentException(
                "Filter pattern cannot start with a wildcard (causes full table scan)",
                parameterName);
        }
    }

    public async Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (document.Embedding != null && document.Embedding.Value.Length != 1536)
            throw new ArgumentException("Embedding must be exactly 1536 dimensions", nameof(document));

        // Upsert by (RepoUrl, FilePath, ChunkIndex) — each chunk is a distinct row
        var existing = await _context.Documents
            .FirstOrDefaultAsync(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath && d.ChunkIndex == document.ChunkIndex, cancellationToken);

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
            existing.TotalChunks = document.TotalChunks;
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
    /// Query for nearest neighbors using pgvector cosine distance operator.
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

        // SECURITY: Enforce upper bound on k to prevent resource exhaustion
        if (k > MaxK)
        {
            k = MaxK;
        }

        // SECURITY: Validate LIKE patterns to prevent slow query attacks
        ValidateLikePattern(repoUrlFilter, nameof(repoUrlFilter));
        ValidateLikePattern(filePathFilter, nameof(filePathFilter));

        try
        {
            // Try native pgvector query via FromSqlInterpolated
            return await QueryNearestNativeAsync(queryEmbedding, k, repoUrlFilter, filePathFilter, cancellationToken);
        }
        catch (Exception)
        {
            // Fallback to in-memory cosine similarity (for compatibility with test databases / missing pgvector)
            return await QueryNearestFallbackAsync(queryEmbedding, k, repoUrlFilter, filePathFilter, cancellationToken);
        }
    }

    /// <summary>
    /// Native pgvector query using the &lt;=&gt; cosine distance operator via FromSqlInterpolated.
    /// </summary>
    private async Task<List<DocumentEntity>> QueryNearestNativeAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k,
        string? repoUrlFilter,
        string? filePathFilter,
        CancellationToken cancellationToken)
    {
        // Convert embedding to pgvector literal format: '[0.1, 0.2, ...]'
        var vectorLiteral = FormatVectorLiteral(queryEmbedding);

        // Build parameterized SQL using <=> cosine distance operator (lower = more similar)
        FormattableString sql;
        if (!string.IsNullOrEmpty(repoUrlFilter) && !string.IsNullOrEmpty(filePathFilter))
        {
            // Check if filters contain LIKE wildcards
            var repoOp = (repoUrlFilter.Contains('%') || repoUrlFilter.Contains('_')) ? "LIKE" : "=";
            var fileOp = (filePathFilter.Contains('%') || filePathFilter.Contains('_')) ? "LIKE" : "=";
            sql = $@"SELECT ""Id"", ""RepoUrl"", ""FilePath"", ""Title"", ""Text"", ""Embedding"", ""MetadataJson"",
                            ""FileType"", ""IsCode"", ""IsImplementation"", ""TokenCount"", ""CreatedAt"", ""UpdatedAt""
                     FROM ""Documents""
                     WHERE ""Embedding"" IS NOT NULL
                       AND ""RepoUrl"" {repoOp:raw} {repoUrlFilter}
                       AND ""FilePath"" {fileOp:raw} {filePathFilter}
                     ORDER BY ""Embedding"" <=> {vectorLiteral}::vector
                     LIMIT {k}";
        }
        else if (!string.IsNullOrEmpty(repoUrlFilter))
        {
            var repoOp = (repoUrlFilter.Contains('%') || repoUrlFilter.Contains('_')) ? "LIKE" : "=";
            sql = $@"SELECT ""Id"", ""RepoUrl"", ""FilePath"", ""Title"", ""Text"", ""Embedding"", ""MetadataJson"",
                            ""FileType"", ""IsCode"", ""IsImplementation"", ""TokenCount"", ""CreatedAt"", ""UpdatedAt""
                     FROM ""Documents""
                     WHERE ""Embedding"" IS NOT NULL
                       AND ""RepoUrl"" {repoOp:raw} {repoUrlFilter}
                     ORDER BY ""Embedding"" <=> {vectorLiteral}::vector
                     LIMIT {k}";
        }
        else if (!string.IsNullOrEmpty(filePathFilter))
        {
            var fileOp = (filePathFilter.Contains('%') || filePathFilter.Contains('_')) ? "LIKE" : "=";
            sql = $@"SELECT ""Id"", ""RepoUrl"", ""FilePath"", ""Title"", ""Text"", ""Embedding"", ""MetadataJson"",
                            ""FileType"", ""IsCode"", ""IsImplementation"", ""TokenCount"", ""CreatedAt"", ""UpdatedAt""
                     FROM ""Documents""
                     WHERE ""Embedding"" IS NOT NULL
                       AND ""FilePath"" {fileOp:raw} {filePathFilter}
                     ORDER BY ""Embedding"" <=> {vectorLiteral}::vector
                     LIMIT {k}";
        }
        else
        {
            sql = $@"SELECT ""Id"", ""RepoUrl"", ""FilePath"", ""Title"", ""Text"", ""Embedding"", ""MetadataJson"",
                            ""FileType"", ""IsCode"", ""IsImplementation"", ""TokenCount"", ""CreatedAt"", ""UpdatedAt""
                     FROM ""Documents""
                     WHERE ""Embedding"" IS NOT NULL
                     ORDER BY ""Embedding"" <=> {vectorLiteral}::vector
                     LIMIT {k}";
        }

        return await _context.Documents
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Fallback in-memory cosine similarity calculation for environments without pgvector.
    /// </summary>
    private async Task<List<DocumentEntity>> QueryNearestFallbackAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k,
        string? repoUrlFilter,
        string? filePathFilter,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Formats a vector as a pgvector literal: '[0.1, 0.2, ...]'
    /// </summary>
    private static string FormatVectorLiteral(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        var sb = new System.Text.StringBuilder(span.Length * 12);
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

    public async Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

        var documents = await _context.Documents
            .Where(d => d.RepoUrl == repoUrl && d.FilePath == filePath)
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
