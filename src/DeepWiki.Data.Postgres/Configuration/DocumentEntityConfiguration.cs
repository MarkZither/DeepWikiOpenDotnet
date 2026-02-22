using DeepWiki.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace DeepWiki.Data.Postgres.Configuration;

/// <summary>
/// EF Core entity configuration for DocumentEntity in PostgreSQL.
/// Configures pgvector extension and vector column mapping for optimal vector search.
/// </summary>
public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        // Apply shared provider-agnostic configuration
        new DeepWiki.Data.Configuration.SharedDocumentEntityConfiguration().Configure(builder);

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
        // Value converter: ReadOnlyMemory<float> (model) <-> Vector (provider) <-> vector(1536) (database)
        // This keeps the model database-agnostic while using pgvector's native vector functionality
        var embeddingConverter = new ValueConverter<ReadOnlyMemory<float>?, Vector?>(
            // Model to provider: ReadOnlyMemory<float> -> Vector
            v => v.HasValue ? new Vector(v.Value.ToArray()) : null,
            // Provider to model: Vector -> ReadOnlyMemory<float>
            v => v == null ? (ReadOnlyMemory<float>?)null : new ReadOnlyMemory<float>(v.ToArray()));

        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(embeddingConverter)
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

        builder.Property(d => d.ChunkIndex)
            .HasDefaultValue(0)
            .HasColumnName("chunk_index");

        builder.Property(d => d.TotalChunks)
            .HasDefaultValue(1)
            .HasColumnName("total_chunks");

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
