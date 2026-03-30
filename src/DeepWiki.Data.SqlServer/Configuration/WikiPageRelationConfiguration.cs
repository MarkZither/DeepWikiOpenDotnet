using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.SqlServer.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiPageRelation"/> in SQL Server.
/// Uses a composite primary key (SourcePageId, TargetPageId) with PascalCase naming.
/// </summary>
public class WikiPageRelationConfiguration : IEntityTypeConfiguration<WikiPageRelation>
{
    public void Configure(EntityTypeBuilder<WikiPageRelation> builder)
    {
        builder.ToTable("WikiPageRelations");

        // Composite PK
        builder.HasKey(r => new { r.SourcePageId, r.TargetPageId });

        builder.Property(r => r.SourcePageId)
            .HasColumnType("uniqueidentifier");

        builder.Property(r => r.TargetPageId)
            .HasColumnType("uniqueidentifier");

        // FK: SourcePage — cascade delete when the source page is removed
        builder.HasOne(r => r.SourcePage)
            .WithMany(p => p.SourceRelations)
            .HasForeignKey(r => r.SourcePageId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WikiPageRelations_SourcePageId");

        // FK: TargetPage — SQL Server limitation: two CASCADE paths from the same table to the same parent
        // table are disallowed ("multiple cascade paths"). Using NoAction here; the repository's
        // DeletePageAsync explicitly removes TargetRelations before deleting the page.
        builder.HasOne(r => r.TargetPage)
            .WithMany(p => p.TargetRelations)
            .HasForeignKey(r => r.TargetPageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WikiPageRelations_TargetPageId");

        // Index on TargetPageId for reverse-lookup ("which pages reference this page?")
        builder.HasIndex(r => r.TargetPageId)
            .HasDatabaseName("IX_WikiPageRelations_TargetPageId");
    }
}
