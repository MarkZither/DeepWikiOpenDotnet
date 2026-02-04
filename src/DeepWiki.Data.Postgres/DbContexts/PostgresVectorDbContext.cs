using DeepWiki.Data.Entities;
using DeepWiki.Data.Postgres.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DeepWiki.Data.Postgres.DbContexts;

/// <summary>
/// EF Core DbContext for PostgreSQL vector database.
/// Provides DbSet for documents and configures connection, retry policy, and logging.
/// Note: UseVector() for pgvector support must be configured at service registration, not here,
/// because Aspire uses DbContext pooling which doesn't allow OnConfiguring overrides.
/// </summary>
public class PostgresVectorDbContext : DbContext
{
    public PostgresVectorDbContext(DbContextOptions<PostgresVectorDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");
        
        // Apply entity configuration
        modelBuilder.ApplyConfiguration(new DocumentEntityConfiguration());
    }
}
