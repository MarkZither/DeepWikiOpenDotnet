using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Embedding.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Embedding;

/// <summary>
/// Unit tests for EmbeddingServiceFactory covering T103-T107.
/// Tests factory instantiation for OpenAI, Foundry, and Ollama providers,
/// and exception handling for unknown providers.
/// </summary>
public class EmbeddingServiceFactoryTests
{
    #region T104: Factory instantiates OpenAI client when config specifies "openai"

    [Fact]
    public void CreateForProvider_OpenAI_InstantiatesOpenAIClient()
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
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
        Assert.Equal("openai", service.Provider);
        Assert.Equal("text-embedding-ada-002", service.ModelId);
    }

    [Fact]
    public void Create_WithOpenAIConfigured_InstantiatesOpenAIClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    [Fact]
    public void CreateForProvider_OpenAI_WithAzureEndpoint_InstantiatesOpenAIClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "openai",
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:OpenAI:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:OpenAI:ModelId"] = "text-embedding-ada-002"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    [Fact]
    public void CreateForProvider_OpenAI_DefaultsToAda002Model()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
            // No ModelId specified
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.Equal("text-embedding-ada-002", service.ModelId);
    }

    #endregion

    #region T105: Factory instantiates Foundry client when config specifies "foundry"

    [Fact]
    public void CreateForProvider_Foundry_InstantiatesFoundryClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-embedding-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("foundry");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<FoundryEmbeddingClient>(service);
        Assert.Equal("foundry", service.Provider);
    }

    [Fact]
    public void CreateForProvider_Azure_InstantiatesFoundryClient()
    {
        // Arrange - "azure" should map to Foundry client
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "azure",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-embedding-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("azure");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<FoundryEmbeddingClient>(service);
    }

    [Fact]
    public void CreateForProvider_AzureOpenAI_InstantiatesFoundryClient()
    {
        // Arrange - "azureopenai" should map to Foundry client
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "azureopenai",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-embedding-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("azureopenai");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<FoundryEmbeddingClient>(service);
    }

    [Fact]
    public void Create_WithFoundryConfigured_InstantiatesFoundryClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry",
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com",
            ["Embedding:Foundry:DeploymentName"] = "my-embedding-deployment",
            ["Embedding:Foundry:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<FoundryEmbeddingClient>(service);
    }

    #endregion

    #region T106: Factory instantiates Ollama client when config specifies "ollama"

    [Fact]
    public void CreateForProvider_Ollama_InstantiatesOllamaClient()
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
        var service = factory.CreateForProvider("ollama");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OllamaEmbeddingClient>(service);
        Assert.Equal("ollama", service.Provider);
        Assert.Equal("nomic-embed-text", service.ModelId);
    }

    [Fact]
    public void CreateForProvider_Ollama_DefaultsToLocalhost()
    {
        // Arrange - no endpoint specified
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama"
            // No Endpoint - should default to localhost:11434
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("ollama");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OllamaEmbeddingClient>(service);
    }

    [Fact]
    public void Create_WithOllamaConfigured_InstantiatesOllamaClient()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama",
            ["Embedding:Ollama:Endpoint"] = "http://localhost:11434"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OllamaEmbeddingClient>(service);
    }

    #endregion

    #region T107: Factory throws exception for unknown provider

    [Fact]
    public void CreateForProvider_UnknownProvider_ThrowsArgumentException()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => factory.CreateForProvider("unknown-provider"));
        Assert.Contains("Unknown embedding provider", exception.Message);
        Assert.Contains("unknown-provider", exception.Message);
        Assert.Contains("Supported providers: openai, foundry, ollama", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("anthropic")]
    [InlineData("cohere")]
    [InlineData("huggingface")]
    public void CreateForProvider_InvalidProvider_ThrowsArgumentException(string provider)
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => factory.CreateForProvider(provider));
        Assert.Contains("Unknown embedding provider", exception.Message);
    }

    #endregion

    #region Additional factory tests

    [Fact]
    public void Create_DefaultsToOpenAI_WhenNoProviderSpecified()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
            // No Provider specified - should default to openai
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.Create();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    [Fact]
    public void GetConfiguredProvider_ReturnsConfiguredProvider()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "foundry"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var provider = factory.GetConfiguredProvider();

        // Assert
        Assert.Equal("foundry", provider);
    }

    [Fact]
    public void GetConfiguredProvider_DefaultsToOpenAI()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>()); // Empty config

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var provider = factory.GetConfiguredProvider();

        // Assert
        Assert.Equal("openai", provider);
    }

    [Fact]
    public void IsProviderAvailable_OpenAI_ReturnsTrueWhenApiKeyConfigured()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var available = factory.IsProviderAvailable("openai");

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsProviderAvailable_OpenAI_ReturnsFalseWhenNoApiKey()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>()); // No API key

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var available = factory.IsProviderAvailable("openai");

        // Assert
        Assert.False(available);
    }

    [Fact]
    public void IsProviderAvailable_Foundry_ReturnsTrueWhenEndpointConfigured()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Foundry:Endpoint"] = "https://myresource.openai.azure.com"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var available = factory.IsProviderAvailable("foundry");

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsProviderAvailable_Ollama_ReturnsTrueWhenEndpointConfigured()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Ollama:Endpoint"] = "http://localhost:11434"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var available = factory.IsProviderAvailable("ollama");

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsProviderAvailable_UnknownProvider_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var available = factory.IsProviderAvailable("unknown");

        // Assert
        Assert.False(available);
    }

    [Fact]
    public void CreateForProvider_CaseInsensitive()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var lowerCase = factory.CreateForProvider("openai");
        var upperCase = factory.CreateForProvider("OPENAI");
        var mixedCase = factory.CreateForProvider("OpenAI");

        // Assert
        Assert.IsType<OpenAIEmbeddingClient>(lowerCase);
        Assert.IsType<OpenAIEmbeddingClient>(upperCase);
        Assert.IsType<OpenAIEmbeddingClient>(mixedCase);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EmbeddingServiceFactory(null!));
    }

    [Fact]
    public void CreateForProvider_OpenAI_MissingApiKey_ThrowsInvalidOperationException()
    {
        // Arrange - no API key configured and no environment variable
        var config = CreateConfiguration(new Dictionary<string, string?>()); // Empty

        var factory = new EmbeddingServiceFactory(config);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateForProvider("openai"));
        Assert.Contains("API key not configured", exception.Message);
    }

    [Fact]
    public void CreateForProvider_Foundry_MissingEndpoint_ThrowsInvalidOperationException()
    {
        // Arrange - no endpoint configured and no environment variable
        var config = CreateConfiguration(new Dictionary<string, string?>()); // Empty

        var factory = new EmbeddingServiceFactory(config);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateForProvider("foundry"));
        Assert.Contains("endpoint not configured", exception.Message);
    }

    [Fact]
    public void Factory_WithLoggerFactory_CreatesProvidersWithLoggers()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config, loggerFactory);

        // Act
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    [Fact]
    public void Factory_WithCache_CreatesProvidersWithCache()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key"
        });

        var factory = new EmbeddingServiceFactory(config, cache: cache);

        // Act
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.NotNull(service);
        Assert.IsType<OpenAIEmbeddingClient>(service);
    }

    [Fact]
    public void Factory_ReadsRetryConfiguration()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:OpenAI:ApiKey"] = "test-api-key",
            ["Embedding:MaxRetries"] = "5",
            ["Embedding:BaseDelayMs"] = "200",
            ["Embedding:MaxDelayMs"] = "30000"
        });

        var factory = new EmbeddingServiceFactory(config);

        // Act
        var service = factory.CreateForProvider("openai");

        // Assert
        Assert.NotNull(service);
        // The retry policy configuration is internal, but service should be created successfully
    }

    #endregion

    #region Helpers

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    #endregion
}
