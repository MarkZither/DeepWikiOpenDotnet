using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.SqlServer.DbContexts;

/// <summary>
/// EF Core DbContext for SQL Server 2025 vector database.
/// Provides DbSet for documents and configures connection, retry policy, and logging.
/// </summary>
public class SqlServerVectorDbContext : DbContext
{
    public SqlServerVectorDbContext(DbContextOptions<SqlServerVectorDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents { get; set; } = null!;

    // ── Wiki entities ──────────────────────────────────────────────────────
    public DbSet<WikiEntity> Wikis { get; set; } = null!;
    public DbSet<WikiPageEntity> WikiPages { get; set; } = null!;
    public DbSet<WikiPageRelation> WikiPageRelations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply entity configurations
        modelBuilder.ApplyConfiguration(new DocumentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WikiEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageRelationConfiguration());
    }

    /// <summary>
    /// Configures default retry policy for transient SQL Server failures.
    /// Note: This is only used when DbContext is instantiated without explicit options.
    /// The factory (design-time) provides fully configured options, so this won't be called during migrations.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Only configure if not already configured (e.g., DI container hasn't set options yet)
        if (!optionsBuilder.IsConfigured)
        {
            // This path is only for scenarios where DbContext is created without explicit options
            optionsBuilder.UseSqlServer(c => c
                .EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null)
            );
        }
    }
}
