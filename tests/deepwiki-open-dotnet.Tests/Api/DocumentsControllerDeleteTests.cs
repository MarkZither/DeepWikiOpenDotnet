using DeepWiki.ApiService.Tests.Api;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DeepWiki.ApiService.Tests.Api;

public class DocumentsControllerDeleteTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public DocumentsControllerDeleteTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Delete_WhenDocumentExists_ReturnsNoContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var deleteCalled = false;

        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentRepository));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddScoped<IDocumentRepository>(_ => new MockRepository(true, id, () => deleteCalled = true));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/documents/{id}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(deleteCalled, "Expected repository.DeleteAsync to be called");
    }

    [Fact]
    public async Task Delete_WhenDocumentMissing_ReturnsNotFound()
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

                    services.AddScoped<IDocumentRepository>(_ => new MockRepository(false, id));
                });
            });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/documents/{id}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<DeepWiki.ApiService.Models.ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("not found", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private class MockRepository : IDocumentRepository
    {
        private readonly bool _exists;
        private readonly Guid _expectedId;
        private readonly Action? _onDelete;

        public MockRepository(bool exists, Guid expectedId, Action? onDelete = null)
        {
            _exists = exists;
            _expectedId = expectedId;
            _onDelete = onDelete;
        }

        public Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id != _expectedId) throw new InvalidOperationException("Unexpected id");
            _onDelete?.Invoke();
            return Task.CompletedTask;
        }
        public Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_exists && id == _expectedId);
        public Task<(List<DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, bool firstChunkOnly = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}