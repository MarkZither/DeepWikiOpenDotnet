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

        // Wait for SQL Server to accept connections and be ready (retry with exponential backoff)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var maxWait = TimeSpan.FromMinutes(5); // allow up to 5 minutes for container startup in constrained CI
        var attempt = 0;
        var connBuilder = new SqlConnectionStringBuilder(ConnectionString)
        {
            ConnectTimeout = 30 // make connect attempts a bit more patient
        };

        while (true)
        {
            try
            {
                using (var connection = new SqlConnection(connBuilder.ConnectionString))
                {
                    await connection.OpenAsync();
                }

                break; // success
            }
            catch (Exception)
            {
                attempt++;
                if (sw.Elapsed > maxWait)
                    throw;

                // Exponential backoff with cap (up to 5s)
                var delayMs = Math.Min(1000 * attempt, 5000);
                await Task.Delay(delayMs);
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
        var testDbConnectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "DeepWikiTest",
            ConnectTimeout = 30
        }.ConnectionString;

        var dbReadySw = System.Diagnostics.Stopwatch.StartNew();
        var dbMaxWait = TimeSpan.FromMinutes(5);
        attempt = 0;
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
                attempt++;
                if (dbReadySw.Elapsed > dbMaxWait)
                    throw;

                var delayMs = Math.Min(1000 * attempt, 5000);
                await Task.Delay(delayMs);
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
        var testConnectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "DeepWikiTest",
            ConnectTimeout = 30
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(testConnectionString, o => o.CommandTimeout(180).EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null))
            .Options;

        var context = new SqlServerVectorDbContext(options);
        
        // Apply migrations/create schema with a small retry to handle transient lock/race conditions
        var applySw = System.Diagnostics.Stopwatch.StartNew();
        var applyMax = TimeSpan.FromSeconds(60);
        var tries = 0;
        while (true)
        {
            try
            {
                context.Database.EnsureCreated();
                break;
            }
            catch (Exception)
            {
                tries++;
                if (applySw.Elapsed > applyMax || tries > 6)
                    throw;

                // Use synchronous wait here because this method is synchronous and used by tests.
                Task.Delay(Math.Min(500 * tries, 2000)).GetAwaiter().GetResult();
            }
        }

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
