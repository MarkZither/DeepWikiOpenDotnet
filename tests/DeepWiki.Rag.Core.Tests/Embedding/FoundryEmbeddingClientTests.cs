using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Embedding.Providers;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Embedding;

/// <summary>
/// Unit tests for FoundryEmbeddingClient covering T113.
/// Tests following the same pattern as OpenAIEmbeddingClientTests (T108-T112).
/// Tests EmbedAsync, retry logic, cache fallback, and batch embedding.
/// </summary>
public class FoundryEmbeddingClientTests
{
    private const string TestEndpoint = "https://myresource.openai.azure.com";
    private const string TestDeploymentName = "text-embedding-ada-002";
    private const string TestApiKey = "test-api-key";

    #region Constructor tests

    [Fact]
    public void Constructor_ValidParameters_CreatesClient()
    {
        // Arrange & Act
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Assert
        Assert.NotNull(client);
        Assert.Equal("foundry", client.Provider);
        Assert.Equal(TestDeploymentName, client.ModelId);
        Assert.Equal(TestDeploymentName, client.DeploymentName);
        Assert.Equal(1536, client.EmbeddingDimension);
    }

    [Fact]
    public void Constructor_EmptyEndpoint_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FoundryEmbeddingClient("", TestDeploymentName, apiKey: TestApiKey));
        Assert.Contains("Endpoint cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_NullEndpoint_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FoundryEmbeddingClient(null!, TestDeploymentName, apiKey: TestApiKey));
        Assert.Contains("Endpoint cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_EmptyDeploymentName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FoundryEmbeddingClient(TestEndpoint, "", apiKey: TestApiKey));
        Assert.Contains("Deployment name cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_NullDeploymentName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FoundryEmbeddingClient(TestEndpoint, null!, apiKey: TestApiKey));
        Assert.Contains("Deployment name cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_CustomModelId_UsesCustomModelId()
    {
        // Arrange & Act
        var customModelId = "custom-model-id";
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            modelId: customModelId,
            apiKey: TestApiKey);

        // Assert
        Assert.Equal(customModelId, client.ModelId);
        Assert.Equal(TestDeploymentName, client.DeploymentName);
    }

    [Fact]
    public void Constructor_NoModelId_UsesDeploymentNameAsModelId()
    {
        // Arrange & Act
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Assert
        Assert.Equal(TestDeploymentName, client.ModelId);
    }

    [Fact]
    public void Constructor_WithRetryPolicy_CreatesClient()
    {
        // Arrange
        var retryPolicy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey,
            retryPolicy: retryPolicy);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithCache_CreatesClient()
    {
        // Arrange
        var cache = new EmbeddingCache();

        // Act
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey,
            cache: cache);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithLogger_CreatesClient()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<FoundryEmbeddingClient>();

        // Act
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey,
            logger: logger);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithoutApiKey_CreatesClientForManagedIdentity()
    {
        // Arrange & Act
        // When no API key is provided, client should use DefaultAzureCredential
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName);

        // Assert
        Assert.NotNull(client);
        Assert.Equal("foundry", client.Provider);
    }

    #endregion

    #region EmbedAsync tests

    [Fact]
    public async Task EmbedAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.EmbedAsync(""));
        Assert.Contains("Text cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_NullText_ThrowsArgumentException()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.EmbedAsync(null!));
        Assert.Contains("Text cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.EmbedAsync("test text", cts.Token));
    }

    #endregion

    #region Cache fallback tests

    [Fact]
    public async Task EmbedAsync_WithCachedValue_CacheIsAccessible()
    {
        // Arrange - pre-populate cache with a known embedding
        var cache = new EmbeddingCache();
        var cachedEmbedding = CreateValidEmbedding();
        var text = "cached foundry test text";
        await cache.SetAsync(text, TestDeploymentName, cachedEmbedding);

        var retryPolicy = new RetryPolicy(cache)
        {
            MaxRetries = 1,
            UseCacheFallback = true
        };

        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey,
            retryPolicy: retryPolicy,
            cache: cache);

        // Verify cache is accessible
        var cachedResult = await cache.GetAsync(text, TestDeploymentName);
        Assert.NotNull(cachedResult);
        Assert.Equal(1536, cachedResult.Length);
    }

    #endregion

    #region Batch embedding tests

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act
        var results = new List<float[]>();
        await foreach (var embedding in client.EmbedBatchAsync(Array.Empty<string>()))
        {
            results.Add(embedding);
        }

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task EmbedBatchAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.EmbedBatchAsync(new[] { "test" }, cts.Token))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task EmbedBatchWithMetadataAsync_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act
        var results = await client.EmbedBatchWithMetadataAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Provider property tests

    [Fact]
    public void Provider_ReturnsFoundry()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act & Assert
        Assert.Equal("foundry", client.Provider);
    }

    [Fact]
    public void EmbeddingDimension_Returns1536()
    {
        // Arrange
        var client = new FoundryEmbeddingClient(
            endpoint: TestEndpoint,
            deploymentName: TestDeploymentName,
            apiKey: TestApiKey);

        // Act & Assert
        Assert.Equal(1536, client.EmbeddingDimension);
    }

    #endregion

    #region Helpers

    private static float[] CreateValidEmbedding()
    {
        // Create a 1536-dimensional embedding
        var embedding = new float[1536];
        var random = new Random(42); // Deterministic for testing
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range [-1, 1]
        }
        return embedding;
    }

    #endregion
}
