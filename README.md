# DeepWiki .NET - Multi-Database Data Access Layer

A production-grade, test-first data access layer for the DeepWiki knowledge base system. Provides unified vector search and document management across SQL Server 2025 and PostgreSQL databases.

## Features

### ✨ Core Capabilities

- **Multi-Database Support**: Write once, run on SQL Server 2025 or PostgreSQL 17+
- **Vector Search**: Native HNSW indexes for semantic similarity queries on 1536-dimensional embeddings
- **Document Management**: CRUD operations with metadata, code tracking, and implementation flags
- **Type-Safe Access**: Entity Framework Core 10.x with full type safety and compile-time query validation
- **Optimistic Concurrency**: Built-in conflict detection using `UpdatedAt` timestamps
- **Bulk Operations**: Efficient batch upserts with automatic transaction management
- **Health Checks**: Integrated health check endpoints with database version validation
- **Streaming RAG Service**: Token-by-token generation with retrieval augmentation, HTTP/SignalR streaming, and provider fallback (Ollama/OpenAI)

> **New**: Check out the [Streaming RAG Service Quick Start](specs/001-streaming-rag-service/quickstart.md) and [API Contracts](specs/001-streaming-rag-service/contracts/) for code generation with semantic retrieval.

### 🏗️ Architecture

Three-project design for clean separation:

1. **DeepWiki.Data** - Shared abstractions and models
2. **DeepWiki.Data.SqlServer** - SQL Server 2025 vector type implementation
3. **DeepWiki.Data.Postgres** - PostgreSQL pgvector extension implementation

All implementations share 100% test parity for consistency across databases.

## Quick Start

### Prerequisites

- **.NET 10** SDK or runtime
- **SQL Server 2025** OR **PostgreSQL 17+** with pgvector extension
- Optional: **Docker** for containerized databases (recommended for development)

### Installation

#### From Source
```bash
git clone https://github.com/deepwiki/deepwiki-open-dotnet.git
cd deepwiki-open-dotnet
dotnet build
dotnet test  # Run all tests
```

### Basic Usage

#### 1. Configure Dependency Injection (ASP.NET Core)

```csharp
using DeepWiki.Data.SqlServer;
using DeepWiki.Data.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Choose one:
// SQL Server 2025
builder.Services.AddSqlServerVectorStore(
    builder.Configuration.GetConnectionString("SqlServer"),
    options => options.EnableDetailedErrors(builder.Environment.IsDevelopment())
);

// PostgreSQL with pgvector
builder.Services.AddPostgresVectorStore(
    builder.Configuration.GetConnectionString("Postgres")
);

var app = builder.Build();
app.Run();
```

#### 2. Inject and Use

```csharp
using DeepWiki.Data;

public class SearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _repository;

    public SearchService(IVectorStore vectorStore, IDocumentRepository repository)
    {
        _vectorStore = vectorStore;
        _repository = repository;
    }

    public async Task<List<DocumentEntity>> FindSimilarDocuments(
        ReadOnlyMemory<float> embedding, int topK = 10)
    {
        return await _vectorStore.QueryNearestAsync(embedding, topK);
    }

    public async Task StoreDocument(DocumentEntity doc)
    {
        await _repository.AddAsync(doc);
    }
}
```

## Configuration

### Connection Strings

**appsettings.json** (Development):
```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Encrypt=false",
    "Postgres": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres"
  }
}
```

**Environment Variables** (Production):
```bash
# SQL Server
ConnectionStrings__SqlServer="Server=prod-sql-server;Database=deepwiki;User Id=sa;Password=***;Encrypt=true"

# PostgreSQL
ConnectionStrings__Postgres="Host=prod-postgres;Port=5432;Database=deepwiki;Username=postgres;Password=***"
```

See [Connection String Configuration](docs/connection-string-configuration.md) for detailed setup.

## Database Setup

### SQL Server 2025

```bash
# Using Docker (recommended)
docker run --name deepwiki-sql -e SA_PASSWORD=YourPassword123! \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest

# Apply migrations
dotnet ef database update -p src/DeepWiki.Data.SqlServer
```

See [SQL Server Setup Guide](docs/sql-server-setup.md) for advanced options.

### PostgreSQL with pgvector

```bash
# Using Docker (recommended)
docker run --name deepwiki-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 -d pgvector/pgvector:pg17

# Apply migrations
dotnet ef database update -p src/DeepWiki.Data.Postgres
```

