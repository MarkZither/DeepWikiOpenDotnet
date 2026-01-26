using Xunit;

namespace DeepWiki.Data.Postgres.Tests.VectorStore;

public class PostgresVectorStoreUnitTests
{
    [Fact(Skip = "Postgres provider uses pgvector mapping; unit tests should be integration tests against a Postgres instance. See tests/DeepWiki.Data.Postgres.Tests/Integration for coverage.")]
    public void PostgresVectorStore_UnitTests_AreIntegrationOnly()
    {
        // Intentionally skipped: Postgres vector mapping cannot be validated with EF InMemory provider.
    }
}