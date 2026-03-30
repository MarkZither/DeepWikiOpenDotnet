// Migration Checklist — AddWikiTables (Postgres)
// =================================================
// (1) Index impact:
//     - ix_wikis_collection_id          on wikis(collection_id)
//     - ix_wiki_pages_wiki_id           on wiki_pages(wiki_id)
//     - ix_wiki_pages_section_path      on wiki_pages(section_path)
//     - ix_wiki_page_relations_target_page_id on wiki_page_relations(target_page_id)
// (2) No downtime expected — additive-only schema change (new tables and indexes only).
// (3) Rollback: dotnet ef migrations remove  (from src/DeepWiki.Data.Postgres/)

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepWiki.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWikiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wikis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    collection_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wikis", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wiki_pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    wiki_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    section_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    parent_page_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wiki_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_wiki_pages_parent_page_id",
                        column: x => x.parent_page_id,
                        principalTable: "wiki_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_wiki_pages_wiki_id",
                        column: x => x.wiki_id,
                        principalTable: "wikis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wiki_page_relations",
                columns: table => new
                {
                    source_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_page_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wiki_page_relations", x => new { x.source_page_id, x.target_page_id });
                    table.ForeignKey(
                        name: "fk_wiki_page_relations_source_page_id",
                        column: x => x.source_page_id,
                        principalTable: "wiki_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wiki_page_relations_target_page_id",
                        column: x => x.target_page_id,
                        principalTable: "wiki_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wiki_page_relations_target_page_id",
                table: "wiki_page_relations",
                column: "target_page_id");

            migrationBuilder.CreateIndex(
                name: "IX_wiki_pages_parent_page_id",
                table: "wiki_pages",
                column: "parent_page_id");

            migrationBuilder.CreateIndex(
                name: "ix_wiki_pages_section_path",
                table: "wiki_pages",
                column: "section_path");

            migrationBuilder.CreateIndex(
                name: "ix_wiki_pages_wiki_id",
                table: "wiki_pages",
                column: "wiki_id");

            migrationBuilder.CreateIndex(
                name: "ix_wikis_collection_id",
                table: "wikis",
                column: "collection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wiki_page_relations");

            migrationBuilder.DropTable(
                name: "wiki_pages");

            migrationBuilder.DropTable(
                name: "wikis");
        }
    }
}
