using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;

namespace DeepWiki.ApiService.Tests.TestUtilities;

/// <summary>
/// In-memory stub of IDocumentRepository for testing.
/// Returns empty/default results without touching the database.
/// Individual tests can register their own instance or subclass as needed.
/// </summary>
public class MockDocumentRepository : IDocumentRepository
{
    public Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
        
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
        
    public Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) 
        => Task.FromResult(new List<DocumentEntity>());
        
    public Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) 
        => Task.FromResult<DocumentEntity?>(null);
        
    public Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
        
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) 
        => Task.FromResult(false);
        
    public Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, bool firstChunkOnly = false, CancellationToken cancellationToken = default) 
        => Task.FromResult((new List<DocumentEntity>(), 0));

    public Task<List<(string RepoUrl, int DocumentCount)>> GetCollectionSummariesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<(string RepoUrl, int DocumentCount)>());
}
