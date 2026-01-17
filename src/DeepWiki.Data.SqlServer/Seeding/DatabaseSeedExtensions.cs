using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Seeds;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.SqlServer.Seeding;

/// <summary>
/// Database seeding utility for SQL Server.
/// Ensures seed data is idempotent - safe to run multiple times.
/// </summary>
public static class DatabaseSeedExtensions
{
    /// <summary>
    /// Apply seed data to the database if it doesn't already exist.
    /// This method is idempotent and safe to call multiple times.
    /// </summary>
    public static async Task SeedInitialDataAsync(this SqlServerVectorDbContext context)
    {
        // If any documents exist, assume seeding is already done
        if (await context.Documents.AnyAsync())
        {
            return;
        }

        var seedDocuments = InitialSeedData.GetSeedDocuments().ToList();
        
        context.Documents.AddRange(seedDocuments);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Clear all documents from the database (for testing/reset purposes).
    /// WARNING: Destructive operation - use with caution!
    /// </summary>
    public static async Task ClearAllDocumentsAsync(this SqlServerVectorDbContext context)
    {
        await context.Database.ExecuteSqlAsync($"DELETE FROM [Documents];");
    }
}
