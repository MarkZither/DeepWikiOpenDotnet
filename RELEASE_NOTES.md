# Release Notes: v1.1.0 - RAG Query API (Polish)

**Release Date**: February 4, 2026  
**Status**: Feature Complete ✅  
**Type**: Minor Release

---

## Summary
Phase 8 (Polish & Cross-Cutting Concerns) for the `003-rag-query-api` feature has been completed. This release includes OpenAPI annotations, configuration samples, OpenAPI contract validation tests, quickstart verification, and related cleanup documented in `specs/003-rag-query-api/tasks.md` (T051-T057 completed).

### Highlights
- ✅ Added OpenAPI annotations to `QueryController` and `DocumentsController`
- ✅ Added `VectorStore` & `Embedding` sample configuration to `appsettings.json` and `appsettings.Development.json`
- ✅ Added `OpenApiContractTests` to validate generated OpenAPI doc and quickstart curl examples
- ✅ Enabled XML documentation generation for improved OpenAPI output
- ✅ Validated endpoints vs `specs/003-rag-query-api/contracts/openapi.yaml`
- ✅ Updated `specs/003-rag-query-api/tasks.md` to mark Phase 8 tasks T051–T057 as complete

---

# Release Notes: v1.0.0 - Multi-Database Data Access Layer

**Release Date**: January 18, 2026  
**Status**: Production Ready ✅  
**Type**: Major Release

---

## Overview

First production release of the DeepWiki .NET multi-database data access layer. Provides unified vector search and document management across SQL Server 2025 and PostgreSQL 17+ with comprehensive test coverage and production-grade documentation.

### Key Achievements

- ✅ Multi-database support (SQL Server 2025 + PostgreSQL 17+)
- ✅ 150+ tests with 90%+ code coverage
- ✅ Native HNSW vector indexing
- ✅ Test parity across all databases
- ✅ Production deployment guides
- ✅ Comprehensive documentation
- ✅ Health check integration
- ✅ Full architectural constitution compliance

---

## What's New

### Core Features

#### 1. Multi-Database Data Access Layer
- **DocumentEntity**: 13 properties with comprehensive validation
- **IVectorStore**: 5 methods for semantic search operations
- **IDocumentRepository**: 6 methods for CRUD operations
- **Shared Abstractions**: Database-agnostic model layer in DeepWiki.Data

#### 2. SQL Server 2025 Support
- Native `vector(1536)` type support
- HNSW index with m=16, ef_construction=200
- VECTOR_DISTANCE(COSINE) for similarity queries
- SqlServerVectorStore implementation
- SqlServerDocumentRepository implementation
- Version validation (2025+)
- Health check endpoint

#### 3. PostgreSQL 17+ Support
- pgvector extension integration
- HNSW index with identical parameters to SQL Server
- `<=>` cosine distance operator
- PostgresVectorStore implementation
- PostgresDocumentRepository implementation
- pgvector extension validation
- Health check endpoint

#### 4. Bulk Operations
- BulkUpsertAsync for efficient batch operations
- Transaction support with all-or-nothing semantics
- Configurable batch sizing (1000 documents per batch default)
- Memory-efficient streaming for large datasets

#### 5. Concurrency Control
- Optimistic concurrency using UpdatedAt timestamp
- DbUpdateConcurrencyException handling
- Conflict detection in updates
- Race condition handling

#### 6. Health Checks
- SqlServerHealthCheck with version validation
- PostgresHealthCheck with pgvector validation
- Database connectivity validation
- Schema readiness verification
- Index status checking

### Testing Infrastructure

#### Unit Tests (DeepWiki.Data.Tests)
- 31+ DocumentEntity tests
- 100% entity validation coverage
- JSON serialization round-trip tests
- Edge case handling (max lengths, null handling, timestamps)
- Embedding validation (1536-dimensional)

#### Integration Tests (DeepWiki.Data.SqlServer.Tests)
- 40+ SQL Server tests
- Testcontainers for container management
- HNSW index creation and usage
- Vector similarity query verification
- Concurrency and bulk operation tests

#### Integration Tests (DeepWiki.Data.Postgres.Tests)
- 40+ PostgreSQL tests
- Testcontainers for container management
- pgvector extension verification
- HNSW index creation and usage
- Cross-database parity verification

