using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepWiki.Data.SqlServer.DbContexts;

/// <summary>
/// Design-time factory for creating SqlServerVectorDbContext instances.
/// Used by EF Core CLI tools (migrations, scaffolding, etc.).
/// </summary>
public class SqlServerVectorDbContextFactory : IDesignTimeDbContextFactory<SqlServerVectorDbContext>
{
    public SqlServerVectorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("DEEPWIKI_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Connection string not configured. Set CONNECTION_STRING or DEEPWIKI_CONNECTION_STRING environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<SqlServerVectorDbContext>();
        
        optionsBuilder.UseSqlServer(connectionString, options =>
        {
            options.MaxBatchSize(100);
            options.CommandTimeout(30);
            // Include retry policy for design-time context creation
            options.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });

        return new SqlServerVectorDbContext(optionsBuilder.Options);
    }
}
