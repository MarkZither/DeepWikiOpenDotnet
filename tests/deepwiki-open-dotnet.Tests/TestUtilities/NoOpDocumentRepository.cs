using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;

namespace DeepWiki.ApiService.Tests.TestUtilities;

/// <summary>
/// No-op implementation of IDocumentRepository for testing.
/// Returns empty results for all operations without performing actual database operations.
/// Individual tests can override this with specific mock implementations as needed.
/// </summary>
public class NoOpDocumentRepository : IDocumentRepository
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
        
    public Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default) 
        => Task.FromResult((new List<DocumentEntity>(), 0));
}
