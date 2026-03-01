using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.Postgres.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiPageRelation"/> in PostgreSQL.
/// Uses a composite primary key (source_page_id, target_page_id) with snake_case naming.
/// </summary>
public class WikiPageRelationConfiguration : IEntityTypeConfiguration<WikiPageRelation>
{
    public void Configure(EntityTypeBuilder<WikiPageRelation> builder)
    {
        builder.ToTable("wiki_page_relations");

        // Composite PK
        builder.HasKey(r => new { r.SourcePageId, r.TargetPageId });

        builder.Property(r => r.SourcePageId)
            .HasColumnType("uuid")
            .HasColumnName("source_page_id");

        builder.Property(r => r.TargetPageId)
            .HasColumnType("uuid")
            .HasColumnName("target_page_id");

        // FK: SourcePage — deleting a page removes all relations where it is the source
        builder.HasOne(r => r.SourcePage)
            .WithMany(p => p.SourceRelations)
            .HasForeignKey(r => r.SourcePageId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wiki_page_relations_source_page_id");

        // FK: TargetPage — deleting a page removes all relations that point to it
        builder.HasOne(r => r.TargetPage)
            .WithMany(p => p.TargetRelations)
            .HasForeignKey(r => r.TargetPageId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wiki_page_relations_target_page_id");

        // Index on TargetPageId for reverse-lookup ("which pages reference this page?")
        builder.HasIndex(r => r.TargetPageId)
            .HasDatabaseName("ix_wiki_page_relations_target_page_id");
    }
}
