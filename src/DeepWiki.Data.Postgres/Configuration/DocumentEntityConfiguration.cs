using DeepWiki.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.Postgres.Configuration;

/// <summary>
/// EF Core entity configuration for DocumentEntity in PostgreSQL.
/// Configures pgvector extension and vector column mapping for optimal vector search.
/// </summary>
public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        builder.Property(d => d.RepoUrl)
            .HasMaxLength(500)
            .IsRequired()
            .HasColumnName("repo_url");

        builder.Property(d => d.FilePath)
            .HasMaxLength(1000)
            .IsRequired()
            .HasColumnName("file_path");

        builder.Property(d => d.Title)
            .HasMaxLength(500)
            .IsRequired()
            .HasColumnName("title");

        builder.Property(d => d.Text)
            .IsRequired()
            .HasColumnName("text");

        // Store embedding as pgvector(1536) type
        // With pgvector extension, we can use the vector type directly
        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasColumnName("embedding");

        builder.Property(d => d.FileType)
            .HasMaxLength(50)
            .HasColumnName("file_type");

        builder.Property(d => d.IsCode)
            .HasDefaultValue(false)
            .HasColumnName("is_code");

        builder.Property(d => d.IsImplementation)
            .HasDefaultValue(false)
            .HasColumnName("is_implementation");

        builder.Property(d => d.TokenCount)
            .HasColumnName("token_count");

        builder.Property(d => d.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(d => d.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .IsConcurrencyToken()
            .HasColumnName("updated_at");

        builder.Property(d => d.MetadataJson)
            .HasColumnType("jsonb")
            .HasColumnName("metadata_json");

        // Table name
        builder.ToTable("documents");

        // Indexes for performance
        builder.HasIndex(d => d.RepoUrl)
            .HasDatabaseName("ix_documents_repo_url");

        builder.HasIndex(d => d.CreatedAt)
            .HasDatabaseName("ix_documents_created_at");

        // Note: pgvector HNSW indexes are created via raw SQL in migrations
        // because EF Core doesn't have native support for pgvector-specific index options
    }
}
