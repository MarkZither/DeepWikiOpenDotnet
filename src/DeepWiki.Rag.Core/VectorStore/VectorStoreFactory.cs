using DeepWiki.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.VectorStore;

/// <summary>
/// Factory for creating vector store instances based on configuration.
/// Supports SQL Server 2025 and PostgreSQL with pgvector providers.
/// </summary>
public sealed class VectorStoreFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Configuration section name for vector store settings.
    /// </summary>
    public const string ConfigurationSection = "VectorStore";

    /// <summary>
    /// Creates a new vector store factory.
    /// </summary>
    /// <param name="serviceProvider">Service provider to resolve vector store implementations.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public VectorStoreFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a vector store instance based on the configured provider.
    /// Reads configuration from "VectorStore:Provider" (defaults to "sqlserver").
    /// </summary>
    /// <returns>An IVectorStore implementation for the configured provider.</returns>
    public IVectorStore Create()
    {
        var section = _configuration.GetSection(ConfigurationSection);
        var provider = section["Provider"]?.ToLowerInvariant() ?? "sqlserver";

        return CreateForProvider(provider);
    }

    /// <summary>
    /// Creates a vector store for a specific provider.
    /// </summary>
    /// <param name="provider">The provider name: "sqlserver" or "postgres".</param>
    /// <returns>An IVectorStore implementation for the specified provider.</returns>
    /// <exception cref="ArgumentException">Thrown when the provider is unknown.</exception>
    public IVectorStore CreateForProvider(string provider)
    {
        var logger = _loggerFactory?.CreateLogger<VectorStoreFactory>();

        return provider.ToLowerInvariant() switch
        {
            "sqlserver" => CreateSqlServerStore(logger),
            "postgres" or "postgresql" or "pgvector" => CreatePostgresStore(logger),
            _ => throw new ArgumentException(
                $"Unknown vector store provider: '{provider}'. Supported providers: sqlserver, postgres.",
                nameof(provider))
        };
    }

    /// <summary>
    /// Gets the configured provider name.
    /// </summary>
    public string GetConfiguredProvider()
    {
        return _configuration.GetSection(ConfigurationSection)["Provider"]?.ToLowerInvariant() ?? "sqlserver";
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
            "sqlserver" => !string.IsNullOrEmpty(section["SqlServer:ConnectionString"]) ||
                          !string.IsNullOrEmpty(_configuration.GetConnectionString("SqlServer")),
            "postgres" or "postgresql" => !string.IsNullOrEmpty(section["Postgres:ConnectionString"]) ||
                                         !string.IsNullOrEmpty(_configuration.GetConnectionString("Postgres")),
            _ => false
        };
    }

    private IVectorStore CreateSqlServerStore(ILogger<VectorStoreFactory>? logger)
    {
        try
        {
            var store = _serviceProvider.GetService<IVectorStore>();
            if (store == null)
            {
                logger?.LogWarning("SQL Server vector store not registered in DI container. Falling back to NoOpVectorStore.");
                return new NoOpVectorStore();
            }
            
            logger?.LogInformation("Created SQL Server vector store instance");
            return store;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create SQL Server vector store. Falling back to NoOpVectorStore.");
            return new NoOpVectorStore();
        }
    }

    private IVectorStore CreatePostgresStore(ILogger<VectorStoreFactory>? logger)
    {
        try
        {
            var store = _serviceProvider.GetService<IVectorStore>();
            if (store == null)
            {
                logger?.LogWarning("PostgreSQL vector store not registered in DI container. Falling back to NoOpVectorStore.");
                return new NoOpVectorStore();
            }
            
            logger?.LogInformation("Created PostgreSQL vector store instance");
            return store;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create PostgreSQL vector store. Falling back to NoOpVectorStore.");
            return new NoOpVectorStore();
        }
    }
}
