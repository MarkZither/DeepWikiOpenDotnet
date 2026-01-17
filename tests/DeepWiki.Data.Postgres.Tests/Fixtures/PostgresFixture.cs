using DeepWiki.Data.Postgres;
using DeepWiki.Data.Postgres.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.Fixtures;

/// <summary>
/// Provides a PostgreSQL 17 container with pgvector extension for integration testing.
/// Implements IAsyncLifetime for proper container lifecycle management.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17-latest")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        
        // Create database and enable pgvector extension
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            
            // Create test database
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE DATABASE deepwiki_test;";
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException ex) when (ex.Message.Contains("already exists"))
                {
                    // Database already exists, ignore
                }
            }
        }

        // Enable pgvector extension in test database
        var testConnectionString = ConnectionString.Replace("postgres", "deepwiki_test");
        using (var connection = new NpgsqlConnection(testConnectionString))
        {
            await connection.OpenAsync();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a fresh DbContext for each test with the test database.
    /// </summary>
    public PostgresVectorDbContext CreateDbContext()
    {
        var testConnectionString = ConnectionString.Replace("postgres", "deepwiki_test");
        var options = new DbContextOptionsBuilder<PostgresVectorDbContext>()
            .UseNpgsql(testConnectionString, npgsqlOptions => npgsqlOptions
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;

        var context = new PostgresVectorDbContext(options);
        
        // Apply migrations/create schema
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>
    /// Clears all data from the test database.
    /// </summary>
    public async Task ClearDatabaseAsync()
    {
        var testConnectionString = ConnectionString.Replace("postgres", "deepwiki_test");
        using (var connection = new NpgsqlConnection(testConnectionString))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM documents;";
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
