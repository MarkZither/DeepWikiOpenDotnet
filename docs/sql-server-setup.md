# SQL Server 2025 Vector Database Setup Guide

## Overview

This guide covers setting up and using SQL Server 2025 with the DeepWiki multi-database data access layer. SQL Server 2025 introduces native `vector` data type support for storing and querying high-dimensional embeddings efficiently.

## Architecture

### Database Schema

The implementation uses:
- **Entity**: `Documents` table with 13 columns
- **Vector Column**: `Embedding` (vector(1536)) - SQL Server native type
- **Vector Type**: `SqlVector<float>` in .NET (zero-copy, 1536-dimensional embeddings)
- **Indexing**: Standard B-tree indexes on `RepoUrl` and `CreatedAt`; vector searches use `VECTOR_DISTANCE()` function
- **Concurrency**: Optimistic locking via `UpdatedAt` timestamp column

### Type Mapping

The value converter chain:
```
Model Layer: ReadOnlyMemory<float> (database-agnostic, zero-allocation)
    ↓ (EF Core Value Converter)
Provider Layer: SqlVector<float> (SQL Server native type)
    ↓ (EF Core Type Mapping)
Database: vector(1536) (SQL Server 2025 native column type)
```

This architecture ensures:
- Database portability (same model layer for PostgreSQL, etc.)
- Memory efficiency (ReadOnlyMemory for stack-allocated embeddings)
- Native SQL Server performance (SqlVector<float> for vector operations)

## Project Structure

```
src/DeepWiki.Data.SqlServer/
├── Configuration/
│   └── DocumentEntityConfiguration.cs      # EF Core entity fluent configuration
├── DbContexts/
│   ├── SqlServerVectorDbContext.cs          # Main DbContext with retry policy
│   └── SqlServerVectorDbContextFactory.cs   # IDesignTimeDbContextFactory for EF CLI
├── Health/
│   └── SqlServerHealthCheck.cs              # Health check with version validation
├── Repositories/
│   ├── SqlServerDocumentRepository.cs       # IDocumentRepository implementation
│   └── SqlServerVectorStore.cs              # IVectorStore implementation
├── Seeds/
│   └── InitialSeedData.cs                   # Test/development seed data
├── Seeding/
│   └── DatabaseSeedExtensions.cs            # Seeding utilities
└── Migrations/
    ├── 20260117212713_InitialCreate.cs      # EF-generated migration
    └── SqlServerVectorDbContextModelSnapshot.cs # EF migration metadata
```

## Setup Instructions

### Prerequisites

- SQL Server 2025 or later
- .NET 10 or later
- EF Core 10.0.2 or later
- Testcontainers (for integration testing)

### Local Development

#### Option 1: Using Docker (Recommended)

```bash
# Pull SQL Server 2025 image
docker pull mcr.microsoft.com/mssql/server:2025-latest

# Run container
docker run -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD="Strong@Password123" \
  -p 1433:1433 \
  -d mcr.microsoft.com/mssql/server:2025-latest

# Wait for container to be ready
docker exec <container_id> /opt/mssql-tools18/bin/sqlcmd -C \
  -S localhost -U sa -P "Strong@Password123" -Q "SELECT 1"
```

#### Option 2: Using Testcontainers (Automatic)

Integration tests use Testcontainers which automatically manage SQL Server containers:

```csharp
// From SqlServerFixture.cs
var container = new MsSqlBuilder()
    .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_SA_PASSWORD", "Strong@Password123")
    .Build();

await container.StartAsync();
```

### Database Initialization

#### 1. Create Database

```bash
sqlcmd -C -S localhost -U sa -P "Strong@Password123" \
  -Q "CREATE DATABASE DeepWiki"
```

#### 2. Apply Migrations

```bash
cd src/DeepWiki.Data.SqlServer

# Add migration (if needed - already generated)
dotnet ef migrations add InitialCreate

# Apply migration to database
dotnet ef database update
```

#### 3. Verify Setup

```bash
sqlcmd -C -S localhost -U sa -P "Strong@Password123" \
  -d DeepWiki \
  -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'"
```

Expected output:
```
Documents
```

### Seed Data

Initial seed data is available in `InitialSeedData.cs` with 3 sample documents:
- Database design patterns
- Query optimization implementation
- Distributed systems architecture

To apply seed data:

```csharp
var context = new SqlServerVectorDbContext(options);
await context.SeedInitialDataAsync();
```

## API Usage

### Document Storage

```csharp
// Create document with embedding
var doc = new DocumentEntity
{
    Id = Guid.NewGuid(),
    RepoUrl = "https://github.com/org/repo",
    FilePath = "src/Program.cs",
    Title = "Program Entry Point",
    Text = "Main entry point for the application...",
    Embedding = new ReadOnlyMemory<float>(embedding),  // 1536-dimensional
    FileType = "cs",
    IsCode = true,
    TokenCount = 250
};

// Save document
var repository = new SqlServerDocumentRepository(context);
await repository.AddAsync(doc);
```

