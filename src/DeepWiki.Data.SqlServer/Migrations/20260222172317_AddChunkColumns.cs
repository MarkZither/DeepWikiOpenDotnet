using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepWiki.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChunkIndex",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalChunks",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChunkIndex",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TotalChunks",
                table: "Documents");
        }
    }
}
