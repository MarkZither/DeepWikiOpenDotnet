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
        // Start the container with retries - sometimes Docker images take time or transient failures occur
        var startSw = System.Diagnostics.Stopwatch.StartNew();
        var startMax = TimeSpan.FromMinutes(7);
        var startAttempt = 0;
        while (true)
        {
            try
            {
                await _container.StartAsync();
                break;
            }
            catch (Exception)
            {
                startAttempt++;
                if (startSw.Elapsed > startMax)
                    throw;

                // Backoff with cap (up to 10s)
                var delayMs = Math.Min(1000 * startAttempt, 10000);
                await Task.Delay(delayMs);
            }
        }

        // Wait for SQL Server to accept connections and be ready (retry with exponential backoff)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var maxWait = TimeSpan.FromMinutes(7); // allow up to 7 minutes for container startup in constrained CI
        var attempt = 0;
        var connBuilder = new SqlConnectionStringBuilder(ConnectionString)
        {
            ConnectTimeout = 60 // make connect attempts more patient
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

                // Exponential backoff with cap (up to 10s)
                var delayMs = Math.Min(1000 * attempt, 10000);
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
            ConnectTimeout = 60
        }.ConnectionString;

        var dbReadySw = System.Diagnostics.Stopwatch.StartNew();
        var dbMaxWait = TimeSpan.FromMinutes(7);
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

                var delayMs = Math.Min(1000 * attempt, 10000);
                await Task.Delay(delayMs);
            }
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _container.StopAsync();
        }
        catch { /* ignore stop errors on CI */ }

        try
        {
            await _container.DisposeAsync();
        }
        catch { /* ignore dispose errors on CI */ }
    }

    /// <summary>
    /// Creates a fresh DbContext for each test with the test database.
    /// </summary>
    public SqlServerVectorDbContext CreateDbContext()
    {
        var testConnectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "DeepWikiTest",
            ConnectTimeout = 60
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(testConnectionString, o => o.CommandTimeout(300).EnableRetryOnFailure(10, TimeSpan.FromSeconds(10), null))
            .Options;

        var context = new SqlServerVectorDbContext(options);
        
        // Apply migrations/create schema with a more tolerant retry to handle transient lock/race conditions
        var applySw = System.Diagnostics.Stopwatch.StartNew();
        var applyMax = TimeSpan.FromSeconds(120);
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
                if (applySw.Elapsed > applyMax || tries > 12)
                    throw;

                // Use synchronous wait here because this method is synchronous and used by tests.
                Task.Delay(Math.Min(500 * tries, 5000)).GetAwaiter().GetResult();
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
