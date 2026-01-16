# Phase 0: Research & Decisions — Multi-Database Data Access Layer

**Date**: 2026-01-16  
**Phase**: Outline & Research  
**Purpose**: Resolve all NEEDS CLARIFICATION items and document technology choices

---

## Research Tasks Completed

### 1. EF Core 10.x Vector Type Support

**Question**: Does EF Core 10.x natively support SQL Server 2025 vector type and PostgreSQL pgvector?

**Decision**: Use native column type mapping with custom configurations

**Rationale**:
- EF Core 10.x supports custom column type mappings via `HasColumnType("vector(1536)")`
- SQL Server 2025 vector type requires explicit column type annotation in entity configuration
- PostgreSQL pgvector extension requires `CREATE EXTENSION IF NOT EXISTS vector` and column type `vector(1536)`
- Float array (`float[]`) property in entity model maps to database-specific vector columns through provider-specific configurations

**Alternatives Considered**:
- Store embeddings as VARBINARY/BYTEA: Rejected due to lack of native ANN index support and manual serialization overhead
- Use JSON columns: Rejected due to poor query performance and lack of vector index support
- Wait for official EF Core vector type support: Rejected as timeline uncertain; custom column types are stable pattern

**Implementation Notes**:
- Base `DocumentEntity` uses `float[]? Embedding` property
- `SqlServerVectorDbContext` configuration maps to `vector(1536)` column type
- `PostgresVectorDbContext` configuration maps to `vector(1536)` with pgvector extension
- Value converters may be needed depending on EF Core 10 final release; test early in Phase 1.1

**References**:
- SQL Server 2025 vector type documentation
- PostgreSQL pgvector extension documentation
- EF Core custom type mapping: https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions

---

### 2. Vector Similarity Query Syntax

**Question**: What SQL syntax should be used for cosine similarity queries in SQL Server 2025 vs PostgreSQL pgvector?

**Decision**: 
- **SQL Server**: Use `VECTOR_DISTANCE('cosine', Embedding, @queryVector)` function
- **PostgreSQL**: Use `Embedding <=> @queryVector` operator (cosine distance)

**Rationale**:
- SQL Server 2025 provides built-in `VECTOR_DISTANCE()` function with distance metric parameter
- PostgreSQL pgvector uses operator-based syntax where `<=>` represents cosine distance
- Both return distance values (lower = more similar); ORDER BY ASC for nearest neighbors
- Parameterized queries via EF Core `FromSqlInterpolated` prevent SQL injection while supporting native vector operations

**Implementation Pattern** (pseudocode):
```sql
-- SQL Server
SELECT TOP(@k) *
FROM Documents
WHERE (@repoUrl IS NULL OR RepoUrl = @repoUrl)
ORDER BY VECTOR_DISTANCE('cosine', Embedding, @queryVector) ASC

-- PostgreSQL
SELECT *
FROM Documents
WHERE (@repoUrl IS NULL OR RepoUrl = @repoUrl)
ORDER BY Embedding <=> @queryVector
LIMIT @k
```

**Alternatives Considered**:
- Client-side cosine calculation: Rejected due to unacceptable performance at scale (requires loading all embeddings into memory)
- Generic ORM abstraction for vector queries: Rejected as no existing library supports both SQL Server 2025 and pgvector; raw SQL with parameterization is clearer

**Testing Strategy**:
- Create sample documents with known embeddings
- Query with exact match embedding (expect distance ≈ 0)
- Query with orthogonal vectors (expect distance ≈ 1 for cosine)
- Verify results ordered by distance ascending

---

### 3. Vector Index Strategy

**Question**: What indexing strategy should be used for 1536-dimensional embeddings to achieve <500ms query time at 10K documents and <2s at 3M documents?

**Decision**: 
- **SQL Server**: HNSW (Hierarchical Navigable Small World) index
- **PostgreSQL**: HNSW index (pgvector 0.5.0+) or IVFFlat as fallback

**Rationale**:
- HNSW provides best recall vs performance trade-off for high-dimensional vectors
- SQL Server 2025 native HNSW support for vector columns
- PostgreSQL pgvector 0.5.0+ supports HNSW; IVFFlat available in earlier versions
- HNSW recall typically 95-99% with 10-100x speedup over exhaustive search
- At 3M documents with 1536 dims, exhaustive search is infeasible (<10 QPS); HNSW achieves 100-500 QPS

**Index Creation Syntax**:
```sql
-- SQL Server (via migration SQL)
CREATE INDEX idx_documents_embedding
ON Documents(Embedding)
USING VECTOR
WITH (METHOD = 'HNSW', METRIC = 'COSINE');

-- PostgreSQL (via migration SQL)
CREATE INDEX idx_documents_embedding
ON Documents
USING hnsw (Embedding vector_cosine_ops);
```