### Vector Similarity Search

```csharp
var vectorStore = new SqlServerVectorStore(context);

// Find 5 most similar documents
var queryEmbedding = new ReadOnlyMemory<float>(queryVector);  // 1536-dimensional
var results = await vectorStore.QueryNearestAsync(
    queryEmbedding,
    k: 5,
    repoUrl: "https://github.com/org/repo"
);

foreach (var result in results)
{
    Console.WriteLine($"{result.Title} (distance: {result.Distance:F3})");
}
```

### Bulk Operations

```csharp
var documents = new[] {
    new DocumentEntity { /* ... */ },
    new DocumentEntity { /* ... */ }
};

// Insert multiple documents in transaction
context.Documents.AddRange(documents);
await context.SaveChangesAsync();

// Delete by repository
var toDelete = context.Documents
    .Where(d => d.RepoUrl == "https://github.com/org/repo");
context.Documents.RemoveRange(toDelete);
await context.SaveChangesAsync();
```

## Performance Characteristics

### Query Performance

Based on testing with Testcontainers:

| Operation | Dataset Size | Typical Time | Notes |
|-----------|--------------|--------------|-------|
| Vector search (k=5) | 100 docs | <50ms | No index (functional query) |
| Fetch by ID | Any | <10ms | Primary key lookup |
| List by repository | 1000 docs | <100ms | IX_Documents_RepoUrl index |
| Bulk insert | 100 docs | <200ms | Single transaction |

### Optimization Tips

1. **Indexing**: Standard B-tree indexes on frequently filtered columns (`RepoUrl`, `CreatedAt`)
2. **Batching**: Insert documents in batches of 100-1000 for bulk loads
3. **Partitioning**: Consider partitioning by `RepoUrl` for multi-tenancy
4. **Vector Search**: Use `VECTOR_DISTANCE()` function for approximate similarity (no index required)

## Integration Testing

Run all SQL Server integration tests:

```bash
dotnet test tests/DeepWiki.Data.SqlServer.Tests/ -v normal
```

Test coverage:

- **Document Repository** (IDocumentRepository): 10 tests
  - CRUD operations (Add, Update, Delete, Get)
  - Pagination (GetByRepoAsync)
  - Concurrency handling

- **Vector Store** (IVectorStore): 11 tests
  - Vector similarity search (QueryNearestAsync)
  - Distance calculation (cosine)
  - Bulk operations (UpsertAsync, DeleteByRepoAsync)

- **Bulk Operations**: 5 tests
  - 100-document batch insertion
  - Duplicate ID handling
  - Concurrent updates
  - Embedding preservation

- **Total**: 26 integration tests, all using Testcontainers for SQL Server 2025

## Troubleshooting

### Connection Issues

**Error**: "Connection timeout"

**Solution**: Ensure SQL Server container is running and healthy:
```bash
docker ps
docker logs <container_id>
```

### Migration Errors

**Error**: "Unable to create DbContext at design time"

**Solution**: Ensure `SqlServerVectorDbContextFactory` is implemented and uses a valid connection string.

### Type Mismatch Errors

**Error**: "Cannot convert SqlVector<float>? to ReadOnlyMemory<float>?"

**Solution**: Verify value converter is correctly configured in `DocumentEntityConfiguration.cs`:
```csharp
var converter = new ValueConverter<ReadOnlyMemory<float>?, SqlVector<float>?>(
    v => v.HasValue ? new SqlVector<float>(v.Value) : null,
    v => v.HasValue ? v.Value.Memory : null);
builder.Property(d => d.Embedding).HasConversion(converter);
```

## Advanced Configuration

### Connection String

Development (Testcontainers/Docker):
```
Server=localhost;Database=DeepWiki;User Id=sa;Password=Strong@Password123;Encrypt=false;TrustServerCertificate=true;
```

Production (with encryption):
```
Server=prod-sql.database.windows.net;Database=DeepWiki;User Id=admin@prod;Password=***;Encrypt=true;Connection Timeout=30;
```

### Retry Policy

Configure in `SqlServerVectorDbContext.OnConfiguring()`:
```csharp
optionsBuilder.UseSqlServer(c => c
    .EnableRetryOnFailure(
        maxRetryCount: 3,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorNumbersToAdd: null));
```

### Health Checks

Register health check in DI container:
```csharp
services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server");
```

## Related Documentation

- [Multi-Database Data Layer Spec](../spec.md)
- [Implementation Plan](../plan.md)
- [Architecture Constitution](../../ARCHITECTURE_CONSTITUTION.md)

## References

- [SQL Server 2025 Vector Data Type](https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type)
- [EF Core SQL Server Vector Support](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/vector-search)
- [Vector Distance Function](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-distance-transact-sql)
