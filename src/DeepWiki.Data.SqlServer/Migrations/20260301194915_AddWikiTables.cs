// Migration Checklist — AddWikiTables (SQL Server)
// ==================================================
// (1) Index impact:
//     - IX_Wikis_CollectionId                     on Wikis(CollectionId)
//     - IX_WikiPages_WikiId                       on WikiPages(WikiId)
//     - IX_WikiPages_SectionPath                  on WikiPages(SectionPath)
//     - IX_WikiPageRelations_TargetPageId         on WikiPageRelations(TargetPageId)
// (2) No downtime expected — additive-only schema change (new tables and indexes only).
// (3) Rollback: dotnet ef migrations remove  (from src/DeepWiki.Data.SqlServer/)

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepWiki.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWikiTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wikis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wikis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WikiPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WikiId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SectionPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ParentPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WikiPages_ParentPageId",
                        column: x => x.ParentPageId,
                        principalTable: "WikiPages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiPages_WikiId",
                        column: x => x.WikiId,
                        principalTable: "Wikis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WikiPageRelations",
                columns: table => new
                {
                    SourcePageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiPageRelations", x => new { x.SourcePageId, x.TargetPageId });
                    table.ForeignKey(
                        name: "FK_WikiPageRelations_SourcePageId",
                        column: x => x.SourcePageId,
                        principalTable: "WikiPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WikiPageRelations_TargetPageId",
                        column: x => x.TargetPageId,
                        principalTable: "WikiPages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WikiPageRelations_TargetPageId",
                table: "WikiPageRelations",
                column: "TargetPageId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_ParentPageId",
                table: "WikiPages",
                column: "ParentPageId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_SectionPath",
                table: "WikiPages",
                column: "SectionPath");

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_WikiId",
                table: "WikiPages",
                column: "WikiId");

            migrationBuilder.CreateIndex(
                name: "IX_Wikis_CollectionId",
                table: "Wikis",
                column: "CollectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WikiPageRelations");

            migrationBuilder.DropTable(
                name: "WikiPages");

            migrationBuilder.DropTable(
                name: "Wikis");
        }
    }
}