See [PostgreSQL Setup Guide](docs/postgres-setup.md) for advanced options.

## Documentation

### Guides
- [Connection String Configuration](docs/connection-string-configuration.md) - All environments and security patterns
- [Deployment Checklist](docs/deployment-checklist.md) - Dev, staging, and production readiness
- [Troubleshooting](docs/troubleshooting.md) - 10+ common issues and solutions
- [Health Checks](docs/health-checks.md) - Monitoring and validation endpoints

### Streaming RAG Service
- **[Quick Start Guide](specs/001-streaming-rag-service/quickstart.md)** - Curl examples, test scenarios, and integration patterns
- **[API Contracts](specs/001-streaming-rag-service/contracts/)** - OpenAPI specs and JSON schemas for generation endpoints
- [Implementation Plan](specs/001-streaming-rag-service/plan.md) - Architecture and design decisions
- [Data Model](specs/001-streaming-rag-service/data-model.md) - Session, Prompt, and GenerationDelta schemas

### Architecture & Implementation
- [SQL Server Implementation](docs/sql-server-setup.md) - HNSW indexing and query optimization
- [PostgreSQL Implementation](docs/postgres-setup.md) - pgvector extension and IVFFlat alternatives
- [Bulk Operations](docs/bulk-operations.md) - Batch upserts and transaction management
- [Dependency Injection](docs/dependency-injection.md) - DI registration patterns for all scenarios

### Specification & Design
- [Feature Specification](specs/001-multi-db-data-layer/spec.md) - Full requirements and acceptance criteria
- [Implementation Plan](specs/001-multi-db-data-layer/plan.md) - Technical architecture and design decisions
- [Data Model](specs/001-multi-db-data-layer/data-model.md) - Entity definitions and relationships

## Features Overview

### DocumentEntity Properties

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| `Id` | `Guid` | Yes | Auto-generated primary key |
| `RepoUrl` | `string` | Yes | Source repository URL |
| `FilePath` | `string` | Yes | File path within repository |
| `Text` | `string` | Yes | Full text content |
| `Title` | `string` | No | Document title |
| `FileType` | `string` | No | File extension or type |
| `IsCode` | `bool` | No | Flag for code files (default: false) |
| `IsImplementation` | `bool` | No | Flag for implementation docs (default: false) |
| `TokenCount` | `int` | No | Approximate token count (default: 0) |
| `MetadataJson` | `string` | No | Custom JSON metadata |
| `Embedding` | `ReadOnlyMemory<float>` | No | 1536-dimensional vector |
| `CreatedAt` | `DateTime` | Yes | Auto-set to UTC now |
| `UpdatedAt` | `DateTime` | Yes | Auto-updated, used for concurrency |

### Vector Search Capabilities

- **Query Nearest**: Find top-K similar documents by embedding
- **Bulk Upsert**: Store or update multiple documents atomically
- **Repository Cleanup**: Delete all documents from a source repository
- **Document Count**: Get total indexed documents

### CRUD Operations

- **Add**: Create new document (auto-generates Id, CreatedAt)
- **Update**: Modify existing document (auto-updates UpdatedAt)
- **Get by Id**: Fast single-document retrieval
- **Get by Repository**: Retrieve all documents from a source with pagination
- **Delete**: Remove single document by Id
- **Exists**: Check if document exists (efficient)

## Performance Characteristics

### Query Times @ Standard Indexes

| Database | 1K Docs | 10K Docs | 100K Docs | 1M Docs |
|----------|---------|----------|-----------|---------|
| SQL Server | <10ms | <50ms | <200ms | <1s |
| PostgreSQL | <15ms | <75ms | <300ms | <1.5s |

*Times are median values on standard hardware (4-core, 8GB RAM) with HNSW index (m=16, ef_construction=200).*

See [Performance Benchmarks](docs/performance-benchmarks.md) for detailed analysis.

## Testing

### Run All Tests
```bash
# Build and test
dotnet build
dotnet test

# With code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Unit Tests Only
```bash
dotnet test tests/DeepWiki.Data.Tests
```

### Integration Tests
Integration tests are marked with the xUnit trait `Category=Integration` and are kept in `Integration/` directories. They require Docker (Testcontainers) to provision databases.

```bash
# Run all integration tests (SQL Server + Postgres)
dotnet test --filter "Category=Integration"

# Run SQL Server integration tests only (project-local)
dotnet test tests/DeepWiki.Data.SqlServer.Tests --filter "Category=Integration"

