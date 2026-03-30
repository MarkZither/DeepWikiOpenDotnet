using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// CRUD orchestration service for wiki entities.
/// Responsibilities: validation, timestamp management, and delegating persistence to IWikiRepository.
/// </summary>
public interface IWikiService
{
    /// <summary>
    /// Creates a new wiki with optional initial pages.
    /// Validates name (required, max 200 chars) and collectionId (required).
    /// </summary>
    Task<WikiEntity> CreateWikiAsync(
        string name,
        string collectionId,
        string? description,
        IEnumerable<WikiPageEntity>? pages = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the wiki with all its pages, or null if not found.</summary>
    Task<WikiEntity?> GetWikiByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns a paginated list of all wikis with total count.</summary>
    Task<(IReadOnlyList<WikiEntity> Items, int TotalCount)> GetProjectsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a wiki and all its pages/relations. Returns false if not found.</summary>
    Task<bool> DeleteWikiAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns a single page by its ID, or null if not found or not belonging to the wiki.</summary>
    Task<WikiPageEntity?> GetPageByIdAsync(Guid wikiId, Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Returns all pages directly related to the given page.</summary>
    Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the description of an existing wiki (name is immutable).
    /// Returns the updated wiki, or null if not found.
    /// </summary>
    Task<WikiEntity?> UpdateWikiDescriptionAsync(Guid id, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new page to an existing wiki.
    /// Validates title and content (both required).
    /// </summary>
    Task<WikiPageEntity> AddPageAsync(
        Guid wikiId,
        string title,
        string content,
        string sectionPath,
        int sortOrder,
        Guid? parentPageId,
        IEnumerable<Guid>? relatedPageIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates mutable fields of an existing page.
    /// Returns the updated page, or null if not found.
    /// </summary>
    Task<WikiPageEntity?> UpdatePageAsync(
        Guid wikiId,
        Guid pageId,
        string? title,
        string? content,
        string? sectionPath,
        int? sortOrder,
        IEnumerable<Guid>? relatedPageIds,
        CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a page. Returns false if not found.</summary>
    Task<bool> DeletePageAsync(Guid wikiId, Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Replaces all related-page links for the given page.</summary>
    Task UpdateRelatedPagesAsync(
        Guid wikiId,
        Guid pageId,
        IEnumerable<Guid> relatedPageIds,
        CancellationToken cancellationToken = default);
}
