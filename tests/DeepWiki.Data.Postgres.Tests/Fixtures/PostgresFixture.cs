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
        // Retry starting the container to handle transient Docker/testcontainers issues in CI
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var maxWait = TimeSpan.FromMinutes(3);
        var attempt = 0;
        while (true)
        {
            try
            {
                await _container.StartAsync();
                break;
            }
            catch (Exception)
            {
                attempt++;
                if (sw.Elapsed > maxWait)
                    throw;

                await Task.Delay(Math.Min(1000 * attempt, 10000));
            }
        }

        // Apply migrations to create the schema and pgvector extension (retry on transient failures)
        var migrateAttempts = 0;
        while (true)
        {
            try
            {
                using (var context = CreateDbContext())
                {
                    await context.Database.MigrateAsync();
                }
                break;
            }
            catch (Exception)
            {
                migrateAttempts++;
                if (migrateAttempts > 5)
                    throw;
                await Task.Delay(Math.Min(500 * migrateAttempts, 5000));
            }
        }
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
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
