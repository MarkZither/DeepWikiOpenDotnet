namespace DeepWiki.ApiService.Configuration;

/// <summary>
/// SQL Server vector store configuration.
/// </summary>
public sealed class SqlServerVectorStoreOptions
{
    /// <summary>
    /// Connection string (can also use ConnectionStrings:SqlServer).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// HNSW index M parameter (default: 16).
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index ef_construction parameter (default: 200).
    /// </summary>
    public int HnswEfConstruction { get; set; } = 200;
}