**Configuration Parameters** (to be tuned in Phase 1.2/1.3):
- **m** (HNSW connections): 16-32 (higher = better recall, more memory)
- **ef_construction**: 64-200 (higher = better index quality, slower build)
- **ef_search**: 40-100 (higher = better recall, slower query)

**Alternatives Considered**:
- IVFFlat only: Rejected as HNSW provides superior recall and speed for high-dimensional data
- No index (exhaustive search): Rejected due to performance requirements (SC-002a: <2s @ 3M docs impossible without indexing)
- Product Quantization (PQ): Deferred to future optimization; HNSW sufficient for v1

**Testing Strategy**:
- Benchmark query time at 1K, 10K, 100K, 1M, 3M document scales
- Measure recall against exhaustive search baseline (expect >95%)
- Document index build time and memory usage
- Test index maintenance overhead during bulk inserts

---

### 4. Retry Policy Implementation

**Question**: Should retry logic use EF Core's built-in `EnableRetryOnFailure` or Polly library?

**Decision**: Start with EF Core `EnableRetryOnFailure`; evaluate Polly for circuit breaker

**Rationale**:
- EF Core 10.x includes `EnableRetryOnFailure()` with exponential backoff for transient SQL errors
- Built-in retry detects provider-specific transient error codes (connection timeout, deadlock, etc.)
- Configuration: `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null)`
- Polly provides circuit breaker pattern not available in EF Core; evaluate if frequent DB unavailability occurs

**Implementation**:
```csharp
// In DbContext options configuration
options.UseSqlServer(connectionString, sqlOptions =>
{
    sqlOptions.EnableRetryOnFailure(
        maxRetryCount: 3,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorNumbersToAdd: null);
});
```

**Circuit Breaker Decision**: Defer to operational requirements
- If DB downtime is frequent (>1% of requests), add Polly circuit breaker around DbContext operations
- Circuit breaker prevents cascade failures by failing fast after threshold (e.g., 5 failures in 30s → open circuit for 60s)
- Monitor in production; add circuit breaker in future iteration if needed

**Alternatives Considered**:
- Polly from start: Rejected as EF Core retry sufficient for transient errors; circuit breaker adds complexity without proven need
- No retry policy: Rejected per constitution observability requirements and user clarification (FR-013a)

**Testing Strategy**:
- Unit tests with transient error simulation (mock exceptions)
- Integration tests with network fault injection (Testcontainers with toxiproxy)
- Verify exponential backoff timing
- Verify circuit breaker behavior if implemented

---

### 5. Connection String Management

**Question**: How should connection strings with credentials be managed across dev/test/prod environments?

**Decision**: User Secrets (dev) + Environment Variables (prod) + Azure Key Vault option

**Rationale** (from clarification session):
- **Development**: .NET User Secrets for local development (prevents accidental commits)
- **CI/Test**: Environment variables for Testcontainers connection strings
- **Production**: Environment variables (Docker/Kubernetes) or Azure Key Vault / HashiCorp Vault for sensitive deployments
- Configuration abstraction allows switching without code changes

**Implementation Pattern**:
```csharp
// Program.cs / Startup.cs
var connectionString = configuration.GetConnectionString("VectorDatabase");
// Falls back: appsettings.json → User Secrets → Env Vars → Key Vault (if configured)
```

**Security Requirements** (from constitution):
- No connection strings in source control (enforce via .gitignore and secret scanning)
- Rotate credentials regularly (document in ops runbook)
- Use least-privilege database accounts (read-only for query-only services)

**Alternatives Considered**:
- Hardcoded in appsettings.json: Rejected due to security risk and accidental exposure
- Key Vault only: Rejected as overkill for dev; environment variables simpler for CI/prod

**Testing Strategy**:
- Verify User Secrets load in dev environment
- Verify environment variable override in integration tests
- Document Key Vault setup in deployment guide (Phase 1.5)

---

### 6. Health Check Endpoint Implementation

**Question**: What should the health check endpoint report and how should it integrate with container orchestration?

**Decision**: ASP.NET Core Health Checks with database connectivity and version validation

**Rationale**:
- ASP.NET Core provides built-in health check middleware (`Microsoft.Extensions.Diagnostics.HealthChecks`)
- Health checks can be exposed at `/health` endpoint for Kubernetes liveness/readiness probes
- Custom health check implementations for SQL Server and PostgreSQL validate:
  - Database connectivity (simple SELECT 1 query)
  - Database version compatibility (SQL Server >= 2025, PostgreSQL >= 17)
  - pgvector extension availability (PostgreSQL only)

