using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Data.Postgres.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL EF Core implementation of <see cref="IWikiRepository"/>.
/// Uses <see cref="PostgresVectorDbContext"/> with eager loading for related entities.
/// </summary>
public class PostgresWikiRepository : IWikiRepository
{
    private readonly PostgresVectorDbContext _context;

    public PostgresWikiRepository(PostgresVectorDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ── Wiki-level operations ─────────────────────────────────────────────

    public async Task<WikiEntity> CreateWikiAsync(WikiEntity wiki, CancellationToken cancellationToken = default)
    {
        if (wiki == null) throw new ArgumentNullException(nameof(wiki));

        wiki.Id = Guid.NewGuid();
        wiki.CreatedAt = DateTime.UtcNow;
        wiki.UpdatedAt = DateTime.UtcNow;

        _context.Wikis.Add(wiki);
        await _context.SaveChangesAsync(cancellationToken);
        return wiki;
    }

    public async Task<WikiEntity?> GetWikiByIdAsync(Guid wikiId, CancellationToken cancellationToken = default)
    {
        return await _context.Wikis
            .Include(w => w.Pages)
                .ThenInclude(p => p.SourceRelations)
                    .ThenInclude(r => r.TargetPage)
            .Include(w => w.Pages)
                .ThenInclude(p => p.ChildPages)
            .FirstOrDefaultAsync(w => w.Id == wikiId, cancellationToken);
    }

    public async Task<IReadOnlyList<WikiEntity>> GetProjectsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await _context.Wikis
            .OrderByDescending(w => w.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteWikiAsync(Guid wikiId, CancellationToken cancellationToken = default)
    {
        // Cascade delete is configured on Postgres — deleting the wiki removes all pages and relations
        await _context.Wikis
            .Where(w => w.Id == wikiId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task UpdateWikiStatusAsync(Guid wikiId, WikiStatus status, CancellationToken cancellationToken = default)
    {
        await _context.Wikis
            .Where(w => w.Id == wikiId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, status)
                .SetProperty(w => w.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }

    // ── Page-level operations ─────────────────────────────────────────────

    public async Task<WikiPageEntity?> GetPageByIdAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        return await _context.WikiPages
            .Include(p => p.SourceRelations)
                .ThenInclude(r => r.TargetPage)
            .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);
    }

    public async Task<WikiPageEntity> AddPageAsync(WikiPageEntity page, CancellationToken cancellationToken = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        page.Id = Guid.NewGuid();
        page.CreatedAt = DateTime.UtcNow;
        page.UpdatedAt = DateTime.UtcNow;

        _context.WikiPages.Add(page);
        await _context.SaveChangesAsync(cancellationToken);
        return page;
    }

    public async Task<WikiPageEntity> UpdatePageAsync(WikiPageEntity page, CancellationToken cancellationToken = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        var existing = await _context.WikiPages.FindAsync([page.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"WikiPage {page.Id} not found.");

        existing.Title = page.Title;
        existing.Content = page.Content;
        existing.SectionPath = page.SectionPath;
        existing.SortOrder = page.SortOrder;
        existing.ParentPageId = page.ParentPageId;
        existing.Status = page.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task DeletePageAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        // Cascade delete on WikiPageRelations.SourcePageId handles relation cleanup
        await _context.WikiPages
            .Where(p => p.Id == pageId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> GetPageCountAsync(Guid wikiId, CancellationToken cancellationToken = default)
    {
        return await _context.WikiPages
            .CountAsync(p => p.WikiId == wikiId, cancellationToken);
    }

    // ── Relation operations ───────────────────────────────────────────────

    public async Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WikiPageRelations
            .Where(r => r.SourcePageId == pageId)
            .Select(r => r.TargetPage)
            .ToListAsync(cancellationToken);
    }

    public async Task SetRelatedPagesAsync(
        Guid sourcePageId,
        IEnumerable<Guid> targetPageIds,
        CancellationToken cancellationToken = default)
    {
        // Remove all existing outgoing relations for this source page
        await _context.WikiPageRelations
            .Where(r => r.SourcePageId == sourcePageId)
            .ExecuteDeleteAsync(cancellationToken);

        // Insert new relations
        var relations = targetPageIds
            .Select(targetId => new WikiPageRelation
            {
                SourcePageId = sourcePageId,
                TargetPageId = targetId
            })
            .ToList();

        if (relations.Count > 0)
        {
            _context.WikiPageRelations.AddRange(relations);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsGeneratingAsync(
        string collectionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _context.Wikis
            .AnyAsync(
                w => w.CollectionId == collectionId
                     && w.Name == name
                     && w.Status == WikiStatus.Generating,
                cancellationToken);
    }

    /// <summary>
    /// Upserts a page: inserts if the page ID does not exist, otherwise updates all mutable fields.
    /// This operation is idempotent — calling it twice with the same data produces a single row.
    /// EF Core tracks the entity state to determine insert vs. update.
    /// </summary>
    public async Task<WikiPageEntity> UpsertPageAsync(
        WikiPageEntity page,
        CancellationToken cancellationToken = default)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));

        var existing = await _context.WikiPages.FindAsync([page.Id], cancellationToken);
        if (existing == null)
        {
            // Insert path
            if (page.Id == Guid.Empty) page.Id = Guid.NewGuid();
            page.CreatedAt = DateTime.UtcNow;
            page.UpdatedAt = DateTime.UtcNow;
            _context.WikiPages.Add(page);
        }
        else
        {
            // Update path — overwrite mutable fields only
            existing.Title = page.Title;
            existing.Content = page.Content;
            existing.SectionPath = page.SectionPath;
            existing.SortOrder = page.SortOrder;
            existing.ParentPageId = page.ParentPageId;
            existing.Status = page.Status;
            existing.UpdatedAt = DateTime.UtcNow;
            page = existing;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return page;
    }
}