#### Test Parity
- 100% same test suite passes on both databases
- Identical vector similarity results
- Performance baseline compliance
- Result ordering verification

### Documentation

#### Setup & Deployment
- [README.md](../README.md) - Quick start and feature overview
- [Deployment Checklist](docs/deployment-checklist.md) - Dev/staging/production readiness
- [SQL Server Setup](docs/sql-server-setup.md) - SQL Server specific configuration
- [PostgreSQL Setup](docs/postgres-setup.md) - PostgreSQL specific configuration

#### Configuration & Usage
- [Connection String Configuration](docs/connection-string-configuration.md) - All environments
- [Dependency Injection](docs/dependency-injection.md) - DI registration patterns
- [Health Checks](docs/health-checks.md) - Health endpoint implementation
- [Bulk Operations](docs/bulk-operations.md) - Batch upsert patterns

#### Operations & Support
- [Troubleshooting Guide](docs/troubleshooting.md) - 20+ common issues
- [Architecture Constitution](../ARCHITECTURE_CONSTITUTION.md) - Design principles
- [Specification](specs/001-multi-db-data-layer/spec.md) - Requirements document
- [Implementation Plan](specs/001-multi-db-data-layer/plan.md) - Technical design

### Architecture

#### Three-Project Design
```
DeepWiki.Data/
├─ DocumentEntity (shared)
├─ IVectorStore (shared)
└─ IDocumentRepository (shared)

DeepWiki.Data.SqlServer/
├─ SqlServerVectorDbContext
├─ SqlServerVectorStore
├─ SqlServerDocumentRepository
└─ SqlServerHealthCheck

DeepWiki.Data.Postgres/
├─ PostgresVectorDbContext
├─ PostgresVectorStore
├─ PostgresDocumentRepository
└─ PostgresHealthCheck
```

#### Database-Agnostic Models
- ReadOnlyMemory<float> for embeddings
- Value converters for provider-specific types
- Shared migration strategy
- Consistent naming across databases

### Performance Characteristics

#### Query Performance
| Database | 1K Docs | 10K Docs | 100K Docs | 1M Docs |
|----------|---------|----------|-----------|---------|
| SQL Server | <10ms | <50ms | <200ms | <1s |
| PostgreSQL | <15ms | <75ms | <300ms | <1.5s |

#### Bulk Operations
- 1000 documents in <10 seconds
- 10,000 documents in <90 seconds
- Memory usage <500MB for 1000 documents
- Atomic transactions (all-or-nothing)

#### Concurrency
- Optimistic concurrency tokens
- Conflict detection and handling
- Concurrent update support
- Race condition prevention

---

## Breaking Changes

**None** - This is the first release.

---

## Deprecated Features

**None** - This is the first release.

---

## Known Issues & Limitations

### 1. SQL Server Version Requirement
- **Minimum**: SQL Server 2025
- **Why**: Vector type (vector(1536)) is new to SQL Server 2025
- **Workaround**: Use PostgreSQL if older SQL Server required

### 2. PostgreSQL pgvector Extension Required
- **Requirement**: Must manually enable pgvector extension
- **Command**: `CREATE EXTENSION IF NOT EXISTS vector;`
- **Container**: pgvector/pgvector:pg17 has it pre-installed

### 3. Fixed Embedding Dimension
- **Dimension**: 1536 fixed (OpenAI standard)
- **Note**: Design is extensible for future versions
- **Workaround**: None for v1.0.0

### 4. Cosine Similarity Only
- **Supported**: Cosine distance only
- **Rationale**: Best for semantic search use cases
- **Future**: L2/Euclidean distance may be added

### 5. Index Parameter Tuning
- **Current**: HNSW with m=16, ef_construction=200
- **Note**: Parameters are optimal for 1K-10M documents
- **Tuning**: Manual index recreation needed for different parameters

### 6. Connection Pool Configuration
- **Default**: Npgsql defaults (min=1, max=100)
- **Note**: May need tuning for very high concurrency (>1000 req/s)

### 7. Bulk Upsert Batch Limit
- **Current**: 1000 documents per batch
- **Note**: Larger batches may cause memory issues
- **Tuning**: Configurable in implementation

---

## Migration from Python Implementation

