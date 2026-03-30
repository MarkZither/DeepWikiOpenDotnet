using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.Postgres.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiEntity"/> in PostgreSQL.
/// Uses snake_case column/table naming per Postgres conventions.
/// </summary>
public class WikiEntityConfiguration : IEntityTypeConfiguration<WikiEntity>
{
    public void Configure(EntityTypeBuilder<WikiEntity> builder)
    {
        builder.ToTable("wikis");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd()
            .HasColumnName("id");

        builder.Property(w => w.CollectionId)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("collection_id");

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(w => w.Description)
            .HasColumnName("description");

        builder.Property(w => w.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status");

        builder.Property(w => w.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(w => w.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .HasColumnName("updated_at");

        // Index on CollectionId for fast lookup by collection
        builder.HasIndex(w => w.CollectionId)
            .HasDatabaseName("ix_wikis_collection_id");
    }
}
