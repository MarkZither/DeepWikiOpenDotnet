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
        var optionsBuilder = new DbContextOptionsBuilder<SqlServerVectorDbContext>();
        
        // Use default connection string for design-time
        // In real scenarios, this would be configured via appsettings.json or environment variables
        const string defaultConnectionString = "Server=localhost;Database=DeepWiki;User Id=sa;Password=Strong@Password123;Encrypt=false;TrustServerCertificate=true;";
        
        optionsBuilder.UseSqlServer(defaultConnectionString, options =>
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
