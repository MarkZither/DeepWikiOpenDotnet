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
        // Use EF Core concurrency mechanism for detached entity updates:
        // Attach the detached entity and set original UpdatedAt as OriginalValue so SaveChanges generates a
        // WHERE UpdatedAt = @originalUpdatedAt clause and throws DbUpdateConcurrencyException when it doesn't match.
        var originalUpdatedAt = document.UpdatedAt;
        var newUpdatedAt = DateTime.UtcNow;

        // Attach the entity in Modified state but preserve original UpdatedAt as OriginalValue
        var entry = _context.Attach(document);
        entry.State = Microsoft.EntityFrameworkCore.EntityState.Modified;
        entry.Property(d => d.UpdatedAt).OriginalValue = originalUpdatedAt;
        entry.Property(d => d.UpdatedAt).CurrentValue = newUpdatedAt;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Rethrow to preserve expected behavior for tests
            throw;
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
