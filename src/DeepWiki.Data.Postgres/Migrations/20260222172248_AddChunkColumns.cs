using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepWiki.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "total_chunks",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "total_chunks",
                table: "documents");
        }
    }
}
