using DeepWiki.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.Configuration;

/// <summary>
/// Shared, provider-agnostic EF Core configuration for <see cref="DocumentEntity"/>.
/// This configures common constraints, lengths, required flags and indexes while
/// leaving provider-specific column types, names, and defaults to the provider
/// specific configurations (Postgres/SqlServer).
/// </summary>
public class SharedDocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        // Key
        builder.HasKey(d => d.Id);

        // Common property constraints (no column types or names)
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

        builder.Property(d => d.FileType)
            .HasMaxLength(50);

        builder.Property(d => d.IsCode)
            .HasDefaultValue(false);

        builder.Property(d => d.IsImplementation)
            .HasDefaultValue(false);

        builder.Property(d => d.TokenCount);

        builder.Property(d => d.MetadataJson);

        // Common indexes
        builder.HasIndex(d => d.RepoUrl);
        builder.HasIndex(d => d.CreatedAt);
    }
}
