using DeepWiki.ApiService.Tests.Api;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DeepWiki.ApiService.Tests.Api;

public class DocumentsControllerListTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public DocumentsControllerListTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_DefaultPagination_ReturnsOk()
    {
        // Arrange
        var items = new List<DocumentEntity>
        {
            new DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "https://repo/a", FilePath = "a.md", Title = "A", Text = "A text", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, TokenCount = 10, FileType = "md", IsCode = false },
            new DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "https://repo/a", FilePath = "b.md", Title = "B", Text = "B text", CreatedAt = DateTime.UtcNow.AddMinutes(-1), UpdatedAt = DateTime.UtcNow.AddMinutes(-1), TokenCount = 20, FileType = "md", IsCode = false }
        };

        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentRepository));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddScoped<IDocumentRepository>(_ => new MockRepository(items, items.Count));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/documents?page=1&pageSize=2", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DeepWiki.ApiService.Models.DocumentListResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(items.Count, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(items[0].Id, result.Items[0].Id);
    }

    [Fact]
    public async Task List_WithRepoFilter_ReturnsFilteredResults()
    {
        // Arrange
        var items = new List<DocumentEntity>
        {
            new DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "https://repo/filter", FilePath = "docs/x.md", Title = "X", Text = "X text", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, TokenCount = 5, FileType = "md", IsCode = false }
        };

        var capturedRepo = string.Empty;

        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentRepository));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddScoped<IDocumentRepository>(_ => new CapturingMockRepository(items, items.Count, r => capturedRepo = r ?? string.Empty));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/documents?page=1&pageSize=10&repoUrl=https://repo/filter", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DeepWiki.ApiService.Models.DocumentListResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(items.Count, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(items[0].Id, result.Items[0].Id);
        Assert.Equal("https://repo/filter", capturedRepo);
    }

    private class MockRepository : IDocumentRepository
    {
        private readonly List<DocumentEntity> _items;
        private readonly int _total;

        public MockRepository(List<DocumentEntity> items, int total)
        {
            _items = items;
            _total = total;
        }

        public Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult((_items, _total));
    }

    private class CapturingMockRepository : IDocumentRepository
    {
        private readonly List<DocumentEntity> _items;
        private readonly int _total;
        private readonly Action<string?> _onCapture;

        public CapturingMockRepository(List<DocumentEntity> items, int total, Action<string?> onCapture)
        {
            _items = items;
            _total = total;
            _onCapture = onCapture;
        }

        public Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
        {
            _onCapture(repoUrl);
            return Task.FromResult((_items, _total));
        }
    }
}