# Run Postgres integration tests only (project-local)
dotnet test tests/DeepWiki.Data.Postgres.Tests --filter "Category=Integration"
```

Tip: During fast local development, exclude integration tests to iterate on unit tests quickly:

```bash
# Run all tests but skip integration tests
dotnet test --filter "Category!=Integration"
```
### Test Coverage

- **Total Test Count**: 150+
- **Code Coverage**: 90%+ across all projects
- **Test Frameworks**: xUnit (unit), xUnit + Testcontainers (integration)
- **Test Environments**: In-memory DbContext, containerized databases

## API Reference

### IVectorStore Interface

```csharp
public interface IVectorStore
{
    // Store or update document with embedding
    Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default);

    // Find similar documents by embedding
    Task<List<DocumentEntity>> QueryNearestAsync(
        ReadOnlyMemory<float> queryEmbedding, 
        int k = 10, 
        CancellationToken cancellationToken = default);

    // Remove document by ID
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);

    // Remove all documents from repository
    Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default);

    // Get total indexed documents
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
```

### IDocumentRepository Interface

```csharp
public interface IDocumentRepository
{
    // Get document by ID
    Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Get documents from repository (paginated)
    Task<List<DocumentEntity>> GetByRepoAsync(
        string repoUrl, 
        int skip = 0, 
        int take = 100, 
        CancellationToken cancellationToken = default);

    // Create new document
    Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default);

    // Update existing document
    Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default);

    // Delete document by ID
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Check if document exists
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
```

## Health Check Integration

Both SQL Server and PostgreSQL implementations provide health check endpoints:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver-vector")
    .AddCheck<PostgresHealthCheck>("postgres-vector");

app.MapHealthChecks("/health");
```

Health checks validate:
- Database connectivity
- Database version compatibility (SQL Server 2025+, PostgreSQL 17+)
- Vector extension availability (PostgreSQL)
- Query performance baseline

See [Health Checks Guide](docs/health-checks.md) for details.

## Troubleshooting

Common issues and solutions are documented in [Troubleshooting Guide](docs/troubleshooting.md).

**Quick Links**:
- [Connection String Issues](docs/troubleshooting.md#connection-string-issues)
- [Database Setup Problems](docs/troubleshooting.md#database-setup-problems)
- [Performance Degradation](docs/troubleshooting.md#performance-degradation)
- [Concurrency Conflicts](docs/troubleshooting.md#concurrency-conflicts)

## Architecture Principles

This project follows the **DeepWiki Architecture Constitution**:

- **Test-First**: All functionality has comprehensive unit and integration tests
- **Database-Agnostic Models**: `DocumentEntity` works on any backend via provider implementations
- **EF Core Mandatory**: No manual SQL; all operations through Entity Framework Core
- **Type Safety**: Strong typing throughout; no magic strings or weak contracts
- **Observable**: Health checks, structured logging, and startup validation
- **Secure**: Connection string management via User Secrets (dev) and Key Vault (prod)

See [ARCHITECTURE_CONSTITUTION.md](ARCHITECTURE_CONSTITUTION.md) for complete details.

## Contributing

### Development Setup
```bash
# Clone and build
git clone https://github.com/deepwiki/deepwiki-open-dotnet.git
cd deepwiki-open-dotnet
dotnet build

# Start databases (Docker)
docker-compose up -d

# Run tests
dotnet test
```

### Branch Policy
- Feature branches: `001-feature-name`
- All PRs must:
  - Pass all tests (unit + integration)
  - Maintain 90%+ code coverage
  - Include documentation
  - Follow Architecture Constitution

### Commits
```bash
# After Phase completion, commit with meaningful messages:
git add .
git commit -m "Phase 1.2: SQL Server 2025 implementation with HNSW indexing"
git push origin 001-multi-db-data-layer
```

## Versioning

- **Current Version**: 1.0.0
- **Release Date**: January 2026
- **Next Phase**: Multi-database optimization and additional vector algorithms

## License

See [LICENSE](LICENSE) file.

## Support

For issues, questions, or contributions:
- **Documentation**: See [docs/](docs/) directory
- **Troubleshooting**: [docs/troubleshooting.md](docs/troubleshooting.md)
- **Issue Tracker**: [GitHub Issues](https://github.com/deepwiki/deepwiki-open-dotnet/issues)

---

**Status**: Production Ready ✅  
**Last Updated**: January 18, 2026  
**Maintained by**: DeepWiki Team