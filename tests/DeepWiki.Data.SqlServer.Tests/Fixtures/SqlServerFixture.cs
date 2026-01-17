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
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
        .WithPassword("Strong@Password123")
        .WithEnvironment("MSSQL_SA_PASSWORD", "Strong@Password123")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        
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
            .UseSqlServer(testConnectionString)
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
