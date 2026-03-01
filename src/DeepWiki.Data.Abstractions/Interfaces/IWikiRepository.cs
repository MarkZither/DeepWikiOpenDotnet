using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.Data.Abstractions.Interfaces;

/// <summary>
/// Data-access contract for wiki persistence operations.
/// Implementations exist per database provider (Postgres, SQL Server).
/// </summary>
public interface IWikiRepository
{
    // ── Wiki-level operations ──────────────────────────────────────────────

    /// <summary>Creates a new wiki and persists it, returning the created entity.</summary>
    Task<WikiEntity> CreateWikiAsync(WikiEntity wiki, CancellationToken cancellationToken = default);

    /// <summary>Returns the wiki with the given ID including its pages, or null if not found.</summary>
    Task<WikiEntity?> GetWikiByIdAsync(Guid wikiId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated list of all wikis (summary projection).
    /// </summary>
    Task<IReadOnlyList<WikiEntity>> GetProjectsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes the wiki and all associated pages and relations.</summary>
    Task DeleteWikiAsync(Guid wikiId, CancellationToken cancellationToken = default);

    /// <summary>Updates only the <see cref="WikiStatus"/> and <see cref="WikiEntity.UpdatedAt"/> of an existing wiki.</summary>
    Task UpdateWikiStatusAsync(Guid wikiId, WikiStatus status, CancellationToken cancellationToken = default);

    // ── Page-level operations ──────────────────────────────────────────────

    /// <summary>Returns the page with the given ID, or null if not found.</summary>
    Task<WikiPageEntity?> GetPageByIdAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new page to an existing wiki.</summary>
    Task<WikiPageEntity> AddPageAsync(WikiPageEntity page, CancellationToken cancellationToken = default);

    /// <summary>Updates a page's mutable fields.</summary>
    Task<WikiPageEntity> UpdatePageAsync(WikiPageEntity page, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a single page and its relations.</summary>
    Task DeletePageAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Returns the total number of pages belonging to the specified wiki.</summary>
    Task<int> GetPageCountAsync(Guid wikiId, CancellationToken cancellationToken = default);

    // ── Relation operations ────────────────────────────────────────────────

    /// <summary>
    /// Returns all pages that are directly related to the specified page
    /// (i.e. pages reachable via <see cref="WikiPageRelation"/> where the given page is the source).
    /// </summary>
    Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all outgoing relations for <paramref name="sourcePageId"/> with
    /// relations pointing to <paramref name="targetPageIds"/>.
    /// </summary>
    Task SetRelatedPagesAsync(Guid sourcePageId, IEnumerable<Guid> targetPageIds, CancellationToken cancellationToken = default);

    // ── Generation guard ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true if a wiki with the given collection/name combination is currently
    /// in <see cref="WikiStatus.Generating"/> status, enabling 409 conflict detection.
    /// </summary>
    Task<bool> ExistsGeneratingAsync(string collectionId, string name, CancellationToken cancellationToken = default);

    // ── Upsert ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts or updates a page identified by its ID.
    /// If the page already exists the mutable fields are updated; otherwise a new row is inserted.
    /// The operation MUST be idempotent — calling it twice with the same data produces a single row.
    /// </summary>
    Task<WikiPageEntity> UpsertPageAsync(WikiPageEntity page, CancellationToken cancellationToken = default);
}