Not applicable - this is the .NET implementation of DeepWiki data layer.

---

## Dependencies

### Core
- **.NET 10.0** or later
- **Entity Framework Core 10.x**
- **System.Text.Json** (standard library)

### Database Drivers
- **Microsoft.Data.SqlClient** 5.x (SQL Server)
- **Npgsql 8.x** (PostgreSQL)

### Testing
- **xUnit 2.x** (unit tests)
- **Testcontainers 3.x** (integration tests)

### All Package Versions
See `src/DeepWiki.Data.SqlServer/DeepWiki.Data.SqlServer.csproj` and `src/DeepWiki.Data.Postgres/DeepWiki.Data.Postgres.csproj` for exact versions.

---

## Download & Installation

### From Source
```bash
git clone https://github.com/deepwiki/deepwiki-open-dotnet.git
cd deepwiki-open-dotnet
git checkout v1.0.0
dotnet build
dotnet test
```

### NuGet (When Available)
```bash
dotnet add package DeepWiki.Data
dotnet add package DeepWiki.Data.SqlServer
# OR
dotnet add package DeepWiki.Data.Postgres
```

---

## Supported Platforms

- **OS**: Linux, Windows, macOS
- **.NET**: .NET 10.0+
- **Databases**: 
  - SQL Server 2025+
  - PostgreSQL 17+ with pgvector
- **Containers**: Docker, Kubernetes

---

## Documentation

- **Complete Documentation**: See [docs/](docs/) directory
- **Quick Start**: [README.md](../README.md)
- **Specification**: [specs/001-multi-db-data-layer/spec.md](specs/001-multi-db-data-layer/spec.md)
- **Architecture**: [ARCHITECTURE_CONSTITUTION.md](../ARCHITECTURE_CONSTITUTION.md)

---

## Feedback & Support

### Report Issues
- [GitHub Issues](https://github.com/deepwiki/deepwiki-open-dotnet/issues)
- Include: error message, steps to reproduce, environment details

### Feature Requests
- [GitHub Discussions](https://github.com/deepwiki/deepwiki-open-dotnet/discussions)
- Describe use case and expected behavior

### Questions
- [Troubleshooting Guide](docs/troubleshooting.md)
- Documentation in [docs/](docs/) directory

---

## What's Next?

### Planned for v1.1.0
- SQLite support with BLOB-based embeddings
- Additional vector algorithms (IVFFlat, LSH)
- Performance benchmarking with BenchmarkDotNet
- Custom index parameter tuning
- Connection string encryption

### Planned for v1.2.0
- Distributed vector search
- Sharding support
- Batch delete operations
- Advanced concurrency patterns
- Query result caching

### Planned for v2.0.0
- GraphQL API layer
- Subscription support
- Advanced filtering and full-text search
- Multi-tenant support
- Vector compression

---

## Contributors

- DeepWiki Team
- Architecture & Constitution: [ARCHITECTURE_CONSTITUTION.md](../ARCHITECTURE_CONSTITUTION.md)

---

## License

See [LICENSE](../LICENSE) file.

---

## Changelog

### v1.0.0 - January 18, 2026

#### Added
- ✅ Multi-database data access layer
- ✅ SQL Server 2025 support with vector type
- ✅ PostgreSQL 17+ support with pgvector
- ✅ 150+ comprehensive tests
- ✅ Health check integration
- ✅ Bulk operations
- ✅ Concurrency control
- ✅ Complete documentation (10+ guides)
- ✅ Deployment checklist
- ✅ Troubleshooting guide
- ✅ Production readiness verification

#### Tested
- ✅ DocumentEntity validation (100% coverage)
- ✅ Vector operations (1536-dimensional embeddings)
- ✅ CRUD operations
- ✅ Bulk upsert (atomic transactions)
- ✅ Concurrency handling
- ✅ Cross-database parity
- ✅ Performance targets (<500ms @ 10K docs)
- ✅ Integration with Testcontainers

#### Documented
- ✅ API reference
- ✅ Configuration guides
- ✅ Deployment procedures
- ✅ Architecture principles
- ✅ Known limitations
- ✅ Troubleshooting procedures

---

**Status**: Stable for Production ✅  
**Support**: Community-supported (open source)  
**Next Release**: TBD
