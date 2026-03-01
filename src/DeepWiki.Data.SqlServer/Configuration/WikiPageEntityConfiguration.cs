using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.SqlServer.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiPageEntity"/> in SQL Server.
/// Uses PascalCase table/column naming per SQL Server conventions.
/// </summary>
public class WikiPageEntityConfiguration : IEntityTypeConfiguration<WikiPageEntity>
{
    public void Configure(EntityTypeBuilder<WikiPageEntity> builder)
    {
        builder.ToTable("WikiPages");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(p => p.WikiId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");

        // nvarchar(max) for potentially large Markdown content
        builder.Property(p => p.Content)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(p => p.SectionPath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnType("nvarchar(1000)");

        builder.Property(p => p.SortOrder)
            .IsRequired();

        builder.Property(p => p.ParentPageId)
            .HasColumnType("uniqueidentifier");

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnType("nvarchar(50)");

        builder.Property(p => p.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        // FK to owning wiki — cascade delete removes pages when the wiki is deleted
        builder.HasOne(p => p.Wiki)
            .WithMany(w => w.Pages)
            .HasForeignKey(p => p.WikiId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WikiPages_WikiId");

        // Self-referencing parent/child — no action to avoid multiple cascade paths in SQL Server
        builder.HasOne(p => p.ParentPage)
            .WithMany(p => p.ChildPages)
            .HasForeignKey(p => p.ParentPageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WikiPages_ParentPageId");

        // Index on WikiId for fetching all pages of a wiki
        builder.HasIndex(p => p.WikiId)
            .HasDatabaseName("IX_WikiPages_WikiId");

        // Index on SectionPath for section-based navigation
        builder.HasIndex(p => p.SectionPath)
            .HasDatabaseName("IX_WikiPages_SectionPath");
    }
}