**Implementation Pattern**:
```csharp
// Health check registration
services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver_vector_db")
    .AddCheck<PostgresHealthCheck>("postgres_vector_db");

// Endpoint mapping
app.MapHealthChecks("/health");
```

**Health Check Response Format**:
```json
{
  "status": "Healthy|Degraded|Unhealthy",
  "totalDuration": "00:00:00.0234",
  "entries": {
    "sqlserver_vector_db": {
      "status": "Healthy",
      "description": "SQL Server 2025 (16.0.1000) with vector support",
      "duration": "00:00:00.0123"
    }
  }
}
```

**Failure Scenarios**:
- Cannot connect to database → Unhealthy + log connection string (sanitized) + error details
- Database version too old → Unhealthy + log actual vs required version
- pgvector extension missing → Unhealthy + log "pgvector extension not found"

**Alternatives Considered**:
- Custom `/status` endpoint: Rejected as health check middleware is standard and integrates with monitoring tools
- No health checks: Rejected per constitution observability requirements (FR-017, SC-011)

**Testing Strategy**:
- Unit tests for health check logic with mocked database connection
- Integration tests verify health endpoint returns 200 OK when database available
- Integration tests verify health endpoint returns 503 Service Unavailable when database down
- Test version validation with incompatible database version (mock)

---

### 7. Testcontainers Configuration

**Question**: What Docker images and configuration should be used for SQL Server and PostgreSQL integration tests?

**Decision**: 
- **SQL Server**: `mcr.microsoft.com/mssql/server:2025-latest` (when available; fallback to 2022 for prototyping)
- **PostgreSQL**: `pgvector/pgvector:pg17` (official pgvector image with PostgreSQL 17)

**Rationale**:
- Testcontainers provides isolated, reproducible database instances for integration tests
- SQL Server 2025 image required for native vector type support; if unavailable, use 2022 and skip vector-specific tests (with TODO)
- pgvector/pgvector image includes extension pre-installed, simplifying test setup
- Containers start fresh per test class (via IClassFixture), ensuring test isolation

**Configuration**:
```csharp
// SQL Server container
var sqlServerContainer = new ContainerBuilder()
    .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("SA_PASSWORD", "YourStrong!Passw0rd")
    .WithPortBinding(1433, true)
    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
    .Build();

// PostgreSQL container
var postgresContainer = new ContainerBuilder()
    .WithImage("pgvector/pgvector:pg17")
    .WithEnvironment("POSTGRES_PASSWORD", "password")
    .WithPortBinding(5432, true)
    .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready"))
    .Build();
```

**Test Execution Flow**:
1. Container starts before test class execution (IClassFixture setup)
2. Run EF migrations to create schema + vector indexes
3. Execute integration tests (insert, query, update, delete)
4. Container destroyed after test class completion

**Resource Limits** (for CI):
- Memory limit: 2GB per container
- CPU limit: 1 core per container
- Startup timeout: 60 seconds
- Test timeout: 5 minutes per test class

**Alternatives Considered**:
- Shared database instance across tests: Rejected due to test interference risk
- Docker Compose for test databases: Rejected as Testcontainers provides programmatic control and automatic cleanup
- In-memory provider only: Rejected as vector queries require real database for integration testing

**Testing Strategy**:
- Verify container starts successfully and migrations apply
- Verify tests can connect and execute queries
- Verify container cleanup on test completion
- Document local Docker requirements (Docker Desktop or Docker Engine)

---

## Summary of Decisions

| Area | Decision | Impact |
|------|----------|--------|
| **Vector Type Mapping** | Custom column type configurations per provider | Requires provider-specific EF configurations |
| **Query Syntax** | VECTOR_DISTANCE() (SQL Server), <=> (Postgres) | Raw SQL via FromSqlInterpolated |
| **Indexing** | HNSW primary, IVFFlat fallback | Migration SQL scripts required |
| **Retry Policy** | EF Core EnableRetryOnFailure (3x exponential) | DbContext options configuration |
| **Circuit Breaker** | Defer to operational needs | Add Polly if >1% DB downtime |
| **Connection Strings** | User Secrets (dev), Env Vars (prod), Key Vault (option) | Configuration abstraction layer |
| **Health Checks** | ASP.NET Core middleware with version validation | Custom health check implementations |
| **Test Databases** | Testcontainers (SQL Server 2025, pgvector/pgvector:pg17) | Docker required for integration tests |

---

## Unresolved Clarifications

**None** - All critical unknowns resolved. Ready to proceed to Phase 1: Design.

---

## Next Steps

Proceed to **Phase 1: Design & Contracts** to generate:
1. `data-model.md` - Detailed entity model with validation rules
2. `contracts/` - Interface specifications (IVectorStore, IDocumentRepository)
3. `quickstart.md` - Developer onboarding guide with setup instructions
