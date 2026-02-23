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
/// SQL Server EF Core implementation of IDocumentRepository.
/// Provides CRUD operations for documents using Entity Framework Core.
/// </summary>
public class SqlServerDocumentRepository : IDocumentRepository
{
    private readonly SqlServerVectorDbContext _context;

    public SqlServerDocumentRepository(SqlServerVectorDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        document.Id = Guid.NewGuid();
        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<DocumentEntity>> GetByRepoAsync(
        string repoUrl,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));
        if (skip < 0) throw new ArgumentException("Skip must be >= 0", nameof(skip));
        if (take < 1 || take > 1000) throw new ArgumentException("Take must be >= 1 and <= 1000", nameof(take));

        return await _context.Documents
            .Where(d => d.RepoUrl == repoUrl)
            .OrderByDescending(d => d.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(
        string? repoUrl = null,
        int skip = 0,
        int take = 100,
        bool firstChunkOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) throw new ArgumentException("Skip must be >= 0", nameof(skip));
        if (take < 1 || take > 1000) throw new ArgumentException("Take must be >= 1 and <= 1000", nameof(take));

        var query = _context.Documents.AsQueryable();
        if (!string.IsNullOrEmpty(repoUrl))
            query = query.Where(d => d.RepoUrl == repoUrl);

        // Apply chunk filter BEFORE Count and Skip/Take so pagination is over
        // distinct files, not over individual chunks.
        if (firstChunkOnly)
            query = query.Where(d => d.ChunkIndex == 0);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (document.Id == Guid.Empty) throw new ArgumentException("Document must have a valid ID", nameof(document));

        // Atomic conditional update: update only when UpdatedAt equals caller's original (optimistic concurrency)
        var originalUpdatedAt = document.UpdatedAt;
        // Ensure a different timestamp even on low-precision DB types by advancing original by 1ms
        var newUpdatedAt = originalUpdatedAt.AddMilliseconds(1);
        Console.WriteLine($"[DIAG] SqlServer UpdateAsync id={document.Id} original={originalUpdatedAt:o} new={newUpdatedAt:o}");

        var updatedCount = await _context.Documents
            .Where(d => d.Id == document.Id && d.UpdatedAt == originalUpdatedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Title, document.Title)
                .SetProperty(d => d.Text, document.Text)
                .SetProperty(d => d.Embedding, document.Embedding)
                .SetProperty(d => d.FileType, document.FileType)
                .SetProperty(d => d.IsCode, document.IsCode)
                .SetProperty(d => d.IsImplementation, document.IsImplementation)
                .SetProperty(d => d.TokenCount, document.TokenCount)
                .SetProperty(d => d.MetadataJson, document.MetadataJson)
                .SetProperty(d => d.UpdatedAt, newUpdatedAt), cancellationToken);

        Console.WriteLine($"[DIAG] SqlServer UpdateAsync updatedCount={updatedCount}");

        if (updatedCount == 0)
        {
            // No rows updated -> concurrency conflict
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Document update conflict");
        }
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

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AnyAsync(d => d.Id == id, cancellationToken);
    }
}
