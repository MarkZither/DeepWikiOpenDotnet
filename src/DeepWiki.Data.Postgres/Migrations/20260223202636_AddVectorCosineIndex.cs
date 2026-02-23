using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepWiki.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddVectorCosineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_documents_embedding_cosine",
                table: "documents",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:ef_construction", 64)
                .Annotation("Npgsql:StorageParameter:m", 16);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_embedding_cosine",
                table: "documents");
        }
    }
}
