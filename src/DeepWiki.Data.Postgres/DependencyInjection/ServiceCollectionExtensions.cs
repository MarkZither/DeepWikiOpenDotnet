using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.Repositories;
using DeepWiki.Data.Postgres.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;

namespace DeepWiki.Data.Postgres.DependencyInjection;

/// <summary>
/// Dependency injection extension methods for registering PostgreSQL data access services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL with pgvector database services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="connectionString">PostgreSQL connection string with pgvector extension support.</param>
    /// <param name="configureOptions">Optional action to configure DbContextOptions for advanced scenarios.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// Registers:
    /// - PostgresVectorDbContext as DbContext
    /// - IVectorStore -> PostgresVectorStore
    /// - IDocumentRepository -> PostgresDocumentRepository
    /// 
    /// The connection string should target PostgreSQL 17+ with pgvector extension installed.
    /// pgvector is automatically integrated with the DbContext via UseVector() calls.
    /// 
    /// Example: "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=password"
    /// 
    /// To enable pgvector extension on first connection, run migration:
    /// await dbContext.Database.MigrateAsync();
    /// </remarks>
    /// <exception cref="ArgumentNullException">If services or connectionString is null or empty.</exception>
    public static IServiceCollection AddPostgresDataLayer(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        // Register DbContext with pgvector support
        services.AddDbContext<PostgresVectorDbContext>(options =>
        {
            // Build data source with pgvector vector type support
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            var dataSource = dataSourceBuilder.Build();

            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                // Register pgvector with EF Core
                npgsqlOptions.UseVector();
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            });

            configureOptions?.Invoke(options);
        });

        // Register repository and vector store implementations
        services.AddScoped<IPersistenceVectorStore, PostgresVectorStore>();
        services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
        services.AddScoped<IWikiRepository, PostgresWikiRepository>();
        
        // Register Abstractions adapter to provide DeepWiki.Data.Abstractions.IVectorStore backed by the provider implementation
        services.AddScoped<DeepWiki.Data.Abstractions.IVectorStore, PostgresVectorStoreAdapter>();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL with pgvector database services with connection string from configuration.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="connectionStringKey">Configuration key for the connection string (e.g., "ConnectionStrings:PostgresConnection").</param>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="configureOptions">Optional action to configure DbContextOptions.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// This overload retrieves the connection string from IConfiguration, useful in ASP.NET Core scenarios.
    /// Example in appsettings.json:
    /// {
    ///   "ConnectionStrings": {
    ///     "PostgresConnection": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=password"
    ///   }
    /// }
    /// 
    /// Usage in Program.cs:
    /// builder.Services.AddPostgresDataLayer("ConnectionStrings:PostgresConnection", builder.Configuration);
    /// </remarks>
    public static IServiceCollection AddPostgresDataLayer(
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

        return services.AddPostgresDataLayer(connectionString, configureOptions);
    }
}
