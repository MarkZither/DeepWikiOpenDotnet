using DeepWiki.Data.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepWiki.Data.Postgres.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="WikiPageEntity"/> in PostgreSQL.
/// Uses snake_case column/table naming per Postgres conventions.
/// </summary>
public class WikiPageEntityConfiguration : IEntityTypeConfiguration<WikiPageEntity>
{
    public void Configure(EntityTypeBuilder<WikiPageEntity> builder)
    {
        builder.ToTable("wiki_pages");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd()
            .HasColumnName("id");

        builder.Property(p => p.WikiId)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("wiki_id");

        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("title");

        builder.Property(p => p.Content)
            .IsRequired()
            .HasColumnType("text")
            .HasColumnName("content");

        builder.Property(p => p.SectionPath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("section_path");

        builder.Property(p => p.SortOrder)
            .IsRequired()
            .HasColumnName("sort_order");

        builder.Property(p => p.ParentPageId)
            .HasColumnType("uuid")
            .HasColumnName("parent_page_id");

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status");

        builder.Property(p => p.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired()
            .HasColumnName("updated_at");

        // FK to owning wiki — cascade delete removes pages when the wiki is deleted
        builder.HasOne(p => p.Wiki)
            .WithMany(w => w.Pages)
            .HasForeignKey(p => p.WikiId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wiki_pages_wiki_id");

        // Self-referencing parent/child — restrict delete to avoid accidental orphan cascades
        builder.HasOne(p => p.ParentPage)
            .WithMany(p => p.ChildPages)
            .HasForeignKey(p => p.ParentPageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_wiki_pages_parent_page_id");

        // Index on WikiId for fetching all pages of a wiki
        builder.HasIndex(p => p.WikiId)
            .HasDatabaseName("ix_wiki_pages_wiki_id");

        // Index on SectionPath for section-based navigation
        builder.HasIndex(p => p.SectionPath)
            .HasDatabaseName("ix_wiki_pages_section_path");
    }
}
