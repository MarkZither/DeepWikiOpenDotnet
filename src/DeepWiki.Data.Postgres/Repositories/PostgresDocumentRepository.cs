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
/// PostgreSQL EF Core implementation of IDocumentRepository.
/// Provides CRUD operations for documents using Entity Framework Core with pgvector support.
/// </summary>
public class PostgresDocumentRepository : IDocumentRepository
{
    private readonly PostgresVectorDbContext _context;

    public PostgresDocumentRepository(PostgresVectorDbContext context)
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

    public async Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (document.Id == Guid.Empty) throw new ArgumentException("Document must have a valid ID", nameof(document));

        // Use server-side conditional update to enforce optimistic concurrency on UpdatedAt
        var originalUpdatedAt = document.UpdatedAt;
        var newUpdatedAt = DateTime.UtcNow;

        // Atomic conditional update: only update when UpdatedAt matches the incoming document (optimistic concurrency)
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
