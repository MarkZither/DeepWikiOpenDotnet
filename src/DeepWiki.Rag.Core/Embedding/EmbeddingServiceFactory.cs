using DeepWiki.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// Factory for creating embedding service instances based on configuration.
/// Supports OpenAI, Microsoft AI Foundry, and Ollama providers.
/// </summary>
public sealed class EmbeddingServiceFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IEmbeddingCache? _cache;

    /// <summary>
    /// Configuration section name for embedding settings.
    /// </summary>
    public const string ConfigurationSection = "Embedding";

    /// <summary>
    /// Creates a new embedding service factory.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cache">Optional embedding cache for fallback.</param>
    public EmbeddingServiceFactory(
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null,
        IEmbeddingCache? cache = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory;
        _cache = cache;
    }

    /// <summary>
    /// Creates an embedding service based on the configured provider.
    /// Reads configuration from "Embedding:Provider" (defaults to "openai").
    /// </summary>
    /// <returns>An IEmbeddingService implementation for the configured provider.</returns>
    public IEmbeddingService Create()
    {
        var section = _configuration.GetSection(ConfigurationSection);
        var provider = section["Provider"]?.ToLowerInvariant() ?? "openai";

        return CreateForProvider(provider);
    }

    /// <summary>
    /// Creates an embedding service for a specific provider.
    /// </summary>
    /// <param name="provider">The provider name: "openai", "foundry", or "ollama".</param>
    /// <returns>An IEmbeddingService implementation for the specified provider.</returns>
    /// <exception cref="ArgumentException">Thrown when the provider is unknown.</exception>
    public IEmbeddingService CreateForProvider(string provider)
    {
        var section = _configuration.GetSection(ConfigurationSection);
        var retryPolicy = CreateRetryPolicy();

        return provider.ToLowerInvariant() switch
        {
            "openai" => CreateOpenAIClient(section, retryPolicy),
            "foundry" or "azure" or "azureopenai" => CreateFoundryClient(section, retryPolicy),
            "ollama" => CreateOllamaClient(section, retryPolicy),
            _ => throw new ArgumentException(
                $"Unknown embedding provider: '{provider}'. Supported providers: openai, foundry, ollama.",
                nameof(provider))
        };
    }

    /// <summary>
    /// Gets the configured provider name.
    /// </summary>
    public string GetConfiguredProvider()
    {
        return _configuration.GetSection(ConfigurationSection)["Provider"]?.ToLowerInvariant() ?? "openai";
    }

    /// <summary>
    /// Checks if a specific provider is available based on configuration.
    /// </summary>
    /// <param name="provider">The provider to check.</param>
    /// <returns>True if the provider has required configuration.</returns>
    public bool IsProviderAvailable(string provider)
    {
        var section = _configuration.GetSection(ConfigurationSection);

        return provider.ToLowerInvariant() switch
        {
            "openai" => !string.IsNullOrEmpty(section["OpenAI:ApiKey"]),
            "foundry" or "azure" => !string.IsNullOrEmpty(section["Foundry:Endpoint"]),
            "ollama" => !string.IsNullOrEmpty(section["Ollama:Endpoint"]),
            _ => false
        };
    }

    private RetryPolicy CreateRetryPolicy()
    {
        var section = _configuration.GetSection(ConfigurationSection);
        var logger = _loggerFactory?.CreateLogger<RetryPolicy>();

        return new RetryPolicy(_cache, logger)
        {
            MaxRetries = section.GetValue("MaxRetries", 3),
            BaseDelayMs = section.GetValue("BaseDelayMs", 100),
            MaxDelayMs = section.GetValue("MaxDelayMs", 10_000),
            UseCacheFallback = section.GetValue("UseCacheFallback", true)
        };
    }

    private Providers.OpenAIEmbeddingClient CreateOpenAIClient(IConfigurationSection section, RetryPolicy retryPolicy)
    {
        var openAISection = section.GetSection("OpenAI");

        var apiKey = openAISection["ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set Embedding:OpenAI:ApiKey or OPENAI_API_KEY environment variable.");

        var modelId = openAISection["ModelId"] ?? "text-embedding-ada-002";
        var endpoint = openAISection["Endpoint"]; // Optional for Azure OpenAI

        return new Providers.OpenAIEmbeddingClient(
            apiKey: apiKey,
            modelId: modelId,
            endpoint: endpoint,
            retryPolicy: retryPolicy,
            cache: _cache,
            logger: _loggerFactory?.CreateLogger<Providers.OpenAIEmbeddingClient>());
    }

    private Providers.FoundryEmbeddingClient CreateFoundryClient(IConfigurationSection section, RetryPolicy retryPolicy)
    {
        var foundrySection = section.GetSection("Foundry");

        var endpoint = foundrySection["Endpoint"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("Foundry/Azure OpenAI endpoint not configured. Set Embedding:Foundry:Endpoint or AZURE_OPENAI_ENDPOINT environment variable.");

        var apiKey = foundrySection["ApiKey"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        var deploymentName = foundrySection["DeploymentName"] ?? "text-embedding-ada-002";
        var modelId = foundrySection["ModelId"] ?? deploymentName;

        return new Providers.FoundryEmbeddingClient(
            endpoint: endpoint,
            deploymentName: deploymentName,
            modelId: modelId,
            apiKey: apiKey,
            retryPolicy: retryPolicy,
            cache: _cache,
            logger: _loggerFactory?.CreateLogger<Providers.FoundryEmbeddingClient>());
    }

    private Providers.OllamaEmbeddingClient CreateOllamaClient(IConfigurationSection section, RetryPolicy retryPolicy)
    {
        var ollamaSection = section.GetSection("Ollama");

        var endpoint = ollamaSection["Endpoint"]
            ?? Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
            ?? "http://localhost:11434";

        var modelId = ollamaSection["ModelId"] ?? "nomic-embed-text";

        return new Providers.OllamaEmbeddingClient(
            endpoint: endpoint,
            modelId: modelId,
            retryPolicy: retryPolicy,
            cache: _cache,
            logger: _loggerFactory?.CreateLogger<Providers.OllamaEmbeddingClient>());
    }
}

/// <summary>
/// Extension methods for registering embedding services in DI.
/// </summary>
public static class EmbeddingServiceExtensions
{
    /// <summary>
    /// Adds the embedding service factory and related services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEmbeddingServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register cache as singleton
        services.AddSingleton<IEmbeddingCache, EmbeddingCache>();

        // Register factory
        services.AddSingleton<EmbeddingServiceFactory>();

        // Register IEmbeddingService using factory
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var factory = sp.GetRequiredService<EmbeddingServiceFactory>();
            return factory.Create();
        });

        return services;
    }
}
