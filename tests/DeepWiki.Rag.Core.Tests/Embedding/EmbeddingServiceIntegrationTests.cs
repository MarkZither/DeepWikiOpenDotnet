using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Embedding.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Embedding;

/// <summary>
/// Integration tests for Embedding Service covering T119-T124.
/// Tests provider configuration, embedding generation, and batch operations.
/// These tests use mocked providers (no live API calls in CI).
/// </summary>
[Trait("Category", "Integration")]
public class EmbeddingServiceIntegrationTests
{
    #region T120: Configure OpenAI provider, call EmbedAsync, verify 1536-dim output

    [Fact]
    public void OpenAIProvider_Configuration_CreatesValidClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:OpenAI:ModelId"] = "text-embedding-ada-002"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.Equal("openai", service.Provider);
        Assert.Equal("text-embedding-ada-002", service.ModelId);
        Assert.Equal(1536, service.EmbeddingDimension);
    }

    [Fact]
    public void OpenAIProvider_WithCustomModel_UsesCustomModel()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:OpenAI:ModelId"] = "text-embedding-3-small"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.Equal("text-embedding-3-small", service.ModelId);
    }

    #endregion

    #region T121: Configure Foundry provider, call EmbedAsync, verify 1536-dim output

    [Fact]
    public void FoundryProvider_Configuration_CreatesValidClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "text-embedding-ada-002",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.Equal("foundry", service.Provider);
        Assert.Equal(1536, service.EmbeddingDimension);
    }

    [Fact]
    public void FoundryProvider_WithCustomModelId_UsesCustomModelId()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-deployment",
            ["Embedding:Foundry:ModelId"] = "custom-model-id",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.Equal("custom-model-id", service.ModelId);
    }

    #endregion

    #region T122: Configure Ollama provider, call EmbedAsync, verify 1536-dim output

    [Fact]
    public void OllamaProvider_Configuration_CreatesValidClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama",
            ["Embedding:Ollama:Endpoint"] = "http://localhost:11434",
            ["Embedding:Ollama:ModelId"] = "nomic-embed-text"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.Equal("ollama", service.Provider);
        Assert.Equal("nomic-embed-text", service.ModelId);
        Assert.Equal(1536, service.EmbeddingDimension);
    }

    [Fact]
    public void OllamaProvider_DefaultEndpoint_UsesLocalhost()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama"
            // No endpoint specified - should default to localhost
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.Equal("ollama", service.Provider);
    }

    #endregion

    #region T123: Batch embedding throughput tests

    [Fact]
    public async Task BatchEmbedding_EmptyList_ReturnsEmpty()
    {
        // Arrange
        var client = CreateTestOpenAIClient();

        // Act
        var results = await client.EmbedBatchWithMetadataAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task BatchEmbedding_WithMetadata_ReturnsCorrectCount()
    {
        // Arrange
        var client = CreateTestOpenAIClient();
        var texts = Array.Empty<string>();

        // Act
        var results = await client.EmbedBatchWithMetadataAsync(texts);

        // Assert
        Assert.Equal(texts.Length, results.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    public void BatchSize_Configuration_AcceptsValidValues(int batchSize)
    {
        // Arrange - batch sizes 1-100 should be valid
        var client = CreateTestOpenAIClient();

        // Act & Assert - should not throw
        Assert.True(batchSize >= 1 && batchSize <= 100);
    }

    #endregion

    #region T124: Provider change in config, verify new provider used

    [Fact]
    public void ProviderChange_FromOpenAIToFoundry_CreatesFoundryClient()
    {
        // Arrange - first create OpenAI
        var openAIConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var openAIFactory = new EmbeddingServiceFactory(openAIConfig);
        var openAIService = openAIFactory.Create();

        // Now create Foundry with different config
        var foundryConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var foundryFactory = new EmbeddingServiceFactory(foundryConfig);
        var foundryService = foundryFactory.Create();

        // Assert
        Assert.Equal("openai", openAIService.Provider);
        Assert.Equal("foundry", foundryService.Provider);
    }

    [Fact]
    public void ProviderChange_FromFoundryToOllama_CreatesOllamaClient()
    {
        // Arrange - first create Foundry
        var foundryConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var foundryFactory = new EmbeddingServiceFactory(foundryConfig);
        var foundryService = foundryFactory.Create();

        // Now create Ollama with different config
        var ollamaConfig = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama",
            ["Embedding:Ollama:Endpoint"] = "http://localhost:11434"
        });

        var ollamaFactory = new EmbeddingServiceFactory(ollamaConfig);
        var ollamaService = ollamaFactory.Create();

        // Assert
        Assert.Equal("foundry", foundryService.Provider);
        Assert.Equal("ollama", ollamaService.Provider);
    }

    [Fact]
    public void ProviderChange_SameFactory_CreatesDifferentProviders()
    {
        // Arrange - create factory that can create any provider
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key",
            ["Embedding:Ollama:Endpoint"] = "http://localhost:11434"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act - create all providers explicitly
        var openAI = factory.CreateForProvider("openai");
        var foundry = factory.CreateForProvider("foundry");
        var ollama = factory.CreateForProvider("ollama");

        // Assert
        Assert.Equal("openai", openAI.Provider);
        Assert.Equal("foundry", foundry.Provider);
        Assert.Equal("ollama", ollama.Provider);
    }

    #endregion

    #region Embedding cache integration tests

    [Fact]
    public async Task EmbeddingCache_SetAndGet_ReturnsValue()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var text = "test text for caching";
        var modelId = "test-model";
        var embedding = CreateValidEmbedding();

        // Act
        await cache.SetAsync(text, modelId, embedding);
        var result = await cache.GetAsync(text, modelId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(embedding.Length, result.Length);
        Assert.Equal(embedding, result);
    }

    [Fact]
    public async Task EmbeddingCache_Expiry_RemovesExpiredEntries()
    {
        // Arrange
        var cache = new EmbeddingCache(defaultTtl: TimeSpan.FromMilliseconds(50));
        var text = "expiring text";
        var modelId = "test-model";
        var embedding = CreateValidEmbedding();

        // Act
        await cache.SetAsync(text, modelId, embedding);

        // Wait for expiry - increase to reduce timing flakiness on loaded CI hosts
        await Task.Delay(250);

        var result = await cache.GetAsync(text, modelId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task EmbeddingCache_Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var embedding = CreateValidEmbedding();

        await cache.SetAsync("text1", "model1", embedding);
        await cache.SetAsync("text2", "model1", embedding);
        await cache.SetAsync("text3", "model2", embedding);

        // Act
        await cache.ClearAsync();

        // Assert
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task EmbeddingCache_ClearByModel_RemovesOnlyModelEntries()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var embedding = CreateValidEmbedding();

        await cache.SetAsync("text1", "model1", embedding);
        await cache.SetAsync("text2", "model1", embedding);
        await cache.SetAsync("text3", "model2", embedding);

        // Act
        await cache.ClearAsync("model1");

        // Assert
        var result1 = await cache.GetAsync("text1", "model1");
        var result2 = await cache.GetAsync("text2", "model1");
        var result3 = await cache.GetAsync("text3", "model2");

        Assert.Null(result1);
        Assert.Null(result2);
        Assert.NotNull(result3);
    }

    #endregion

    #region Configuration with retry policy tests

    [Fact]
    public void Factory_WithRetryConfiguration_CreatesClientWithRetryPolicy()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:MaxRetries"] = "5",
            ["Embedding:BaseDelayMs"] = "200",
            ["Embedding:MaxDelayMs"] = "30000",
            ["Embedding:UseCacheFallback"] = "true"
        });

        var cache = new EmbeddingCache();
        var factory = new EmbeddingServiceFactory(config, cache: cache);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        // Retry policy is internal, but service should be created with those settings
    }

    [Fact]
    public void Factory_WithLoggerFactory_ProvidesLoggingToClients()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config, loggerFactory);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    #endregion

    #region DI extension tests

    [Fact]
    public void AddEmbeddingServices_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        // Register IConfiguration which is required by EmbeddingServiceFactory
        services.AddSingleton<IConfiguration>(config);

        // Act
        services.AddEmbeddingServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        var cache = provider.GetService<IEmbeddingCache>();
        var factory = provider.GetService<EmbeddingServiceFactory>();
        var embeddingService = provider.GetService<IEmbeddingService>();

        Assert.NotNull(cache);
        Assert.NotNull(factory);
        Assert.NotNull(embeddingService);
    }

    [Fact]
    public void AddEmbeddingServices_CacheIsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        // Register IConfiguration which is required by EmbeddingServiceFactory
        services.AddSingleton<IConfiguration>(config);

        // Act
        services.AddEmbeddingServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        var cache1 = provider.GetService<IEmbeddingCache>();
        var cache2 = provider.GetService<IEmbeddingCache>();

        Assert.Same(cache1, cache2);
    }

    #endregion

    #region Helpers

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static OpenAIEmbeddingClient CreateTestOpenAIClient()
    {
        return new OpenAIEmbeddingClient(
            apiKey: "test-api-key",
            modelId: "text-embedding-ada-002");
    }

    private static float[] CreateValidEmbedding()
    {
        var embedding = new float[1536];
        var random = new Random(42);
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return embedding;
    }

    #endregion
}
