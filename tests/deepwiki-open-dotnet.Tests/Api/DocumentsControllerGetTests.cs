using DeepWiki.ApiService.Tests.Api;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DeepWiki.ApiService.Tests.Api;

public class DocumentsControllerGetTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public DocumentsControllerGetTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WhenDocumentExists_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new DocumentEntity
        {
            Id = id,
            RepoUrl = "https://github.com/test/repo",
            FilePath = "src/Test.cs",
            Title = "Test Document",
            Text = "Document content",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MetadataJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TokenCount = 100,
            FileType = "cs",
            IsCode = true,
            IsImplementation = false
        };

        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace IDocumentRepository with mock
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentRepository));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddScoped<IDocumentRepository>(_ => new MockRepository(entity));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/documents/{id}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DeepWiki.Data.Abstractions.Models.DocumentDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal(entity.RepoUrl, result.RepoUrl);
        Assert.Equal(entity.FilePath, result.FilePath);
        Assert.Equal(entity.Title, result.Title);
    }

    [Fact]
    public async Task Get_WhenDocumentMissing_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentRepository));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddScoped<IDocumentRepository>(_ => new MockRepository(null));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/documents/{id}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<DeepWiki.ApiService.Models.ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("not found", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private class MockRepository : IDocumentRepository
    {
        private readonly DocumentEntity? _entity;

        public MockRepository(DocumentEntity? entity)
        {
            _entity = entity;
        }

        public Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_entity);
        public Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_entity != null && _entity.Id == id);
    }
}