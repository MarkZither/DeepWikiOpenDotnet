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
    /// Reads configuration from "VectorStore:Provider" (required).
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
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "VectorStore:Provider is not configured. Set 'VectorStore:Provider' to 'postgres' or 'sqlserver' in appsettings.json or environment variables.");
        }
        provider = provider.ToLowerInvariant();

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
        var provider = _configuration.GetSection(ConfigurationSection)["Provider"];
        // Default to sqlserver when no explicit provider is configured to match existing test expectations
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "sqlserver";
        }
        return provider.ToLowerInvariant();
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
            var adapterType = Type.GetType("DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter, DeepWiki.Data.SqlServer");
            if (adapterType != null)
            {
                var adapterObj = serviceProvider.GetService(adapterType)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, adapterType);
                if (adapterObj is IVectorStore adapter)
                {
                    logger?.LogInformation("Created SQL Server vector store: {Type}", adapter.GetType().Name);
                    return adapter;
                }
            }

            throw new InvalidOperationException(
                "SQL Server vector store adapter (SqlServerVectorStoreAdapter) is not registered in the DI container. " +
                "Ensure AddSqlServerDataLayer() was called with a valid connection string during startup.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Failed to create SQL Server vector store. " +
                "Ensure AddSqlServerDataLayer() was called with a valid connection string during startup.", ex);
        }
    }

    private IVectorStore CreatePostgresStore(IServiceProvider serviceProvider, ILogger<VectorStoreFactory>? logger)
    {
        try
        {
            var adapterType = Type.GetType("DeepWiki.Data.Postgres.VectorStore.PostgresVectorStoreAdapter, DeepWiki.Data.Postgres");
            if (adapterType != null)
            {
                // GetService(concreteType) only succeeds when the type is registered directly;
                // ActivatorUtilities.CreateInstance resolves constructor dependencies from the container
                // so it works even when the type is only registered via its interface.
                var adapterObj = serviceProvider.GetService(adapterType)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, adapterType);
                if (adapterObj is IVectorStore adapter)
                {
                    logger?.LogInformation("Created PostgreSQL vector store: {Type}", adapter.GetType().Name);
                    return adapter;
                }
            }

            throw new InvalidOperationException(
                "PostgreSQL vector store adapter type could not be loaded from DeepWiki.Data.Postgres assembly. " +
                "Ensure the assembly is referenced and AddPostgresDataLayer() was called during startup.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Failed to create PostgreSQL vector store. " +
                "Ensure AddPostgresDataLayer() was called with a valid connection string during startup.", ex);
        }
    }
}
