using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.SqlServer.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiEntity"/> in SQL Server.
/// Uses PascalCase table/column naming per SQL Server conventions.
/// </summary>
public class WikiEntityConfiguration : IEntityTypeConfiguration<WikiEntity>
{
    public void Configure(EntityTypeBuilder<WikiEntity> builder)
    {
        builder.ToTable("Wikis");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(w => w.CollectionId)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)");

        builder.Property(w => w.Description)
            .HasColumnType("nvarchar(max)");

        builder.Property(w => w.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnType("nvarchar(50)");

        builder.Property(w => w.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        // Index on CollectionId for fast lookup by collection
        builder.HasIndex(w => w.CollectionId)
            .HasDatabaseName("IX_Wikis_CollectionId");
    }
}
