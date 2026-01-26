# Quickstart: Multi-Database Data Access Layer

**Version**: 1.0.0  
**Date**: 2026-01-16  
**Audience**: Developers setting up the data layer for the first time

---

## Prerequisites

- **.NET 10 SDK**: [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker Desktop** or **Docker Engine**: Required for integration tests
- **IDE**: Visual Studio 2022, VS Code with C# extension, or JetBrains Rider
- **Database** (choose one):
  - SQL Server 2025 (for SQL Server provider)
  - PostgreSQL 17+ with pgvector extension (for PostgreSQL provider)

---

## Project Structure Overview

```
src/
├── DeepWiki.Data/                    # Base abstractions (start here)
├── DeepWiki.Data.SqlServer/          # SQL Server implementation
└── DeepWiki.Data.Postgres/           # PostgreSQL implementation

tests/
├── DeepWiki.Data.Tests/              # Unit tests
├── DeepWiki.Data.SqlServer.Tests/    # SQL Server integration tests
└── DeepWiki.Data.Postgres.Tests/     # PostgreSQL integration tests
```

---

## Step 1: Clone and Build

```bash
# Clone repository
git clone https://github.com/org/deepwiki-open-dotnet.git
cd deepwiki-open-dotnet

# Checkout feature branch
git checkout 001-multi-db-data-layer

# Restore dependencies
dotnet restore src/DeepWiki.Data/DeepWiki.Data.csproj
dotnet restore src/DeepWiki.Data.SqlServer/DeepWiki.Data.SqlServer.csproj
dotnet restore src/DeepWiki.Data.Postgres/DeepWiki.Data.Postgres.csproj

# Build all projects
dotnet build src/DeepWiki.Data/
dotnet build src/DeepWiki.Data.SqlServer/
dotnet build src/DeepWiki.Data.Postgres/
```

---

## Step 2: Configure Connection Strings

### Development (User Secrets)

**SQL Server**:
```bash
cd src/DeepWiki.Data.SqlServer
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:VectorDatabase" "<your-sql-server-connection-string>"
# Example format: Server=localhost,1433;Database=DeepWikiVector;User Id=sa;TrustServerCertificate=True
```

**PostgreSQL**:
```bash
cd src/DeepWiki.Data.Postgres
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:VectorDatabase" "<your-postgres-connection-string>"
# Example format: Host=localhost;Port=5432;Database=deepwiki_vector;Username=postgres
```

### Production (Environment Variables)

```bash
# Set connection string via environment variable
export ConnectionStrings__VectorDatabase="<production-connection-string>"

# Recommended: Use Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault
# See deployment docs for secure configuration patterns
```

---

## Step 3: Set Up Local Database

### Option A: SQL Server (Docker)

```bash
# Pull SQL Server 2025 image (use 2022 if 2025 not available)
docker pull mcr.microsoft.com/mssql/server:2022-latest

# Run container
docker run -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 \
  --name sqlserver-vector \
  -d mcr.microsoft.com/mssql/server:2022-latest

# Verify running
docker ps | grep sqlserver-vector
```

**Apply Migrations**:
```bash
cd src/DeepWiki.Data.SqlServer
dotnet ef database update

# Verify tables created
docker exec -it sqlserver-vector /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "YourStrong!Passw0rd" \
  -Q "SELECT name FROM sys.tables WHERE name='Documents'"
```

---

### Option B: PostgreSQL with pgvector (Docker)

```bash
# Pull pgvector image
docker pull pgvector/pgvector:pg17

# Run container
docker run --name postgres-vector \
  -e POSTGRES_PASSWORD=password \
  -p 5432:5432 \
  -d pgvector/pgvector:pg17

# Verify pgvector extension
docker exec -it postgres-vector psql -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

**Apply Migrations**:
```bash
cd src/DeepWiki.Data.Postgres
dotnet ef database update

# Verify tables created
docker exec -it postgres-vector psql -U postgres -d deepwiki_vector \
  -c "\dt documents"
```

---

## Step 4: Run Tests

### Unit Tests (No Database Required)

```bash
dotnet test tests/DeepWiki.Data.Tests/
```

### Integration Tests (Requires Docker)

**SQL Server**:
```bash
# Testcontainers will automatically start SQL Server container
dotnet test tests/DeepWiki.Data.SqlServer.Tests/

# View test results
dotnet test tests/DeepWiki.Data.SqlServer.Tests/ --logger "console;verbosity=detailed"
```

**PostgreSQL**:
```bash
# Testcontainers will automatically start PostgreSQL container
dotnet test tests/DeepWiki.Data.Postgres.Tests/

# Run specific test
dotnet test tests/DeepWiki.Data.Postgres.Tests/ \
  --filter FullyQualifiedName~PostgresVectorStoreTests.QueryNearestAsync_ReturnsOrderedResults
```

**Verify Test Coverage**:
```bash
dotnet test --collect:"XPlat Code Coverage"
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report"
open coverage-report/index.html  # macOS
# Or: xdg-open coverage-report/index.html  # Linux
```

---

## Step 5: Basic Usage

### Example: Add and Query Documents

```csharp
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using Microsoft.EntityFrameworkCore;

// Configure DbContext
var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
    .UseSqlServer("your-connection-string")
    .Options;

var dbContext = new SqlServerVectorDbContext(options);
IVectorStore vectorStore = new SqlServerVectorStore(dbContext);
IDocumentRepository repository = new SqlServerDocumentRepository(dbContext);

// Add document
var document = new DocumentEntity
{
    Id = Guid.NewGuid(),
    RepoUrl = "https://github.com/example/repo",
    FilePath = "src/Program.cs",
    Title = "Program.cs",
    Text = "using System; public class Program { static void Main() { } }",
    Embedding = GenerateEmbedding("using System; public class Program..."), // 1536 floats
    IsCode = true,
    TokenCount = 25
};

await vectorStore.UpsertAsync(document);

// Query similar documents
float[] queryEmbedding = GenerateEmbedding("C# main method entry point");
var results = await vectorStore.QueryNearestAsync(queryEmbedding, k: 5);

foreach (var result in results)
{
    Console.WriteLine($"{result.Title}: {result.FilePath}");
}

// Helper (placeholder for actual embedding generation)
float[] GenerateEmbedding(string text)
{
    // In real implementation, call OpenAI/Ollama API
    var embedding = new float[1536];
    new Random().NextBytes(MemoryMarshal.AsBytes(embedding.AsSpan()));
    return embedding;
}
```

---

## Step 6: Dependency Injection Setup

### ASP.NET Core Program.cs

```csharp
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Choose provider based on configuration
var provider = builder.Configuration["Database:Provider"]; // "SqlServer" or "Postgres"

if (provider == "SqlServer")
{
    builder.Services.AddDbContext<SqlServerVectorDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("VectorDatabase"),
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null)));

    builder.Services.AddScoped<IPersistenceVectorStore, SqlServerVectorStore>();
    builder.Services.AddScoped<IDocumentRepository, SqlServerDocumentRepository>();
}
else if (provider == "Postgres")
{
    builder.Services.AddDbContext<PostgresVectorDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("VectorDatabase"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3)));

    builder.Services.AddScoped<IPersistenceVectorStore, PostgresVectorStore>();
    builder.Services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
}

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SqlServerVectorDbContext>("database");

