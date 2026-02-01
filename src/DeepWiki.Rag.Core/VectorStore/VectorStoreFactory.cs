using DeepWiki.Data.Abstractions;
using System;
using System.Linq;
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
        return Create(_serviceProvider);
    }

    /// <summary>
    /// Creates an IVectorStore using the provided service provider scope.
    /// This allows resolving scoped services (like DbContexts) from the current scope instead of the root provider.
    /// </summary>
    public IVectorStore Create(IServiceProvider serviceProvider)
    {
        var section = _configuration.GetSection(ConfigurationSection);
        var provider = section["Provider"]?.ToLowerInvariant() ?? "sqlserver";

        return CreateForProvider(provider, serviceProvider);
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

        return CreateForProvider(provider, _serviceProvider);
    }

    private IVectorStore CreateForProvider(string provider, IServiceProvider serviceProvider)
    {
        var logger = _loggerFactory?.CreateLogger<VectorStoreFactory>();

        return provider.ToLowerInvariant() switch
        {
            "sqlserver" => CreateSqlServerStore(serviceProvider, logger),
            "postgres" or "postgresql" or "pgvector" => CreatePostgresStore(serviceProvider, logger),
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

        // Basic checks for explicit configured connection strings
        var providerLower = provider.ToLowerInvariant();
        if (providerLower == "sqlserver")
        {
            return !string.IsNullOrEmpty(section["SqlServer:ConnectionString"]) ||
                   !string.IsNullOrEmpty(_configuration.GetConnectionString("SqlServer"));
        }

        if (providerLower == "postgres" || providerLower == "postgresql")
        {
            if (!string.IsNullOrEmpty(section["Postgres:ConnectionString"]) ||
                !string.IsNullOrEmpty(_configuration.GetConnectionString("Postgres")))
            {
                return true;
            }

            // Also accept common connection string names provided by hosting/orchestration (e.g., Aspire sets 'deepwikidb')
            var connSection = _configuration.GetSection("ConnectionStrings");
            if (connSection.Exists())
            {
                var candidates = connSection.GetChildren().Where(c => !string.IsNullOrEmpty(c.Value));
                foreach (var c in candidates)
                {
                    var key = c.Key ?? string.Empty;
                    if (key.Equals("deepwikidb", StringComparison.OrdinalIgnoreCase) ||
                        key.IndexOf("postgres", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        key.IndexOf("deepwiki", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return false;
    }

    private IVectorStore CreateSqlServerStore(IServiceProvider serviceProvider, ILogger<VectorStoreFactory>? logger)
    {
        try
        {
            var store = serviceProvider.GetService<IVectorStore>();
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

    private IVectorStore CreatePostgresStore(IServiceProvider serviceProvider, ILogger<VectorStoreFactory>? logger)
    {
        try
        {
            var store = serviceProvider.GetService<IVectorStore>();
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
