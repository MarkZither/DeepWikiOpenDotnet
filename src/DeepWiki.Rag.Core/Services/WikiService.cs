using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Rag.Core.Observability;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// CRUD orchestration service for wiki entities.
/// Validates inputs, manages timestamps, and delegates persistence to <see cref="IWikiRepository"/>.
/// </summary>
public class WikiService : IWikiService
{
    private readonly IWikiRepository _repository;
    private readonly WikiMetrics? _metrics;

    public WikiService(IWikiRepository repository, WikiMetrics? metrics = null)
    {
        _repository = repository;
        _metrics = metrics;
    }

    /// <inheritdoc/>
    public async Task<WikiEntity> CreateWikiAsync(
        string name,
        string collectionId,
        string? description,
        IEnumerable<WikiPageEntity>? pages = null,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        if (string.IsNullOrWhiteSpace(collectionId))
            throw new ArgumentException("CollectionId is required.", nameof(collectionId));

        var now = DateTime.UtcNow;
        var wiki = new WikiEntity
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CollectionId = collectionId.Trim(),
            Description = description?.Trim(),
            Status = WikiStatus.Complete,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Persist the wiki shell first so pages can reference it
        wiki = await _repository.CreateWikiAsync(wiki, cancellationToken);

        _metrics?.RecordWikiCreated();

        // Persist any initial pages
        if (pages != null)
        {
            foreach (var page in pages)
            {
                page.WikiId = wiki.Id;
                page.CreatedAt = now;
                page.UpdatedAt = now;
                var persisted = await _repository.AddPageAsync(page, cancellationToken);
                wiki.Pages.Add(persisted);
            }
        }

        return wiki;
    }

    /// <inheritdoc/>
    public async Task<WikiEntity?> GetWikiByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _repository.GetWikiByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<WikiEntity> Items, int TotalCount)> GetProjectsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Clamp parameters to safe values
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get the page of results and total count concurrently (repo returns all for count)
        var allProjects = await _repository.GetProjectsAsync(1, int.MaxValue, cancellationToken);
        var totalCount = allProjects.Count;
        var pagedItems = allProjects
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedItems, totalCount);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteWikiAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetWikiByIdAsync(id, cancellationToken);
        if (existing == null)
            return false;

        await _repository.DeleteWikiAsync(id, cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async Task<WikiPageEntity?> GetPageByIdAsync(Guid wikiId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var page = await _repository.GetPageByIdAsync(pageId, cancellationToken);
        if (page == null || page.WikiId != wikiId)
            return null;
        return page;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetRelatedPagesAsync(pageId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WikiEntity?> UpdateWikiDescriptionAsync(Guid id, string? description, CancellationToken cancellationToken = default)
    {
        var wiki = await _repository.GetWikiByIdAsync(id, cancellationToken);
        if (wiki == null)
            return null;

        wiki.Description = description?.Trim();
        wiki.UpdatedAt = DateTime.UtcNow;

        // Persist via status update path (description is stored on the entity)
        // Use a dedicated update that modifies the entity directly
        await _repository.UpdateWikiDescriptionAsync(id, description, cancellationToken);

        return wiki;
    }

    /// <inheritdoc/>
    public async Task<WikiPageEntity> AddPageAsync(
        Guid wikiId,
        string title,
        string content,
        string sectionPath,
        int sortOrder,
        Guid? parentPageId,
        IEnumerable<Guid>? relatedPageIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Page title is required.", nameof(title));

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Page content is required.", nameof(content));

        var now = DateTime.UtcNow;
        var page = new WikiPageEntity
        {
            Id = Guid.NewGuid(),
            WikiId = wikiId,
            Title = title.Trim(),
            Content = content,
            SectionPath = sectionPath?.Trim() ?? string.Empty,
            SortOrder = sortOrder,
            ParentPageId = parentPageId,
            Status = PageStatus.OK,
            CreatedAt = now,
            UpdatedAt = now
        };

        var persisted = await _repository.AddPageAsync(page, cancellationToken);

        // Set related pages if provided
        if (relatedPageIds?.Any() == true)
            await _repository.SetRelatedPagesAsync(persisted.Id, relatedPageIds, cancellationToken);

        return persisted;
    }

    /// <inheritdoc/>
    public async Task<WikiPageEntity?> UpdatePageAsync(
        Guid wikiId,
        Guid pageId,
        string? title,
        string? content,
        string? sectionPath,
        int? sortOrder,
        IEnumerable<Guid>? relatedPageIds,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetPageByIdAsync(pageId, cancellationToken);
        if (existing == null || existing.WikiId != wikiId)
            return null;

        // Apply only non-null patches
        if (title != null) existing.Title = title.Trim();
        if (content != null) existing.Content = content;
        if (sectionPath != null) existing.SectionPath = sectionPath.Trim();
        if (sortOrder.HasValue) existing.SortOrder = sortOrder.Value;
        existing.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdatePageAsync(existing, cancellationToken);

        if (relatedPageIds != null)
            await _repository.SetRelatedPagesAsync(pageId, relatedPageIds, cancellationToken);

        return updated;
    }

    /// <inheritdoc/>
    public async Task<bool> DeletePageAsync(Guid wikiId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetPageByIdAsync(pageId, cancellationToken);
        if (existing == null || existing.WikiId != wikiId)
            return false;

        await _repository.DeletePageAsync(pageId, cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async Task UpdateRelatedPagesAsync(
        Guid wikiId,
        Guid pageId,
        IEnumerable<Guid> relatedPageIds,
        CancellationToken cancellationToken = default)
    {
        await _repository.SetRelatedPagesAsync(pageId, relatedPageIds, cancellationToken);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Wiki name is required.", nameof(name));

        if (name.Length > 200)
            throw new ArgumentException("Wiki name must not exceed 200 characters.", nameof(name));
    }
}
