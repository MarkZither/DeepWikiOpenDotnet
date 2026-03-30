using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Data.Abstractions.VectorData;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using DeepWiki.Data.SqlServer.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeepWiki.Data.SqlServer.DependencyInjection;

/// <summary>
/// Dependency injection extension methods for registering SQL Server data access services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server vector database services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="connectionString">SQL Server connection string with vector support (SQL Server 2025+).</param>
    /// <param name="configureOptions">Optional action to configure DbContextOptions for advanced scenarios.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// Registers:
    /// - SqlServerVectorDbContext as DbContext
    /// - IVectorStore -> SqlServerVectorStore
    /// - IDocumentRepository -> SqlServerDocumentRepository
    /// 
    /// The connection string should target SQL Server 2025 or later with vector type support.
    /// Load the connection string securely from:
    /// - User Secrets (development): dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."
    /// - Environment variables (production): CONNECTION_STRING or DEEPWIKI_CONNECTION_STRING
    /// - Azure Key Vault (recommended for cloud deployments)
    /// 
    /// See dependency-injection.md for complete setup instructions.
    /// </remarks>
    /// <exception cref="ArgumentNullException">If services or connectionString is null or empty.</exception>
    public static IServiceCollection AddSqlServerDataLayer(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(connectionString)) 
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        // Register DbContext
        services.AddDbContext<SqlServerVectorDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            });
            
            configureOptions?.Invoke(options);
        });

        // Register repository and vector store implementations (provider / persistence interfaces)
        services.AddScoped<IPersistenceVectorStore, SqlServerVectorStore>();
        services.AddScoped<IDocumentRepository, SqlServerDocumentRepository>();
        services.AddScoped<IWikiRepository, SqlServerWikiRepository>();

        // Register Abstractions adapter to provide DeepWiki.Data.Abstractions.IVectorStore backed by the provider implementation
        services.AddScoped<DeepWiki.Data.Abstractions.IVectorStore, SqlServerVectorStoreAdapter>();

        // Register Microsoft.Extensions.VectorData implementations for modern vector store patterns
        services.AddScoped<IDocumentVectorStore, SqlServerDocumentVectorStore>();
        services.AddScoped<IDocumentVectorCollection>(sp =>
        {
            var store = sp.GetRequiredService<IDocumentVectorStore>();
            return store.GetDocumentCollection();
        });

        return services;
    }

    /// <summary>
    /// Adds SQL Server vector database services with connection string from configuration.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="connectionStringKey">Configuration key for the connection string (e.g., "ConnectionStrings:DefaultConnection").</param>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="configureOptions">Optional action to configure DbContextOptions.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// This overload retrieves the connection string from IConfiguration, useful in ASP.NET Core scenarios.
    /// Example in appsettings.json:
    /// {
    ///   "ConnectionStrings": {
    ///     "DefaultConnection": "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword;"
    ///   }
    /// }
    /// 
    /// Usage in Program.cs:
    /// builder.Services.AddSqlServerDataLayer("ConnectionStrings:DefaultConnection", builder.Configuration);
    /// </remarks>
    public static IServiceCollection AddSqlServerDataLayer(
        this IServiceCollection services,
        string connectionStringKey,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (string.IsNullOrWhiteSpace(connectionStringKey)) 
            throw new ArgumentException("Connection string key cannot be null or empty", nameof(connectionStringKey));

        var connectionString = configuration[connectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string '{connectionStringKey}' not found in configuration");

        return services.AddSqlServerDataLayer(connectionString, configureOptions);
    }
}
