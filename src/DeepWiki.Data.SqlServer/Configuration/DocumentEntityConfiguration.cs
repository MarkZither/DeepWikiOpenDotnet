using DeepWiki.Data.Entities;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DeepWiki.Data.SqlServer.Configuration;

/// <summary>
/// EF Core entity configuration for DocumentEntity in SQL Server.
/// Configures vector(1536) column type and indexes for optimal vector search.
/// </summary>
public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        // Apply shared provider-agnostic configuration
        new DeepWiki.Data.Configuration.SharedDocumentEntityConfiguration().Configure(builder);

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(d => d.RepoUrl)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.FilePath)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(d => d.Title)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.Text)
            .IsRequired();

        // Store embedding as native SQL Server vector(1536) type
        // Value converter: ReadOnlyMemory<float> (model) <-> SqlVector<float> (provider) <-> vector(1536) (database)
        // This keeps the model database-agnostic while using SQL Server's native vector functionality
        var embeddingConverter = new ValueConverter<ReadOnlyMemory<float>?, SqlVector<float>?>(
            // Model to provider: ReadOnlyMemory<float> -> SqlVector<float>
            v => v.HasValue ? new SqlVector<float>(v.Value) : null,
            // Provider to model: SqlVector<float> -> ReadOnlyMemory<float>
            v => v.HasValue ? v.Value.Memory : null);

        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(embeddingConverter);

        builder.Property(d => d.FileType)
            .HasMaxLength(50);

        builder.Property(d => d.IsCode)
            .HasDefaultValue(false);

        builder.Property(d => d.IsImplementation)
            .HasDefaultValue(false);

        builder.Property(d => d.TokenCount);

        builder.Property(d => d.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(d => d.MetadataJson);

        // Table name
        builder.ToTable("Documents");

        // Indexes for performance
        builder.HasIndex(d => d.RepoUrl)
            .HasDatabaseName("IX_Documents_RepoUrl");

        builder.HasIndex(d => d.CreatedAt)
            .HasDatabaseName("IX_Documents_CreatedAt");

        // Note: Vector columns cannot use standard B-tree indexes.
        // Vector similarity search uses EF.Functions.VectorDistance() which doesn't require an index.
        // For large-scale performance, DiskANN indexes can be created via raw SQL after table creation.
    }
}
