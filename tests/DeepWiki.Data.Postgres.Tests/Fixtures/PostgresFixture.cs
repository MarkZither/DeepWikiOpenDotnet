using DeepWiki.Data.Postgres;
using DeepWiki.Data.Postgres.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.Fixtures;

/// <summary>
/// Provides a PostgreSQL container with pgvector extension for integration testing.
/// Uses pgvector/pgvector:pg17 which includes the pgvector extension pre-installed.
/// Implements IAsyncLifetime for proper container lifecycle management.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)  // Ensure container cleanup on disposal
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        
        // Apply migrations to create the schema and pgvector extension
        using (var context = CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        // CleanUpAsync handles both stopping and removing the container
        await _container.CleanUpAsync();
    }

    /// <summary>
    /// Creates a fresh DbContext for each test with pgvector support.
    /// Uses both NpgsqlDataSourceBuilder.UseVector() for type mapping
    /// and NpgsqlDbContextOptionsBuilder.UseVector() for EF Core model mapping.
    /// </summary>
    public PostgresVectorDbContext CreateDbContext()
    {
        // Build data source with Vector type mapping for Npgsql
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<PostgresVectorDbContext>();
        optionsBuilder.UseNpgsql(dataSource, options =>
        {
            options.UseVector(); // Also register with EF Core
            options.EnableRetryOnFailure(maxRetryCount: 3);
        });

        var context = new PostgresVectorDbContext(optionsBuilder.Options);
        return context;
    }

}
