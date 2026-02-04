using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;
using DeepWiki.Rag.Core.VectorStore;

namespace DeepWiki.ApiService.Tests.Api;

/// <summary>
/// Test fixture for API integration tests using WebApplicationFactory.
/// Provides a test server with configured dependencies and in-memory services.
/// References the Program class from DeepWiki.ApiService namespace.
/// </summary>
public class ApiTestFixture : WebApplicationFactory<DeepWiki.ApiService.Program>
{
    /// <summary>
    /// Configures the test host with test-specific services and settings.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove production IVectorStore registration and replace with NoOp for testing
            var vectorStoreDescriptor = services.FirstOrDefault(d => 
                d.ServiceType == typeof(IVectorStore));
            if (vectorStoreDescriptor != null)
            {
                services.Remove(vectorStoreDescriptor);
            }
            
            // Register NoOpVectorStore for tests (individual tests can override with mocks)
            services.AddScoped<IVectorStore, NoOpVectorStore>();
            
            // Remove production IDocumentRepository registration if present and replace with NoOp for testing
            var repositoryDescriptor = services.FirstOrDefault(d => 
                d.ServiceType == typeof(IDocumentRepository));
            if (repositoryDescriptor != null)
            {
                services.Remove(repositoryDescriptor);
            }
            
            // Register NoOpDocumentRepository for tests (individual tests can override with mocks)
            services.AddScoped<IDocumentRepository, NoOpDocumentRepository>();
            
            // Override configuration for test environment
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Tests can add in-memory configuration here if needed
            });
        });

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Creates an HttpClient configured to communicate with the test server.
    /// </summary>
    public new HttpClient CreateClient()
    {
        return base.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
    
    /// <summary>
    /// No-op implementation of IDocumentRepository for testing.
    /// Individual tests can override with specific mocks as needed.
    /// </summary>
    private class NoOpDocumentRepository : IDocumentRepository
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
}
