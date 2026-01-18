using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DeepWiki.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create pgvector extension
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    repo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    file_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_code = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_implementation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    metadata_json = table.Column<string>(type: "jsonb", maxLength: 1048576, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_created_at",
                table: "documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_documents_repo_url",
                table: "documents",
                column: "repo_url");

            // Create HNSW index for vector similarity search
            migrationBuilder.Sql(
                "CREATE INDEX ix_documents_embedding_hnsw ON documents " +
                "USING hnsw (embedding vector_cosine_ops) " +
                "WITH (m=16, ef_construction=200);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
