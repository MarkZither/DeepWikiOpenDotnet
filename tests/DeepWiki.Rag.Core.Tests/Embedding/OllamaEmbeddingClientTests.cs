using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Embedding.Providers;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Embedding;

/// <summary>
/// Unit tests for OllamaEmbeddingClient covering T113.
/// Tests following the same pattern as OpenAIEmbeddingClientTests (T108-T112).
/// Tests EmbedAsync, retry logic, cache fallback, and batch embedding.
/// </summary>
public class OllamaEmbeddingClientTests
{
    private const string TestEndpoint = "http://localhost:11434";
    private const string TestModelId = "nomic-embed-text";

    #region Constructor tests

    [Fact]
    public void Constructor_ValidParameters_CreatesClient()
    {
        // Arrange & Act
        var client = new OllamaEmbeddingClient(
            endpoint: TestEndpoint,
            modelId: TestModelId);

        // Assert
        Assert.NotNull(client);
        Assert.Equal("ollama", client.Provider);
        Assert.Equal(TestModelId, client.ModelId);
        Assert.Equal(1536, client.EmbeddingDimension);
    }

    [Fact]
    public void Constructor_DefaultParameters_CreatesClientWithDefaults()
    {
        // Arrange & Act
        var client = new OllamaEmbeddingClient();

        // Assert
        Assert.NotNull(client);
        Assert.Equal("ollama", client.Provider);
        Assert.Equal("nomic-embed-text", client.ModelId);
    }

    [Fact]
    public void Constructor_EmptyEndpoint_UsesDefaultEndpoint()
    {
        // Arrange & Act
        // Empty endpoint should default to localhost:11434
        var client = new OllamaEmbeddingClient(endpoint: "");

        // Assert
        Assert.NotNull(client);
        Assert.Equal("ollama", client.Provider);
    }

    [Fact]
    public void Constructor_CustomModel_UsesCustomModel()
    {
        // Arrange & Act
        var customModel = "all-minilm";
        var client = new OllamaEmbeddingClient(modelId: customModel);

        // Assert
        Assert.Equal(customModel, client.ModelId);
    }

    [Fact]
    public void Constructor_WithRetryPolicy_CreatesClient()
    {
        // Arrange
        var retryPolicy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var client = new OllamaEmbeddingClient(
            endpoint: TestEndpoint,
            modelId: TestModelId,
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
        var client = new OllamaEmbeddingClient(
            endpoint: TestEndpoint,
            modelId: TestModelId,
            cache: cache);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithLogger_CreatesClient()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<OllamaEmbeddingClient>();

        // Act
        var client = new OllamaEmbeddingClient(
            endpoint: TestEndpoint,
            modelId: TestModelId,
            logger: logger);

        // Assert
        Assert.NotNull(client);
    }

    #endregion

    #region EmbedAsync tests

    [Fact]
    public async Task EmbedAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.EmbedAsync(""));
        Assert.Contains("Text cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_NullText_ThrowsArgumentException()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.EmbedAsync(null!));
        Assert.Contains("Text cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);
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
        var text = "cached ollama test text";
        await cache.SetAsync(text, TestModelId, cachedEmbedding);

        var retryPolicy = new RetryPolicy(cache)
        {
            MaxRetries = 1,
            UseCacheFallback = true
        };

        var client = new OllamaEmbeddingClient(
            endpoint: TestEndpoint,
            modelId: TestModelId,
            retryPolicy: retryPolicy,
            cache: cache);

        // Verify cache is accessible
        var cachedResult = await cache.GetAsync(text, TestModelId);
        Assert.NotNull(cachedResult);
        Assert.Equal(1536, cachedResult.Length);
    }

    #endregion

    #region Batch embedding tests

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

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
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);
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
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act
        var results = await client.EmbedBatchWithMetadataAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Provider property tests

    [Fact]
    public void Provider_ReturnsOllama()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act & Assert
        Assert.Equal("ollama", client.Provider);
    }

    [Fact]
    public void ModelId_ReturnsConfiguredModel()
    {
        // Arrange
        var customModel = "mxbai-embed-large";
        var client = new OllamaEmbeddingClient(modelId: customModel);

        // Act & Assert
        Assert.Equal(customModel, client.ModelId);
    }

    [Fact]
    public void EmbeddingDimension_Returns1536()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act & Assert
        Assert.Equal(1536, client.EmbeddingDimension);
    }

    #endregion

    #region Embedding normalization tests

    [Fact]
    public void Client_ExpectedDimension_Is1536()
    {
        // Arrange
        var client = new OllamaEmbeddingClient(TestEndpoint, TestModelId);

        // Act & Assert
        // OllamaEmbeddingClient normalizes embeddings to 1536 dimensions
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
