namespace DeepWiki.ApiService.Configuration;

/// <summary>
/// Configuration options for vector store provider selection.
/// </summary>
public sealed class VectorStoreOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "VectorStore";

    /// <summary>
    /// Vector store provider: "sqlserver" or "postgres".
    /// </summary>
    public string Provider { get; set; } = "sqlserver";

    /// <summary>
    /// SQL Server-specific configuration.
    /// </summary>
    public SqlServerVectorStoreOptions SqlServer { get; set; } = new();

    /// <summary>
    /// PostgreSQL-specific configuration.
    /// </summary>
    public PostgresVectorStoreOptions Postgres { get; set; } = new();
}