var app = builder.Build();

app.MapHealthChecks("/health");
app.Run();
```

### appsettings.json

```json
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "VectorDatabase": "<set-via-user-secrets-or-environment-variable>"
  }
}
```

---

## Troubleshooting

### Issue: "Table 'Documents' does not exist"

**Solution**: Run migrations
```bash
cd src/DeepWiki.Data.SqlServer  # or Postgres
dotnet ef database update
```

---

### Issue: "pgvector extension not found"

**Solution**: Enable extension manually
```bash
docker exec -it postgres-vector psql -U postgres -d deepwiki_vector \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

---

### Issue: "Embedding must be exactly 1536 dimensions"

**Solution**: Validate embedding before upsert
```csharp
if (embedding.Length != 1536)
{
    throw new ArgumentException($"Invalid embedding length: {embedding.Length}");
}
```

---

### Issue: Testcontainers fails to start

**Solution**: Check Docker is running
```bash
docker ps  # Should list running containers
docker info  # Should show Docker system info
```

If Docker not running, start Docker Desktop or `systemctl start docker`

---

### Issue: Connection timeout during tests

**Solution**: Increase Testcontainers startup timeout
```csharp
var container = new ContainerBuilder()
    .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilPortIsAvailable(5432)
        .WithTimeout(TimeSpan.FromMinutes(2)))
    .Build();
```

---

## Next Steps

1. **Phase 1.1**: Implement `DeepWiki.Data` base project with entity and interfaces
2. **Phase 1.2**: Implement SQL Server provider with vector type support
3. **Phase 1.3**: Implement PostgreSQL provider with pgvector extension
4. **Phase 1.4**: Implement shared repository logic and bulk operations
5. **Phase 1.5**: Complete documentation, benchmarks, and deployment guide

See [plan.md](plan.md) for detailed implementation phases.

---

## Additional Resources

- [EF Core Migrations Documentation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [SQL Server Vector Type Documentation](https://learn.microsoft.com/en-us/sql/relational-databases/vectors/)
- [PostgreSQL pgvector Extension](https://github.com/pgvector/pgvector)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)

---

## Support

- **GitHub Issues**: [https://github.com/org/deepwiki-open-dotnet/issues](https://github.com/org/deepwiki-open-dotnet/issues)
- **Discussions**: [https://github.com/org/deepwiki-open-dotnet/discussions](https://github.com/org/deepwiki-open-dotnet/discussions)
- **Documentation**: [docs/](../../docs/)
