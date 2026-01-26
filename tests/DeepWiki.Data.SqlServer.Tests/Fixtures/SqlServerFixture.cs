using DeepWiki.Data.SqlServer;
using DeepWiki.Data.SqlServer.DbContexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace DeepWiki.Data.SqlServer.Tests.Fixtures;

/// <summary>
/// Provides a SQL Server 2025 container for integration testing.
/// Implements IAsyncLifetime for proper container lifecycle management.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
        .WithPassword("Strong@Password123")
        .WithEnvironment("MSSQL_SA_PASSWORD", "Strong@Password123")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Wait for SQL Server to accept connections and be ready (retry with backoff)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var maxWait = TimeSpan.FromSeconds(180);
        while (true)
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                }

                break; // success
            }
            catch (Exception)
            {
                if (sw.Elapsed > maxWait)
                    throw;

                await Task.Delay(1000);
            }
        }

        // Create database and enable vector extensions
        using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE DATABASE DeepWikiTest;
                    ALTER DATABASE DeepWikiTest SET TRUSTWORTHY ON;
                ";
                await command.ExecuteNonQueryAsync();
            }
        }

        // Wait for the new database to accept connections (DeepWikiTest) before proceeding
        var testDbConnectionString = ConnectionString.Replace("master", "DeepWikiTest");
        var dbReadySw = System.Diagnostics.Stopwatch.StartNew();
        var dbMaxWait = TimeSpan.FromSeconds(180);
        while (true)
        {
            try
            {
                using (var c = new SqlConnection(testDbConnectionString))
                {
                    await c.OpenAsync();
                }

                break; // success
            }
            catch (Exception)
            {
                if (dbReadySw.Elapsed > dbMaxWait)
                    throw;

                await Task.Delay(1000);
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
    public SqlServerVectorDbContext CreateDbContext()
    {
        var testConnectionString = ConnectionString.Replace("master", "DeepWikiTest");
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(testConnectionString, o => o.CommandTimeout(180))
            .Options;

        var context = new SqlServerVectorDbContext(options);
        
        // Apply migrations/create schema
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>
    /// Clears all data from the test database.
    /// </summary>
    public async Task ClearDatabaseAsync()
    {
        using (var connection = new SqlConnection(ConnectionString.Replace("master", "DeepWikiTest")))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Documents;";
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
