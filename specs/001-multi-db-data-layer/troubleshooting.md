# Troubleshooting Guide

Common issues and solutions for the DeepWiki .NET Data Access Layer.

## Table of Contents

1. [Connection String Issues](#connection-string-issues)
2. [Database Setup Problems](#database-setup-problems)
3. [Migration & Schema Errors](#migration--schema-errors)
4. [Query & Performance Issues](#query--performance-issues)
5. [Concurrency Conflicts](#concurrency-conflicts)
6. [Index & Vector Issues](#index--vector-issues)
7. [Testing & Docker Issues](#testing--docker-issues)
8. [Dependency Injection Issues](#dependency-injection-issues)
9. [Health Check Failures](#health-check-failures)
10. [Entity Framework Issues](#entity-framework-issues)

---

## Connection String Issues

### Issue 1: "Connection Refused" or "No Connection Could Be Made"

**Symptoms:**
- `System.Data.SqlClient.SqlException: A network-related or instance-specific error occurred`
- Connection timeout errors when starting application
- Tests cannot connect to database

**Solutions:**

1. **Verify database is running:**
   ```bash
   # SQL Server
   docker ps | grep sql
   # PostgreSQL
   docker ps | grep postgres
   ```

2. **Test connection directly:**
   ```bash
   # SQL Server (requires mssql-cli)
   mssql-cli -S localhost,1433 -U sa -P YourPassword
   
   # PostgreSQL (requires psql)
   psql -h localhost -p 5432 -U postgres -d deepwiki
   ```

3. **Check connection string format:**
   ```json
   // SQL Server - correct format
   "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true"
   
   // PostgreSQL - correct format
   "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres"
   ```

4. **Check firewall/network:**
   - Ensure port 1433 (SQL Server) or 5432 (PostgreSQL) is not blocked
   - If using Docker, verify containers are on same network

### Issue 2: "Login Failed" or "Authentication Failed"

**Symptoms:**
- `Login failed for user 'sa'`
- `FATAL: Ident authentication failed`
- `password authentication failed for user`

**Solutions:**

1. **Verify credentials:**
   ```bash
   # Check environment variable
   echo $ConnectionStrings__SqlServer
   
   # Or in appsettings.json
   cat appsettings.json | grep -A2 ConnectionStrings
   ```

2. **Reset SQL Server password (Docker):**
   ```bash
   docker stop deepwiki-sql
   docker rm deepwiki-sql
   docker run --name deepwiki-sql -e SA_PASSWORD=NewPassword123! \
     -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest
   ```

3. **PostgreSQL user permissions:**
   ```bash
   docker exec deepwiki-postgres psql -U postgres -c "ALTER USER postgres WITH PASSWORD 'newpassword';"
   ```

### Issue 3: "TrustServerCertificate" Issues

**Symptoms:**
- `The certificate chain was issued by an authority that is not trusted`
- SSL/TLS handshake failures

**Solutions:**

1. **For development (not production):**
   ```json
   {
     "ConnectionStrings": {
       "SqlServer": "...;TrustServerCertificate=true;Encrypt=false"
     }
   }
   ```

2. **For production (required):**
   - Use proper SSL certificates
   - Set `Encrypt=true` and `TrustServerCertificate=false`
   - Install certificate in system trust store

---

## Database Setup Problems

### Issue 4: "Database Does Not Exist"

**Symptoms:**
- `Cannot open database "deepwiki" requested by the login`
- `database "deepwiki" does not exist`

**Solutions:**

1. **Create database manually:**
   ```sql
   -- SQL Server
   CREATE DATABASE deepwiki;
   GO
   
   -- PostgreSQL
   CREATE DATABASE deepwiki;
   ```

2. **Or use EF Core migrations:**
   ```bash
   cd /home/mark/docker/deepwiki-open-dotnet
   dotnet ef database update -p src/DeepWiki.Data.SqlServer
   # OR
   dotnet ef database update -p src/DeepWiki.Data.Postgres
   ```

### Issue 5: "Vector Type Not Supported" or "pgvector Extension Not Found"

**Symptoms:**
- `Column type 'vector' is not supported`
- `type "vector" does not exist`
- `pgvector extension is not installed`

**Solutions:**

1. **SQL Server 2025+ required:**
   ```bash
   # Check SQL Server version
   docker inspect deepwiki-sql | grep -i version
   
   # Must be SQL Server 2025 or later
   # Recreate if needed:
   docker run --name deepwiki-sql -e SA_PASSWORD=YourPassword123! \
     -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest
   ```

2. **PostgreSQL pgvector installation:**
   ```bash
   # pgvector/pgvector:pg17 image has it pre-installed
   # Verify:
   docker exec deepwiki-postgres psql -U postgres -d deepwiki \
     -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT * FROM pg_extension WHERE extname='vector';"
   ```

### Issue 6: "Invalid Column Type" After Upgrade

**Symptoms:**
- Migration fails with type mapping errors
- `SqlVector<T>` conversion errors
- Vector column operations fail

**Solutions:**

1. **Verify EF Core version:**
   ```bash
   dotnet add package "Microsoft.EntityFrameworkCore" --version 10.0.0
   dotnet add package "Microsoft.EntityFrameworkCore.SqlServer" --version 10.0.0
   dotnet add package "Npgsql.EntityFrameworkCore.PostgreSQL" --version 10.0.0
   ```

2. **Regenerate migrations:**
   ```bash
   dotnet ef migrations add FixVectorTypeMapping -p src/DeepWiki.Data.SqlServer
   dotnet ef database update -p src/DeepWiki.Data.SqlServer
   ```

---

## Migration & Schema Errors

### Issue 7: "Pending Migrations"

**Symptoms:**
- `The model backing the 'SqlServerVectorDbContext' context has changed since the database was last created`
- Migrations not applied
- Schema mismatch

**Solutions:**

1. **Apply pending migrations:**
   ```bash
   dotnet ef database update -p src/DeepWiki.Data.SqlServer
   # OR
   dotnet ef database update -p src/DeepWiki.Data.Postgres
   ```

2. **View pending migrations:**
   ```bash
   dotnet ef migrations list -p src/DeepWiki.Data.SqlServer
   ```

3. **If migrations are corrupted:**
   ```bash
   # Remove last migration
   dotnet ef migrations remove -p src/DeepWiki.Data.SqlServer
   
   # Recreate
   dotnet ef migrations add FixMigration -p src/DeepWiki.Data.SqlServer
   ```

### Issue 8: "Migration Script Contains Raw SQL Errors"

**Symptoms:**
- Migration .sql file has syntax errors
- `Incorrect syntax` errors in SQL operations
- Database state mismatch

**Solutions:**

1. **Review generated migration:**
   ```bash
   cat src/DeepWiki.Data.SqlServer/Migrations/*_*.cs | grep -A5 "protected override"
   ```

2. **Manual SQL fix (SQL Server):**
   ```sql
   -- Navigate to migration file manually if needed
   -- src/DeepWiki.Data.SqlServer/Migrations/[timestamp]_*.cs
   -- Review Up() method SQL
   ```

3. **Rollback and regenerate:**
   ```bash
   # Go to previous migration
   dotnet ef database update [previous-migration-name] -p src/DeepWiki.Data.SqlServer
   
   # Remove problematic migration
   dotnet ef migrations remove -p src/DeepWiki.Data.SqlServer
   
   # Regenerate
   dotnet ef migrations add FixedMigration -p src/DeepWiki.Data.SqlServer
   ```

---

## Query & Performance Issues

### Issue 9: "Vector Query Timeout" or "Very Slow Queries"

**Symptoms:**
- `Command timeout expired` when querying vectors
- Vector similarity searches take >2 seconds
- CPU usage spikes during queries

**Solutions:**

1. **Increase command timeout:**
   ```csharp
   // In DbContext configuration
   optionsBuilder
     .UseSqlServer(connectionString, options => 
       options.CommandTimeout(300)); // 5 minutes
   ```

2. **Verify HNSW index is created:**
   ```sql
   -- SQL Server: Check index exists
   SELECT name FROM sys.indexes WHERE name LIKE '%vector%' AND object_id = OBJECT_ID('dbo.DocumentEntities');
   
   -- PostgreSQL: Check index exists
   SELECT indexname FROM pg_indexes WHERE indexname LIKE '%embedding%';
   ```

3. **Rebuild index if corrupted:**
   ```sql
   -- SQL Server
   ALTER INDEX ALL ON dbo.DocumentEntities REBUILD;
   
   -- PostgreSQL (drop and recreate)
   DROP INDEX IF EXISTS idx_documententities_embedding_hnsw;
   CREATE INDEX idx_documententities_embedding_hnsw ON document_entities USING hnsw (embedding vector_cosine_ops) WITH (m=16, ef_construction=200);
   ```

4. **Check query execution plan:**
   ```sql
   -- SQL Server
   SET STATISTICS IO ON;
   SELECT TOP 10 * FROM DocumentEntities 
   ORDER BY embedding.VectorDistance([embedding_vector]) ASC;
   SET STATISTICS IO OFF;
   ```

### Issue 10: "Memory Usage Spikes During Bulk Operations"

**Symptoms:**
- Out of memory exceptions during bulk insert
- Application crashes when upserting >1000 documents
- High memory consumption visible in `docker stats`

**Solutions:**

1. **Reduce batch size:**
   ```csharp
   // Existing code may use batches of 1000
   // Reduce to:
   const int batchSize = 100; // or even 50 for very large vectors
   
   foreach (var batch in documents.Chunk(batchSize))
   {
       await _vectorStore.BulkUpsertAsync(batch);
   }
   ```

2. **Process in streaming mode:**
   ```csharp
   // Instead of loading all vectors into memory:
   var allDocuments = await GetAllDocuments(); // ❌ Memory spike
   
   // Stream in batches:
   foreach (var batch in GetDocumentsInBatches(batchSize: 100))
   {
       await _vectorStore.BulkUpsertAsync(batch);
   }
   ```

3. **Increase container memory:**
   ```bash
   docker update --memory 2g deepwiki-sql
   docker update --memory 2g deepwiki-postgres
   ```

---

## Concurrency Conflicts

### Issue 11: "DbUpdateConcurrencyException" During Updates

**Symptoms:**
- `The store was updated after the entity was loaded`
- Update fails with concurrency token mismatch
- `UpdatedAt` timestamp conflict

**Solutions:**

1. **Handle concurrency gracefully:**
   ```csharp
   try
   {
       await _repository.UpdateAsync(document);
   }
   catch (DbUpdateConcurrencyException ex)
   {
       // Refresh from database
       await ex.Entries[0].ReloadAsync();
       
       // Re-apply changes or notify user
       return new ConflictResult("Document was modified by another process");
   }
   ```

2. **Reload before updating (optimistic):**
   ```csharp
   var document = await _repository.GetByIdAsync(id);
   if (document != null)
   {
       // Another process may have modified it
       // Reload to get latest UpdatedAt
       await context.Entry(document).ReloadAsync();
       
       // Now update safely
       document.Text = newText;
       await _repository.UpdateAsync(document);
   }
   ```

3. **Disable concurrency checking (not recommended):**
   ```csharp
   // In entity configuration - NOT RECOMMENDED
   builder.Property(e => e.UpdatedAt)
     .IsConcurrencyToken(false); // ❌ Removes concurrency protection
   ```

---

## Index & Vector Issues

### Issue 12: "HNSW Index Parameters Suboptimal"

**Symptoms:**
- Slow vector search despite small dataset
- Index not created with correct parameters
- Vector operations fall back to sequential scan

**Solutions:**

1. **Verify index parameters (SQL Server):**
   ```sql
   -- Check current index on vector column
   EXEC sp_helpindex 'dbo.DocumentEntities';
   ```

2. **Recreate with correct parameters:**
   ```sql
   -- SQL Server: Drop and recreate
   DROP INDEX IF EXISTS idx_documententities_embedding ON dbo.DocumentEntities;
   CREATE INDEX idx_documententities_embedding ON dbo.DocumentEntities (embedding)
     USING VECTOR_DISTANCE(COSINE) WITH (m = 16, ef_construction = 200);
   ```

   ```sql
   -- PostgreSQL: Drop and recreate
   DROP INDEX IF EXISTS idx_documententities_embedding;
   CREATE INDEX idx_documententities_embedding ON document_entities 
     USING hnsw (embedding vector_cosine_ops) WITH (m=16, ef_construction=200);
   ```

3. **Tune parameters for your scale:**
   - **m=16**: Good for general use (default)
   - **m=32**: Better for larger datasets (>100K documents)
   - **ef_construction=200**: Good for general use (default)
   - **ef_construction=400**: For higher accuracy (slower builds)

---

## Testing & Docker Issues

### Issue 13: "Testcontainers Cannot Connect to Docker"

**Symptoms:**
- `Docker.DotNet.DockerApiException: Docker API responded with status code InternalServerError`
- `System.IO.IOException: Cannot connect to the Docker daemon`
- Integration tests fail immediately

**Solutions:**

1. **Verify Docker is running:**
   ```bash
   docker ps
   # Should return container list, not error
   ```

2. **Check Docker daemon socket permissions (Linux):**
   ```bash
   # Add user to docker group
   sudo usermod -aG docker $USER
   newgrp docker
   
   # Verify
   docker ps
   ```

3. **Set Docker socket path (if custom):**
   ```csharp
   // In test fixture
   var settings = DockerClientConfiguration.FactoryOptions.EngineFactory(new Uri("unix:///var/run/docker.sock"));
   ```

4. **Use Testcontainers configuration file:**
   ```bash
   # ~/.testcontainers.properties
   testcontainers.docker.socket.override=/var/run/docker.sock
   ```

### Issue 14: "Port Already in Use" in Integration Tests

**Symptoms:**
- `Address already in use` when starting test container
- Port 1433 or 5432 already bound
- Multiple test runs fail sequentially

**Solutions:**

1. **Kill existing containers:**
   ```bash
   docker stop $(docker ps -q --filter "name=deepwiki")
   docker rm $(docker ps -aq --filter "name=deepwiki")
   ```

2. **Use random ports in tests:**
   ```csharp
   // Testcontainers uses random ports by default
   // Verify no fixed port config in test fixture
   ```

3. **Clean up test databases:**
   ```bash
   docker system prune -a
   ```

### Issue 15: "Testcontainers Image Pull Timeout"

**Symptoms:**
- `Failed to pull image` on first test run
- Network timeout downloading image
- First test run very slow

**Solutions:**

1. **Pre-pull images:**
   ```bash
   docker pull mcr.microsoft.com/mssql/server:2025-latest
   docker pull pgvector/pgvector:pg17
   ```

2. **Increase pull timeout:**
   ```bash
   # In ~/.testcontainers.properties
   testcontainers.image.pull.timeout=600s
   ```

3. **Use local image cache:**
   - Keep images downloaded to avoid re-pulling
   - Images are cached after first pull

---

## Dependency Injection Issues

### Issue 16: "Cannot Resolve Service" or "No Service Registered"

**Symptoms:**
- `Unable to resolve service for type 'DeepWiki.Data.IVectorStore'`
- DI container doesn't find registered services
- `InvalidOperationException` on service resolution

**Solutions:**

1. **Verify DI registration:**
   ```csharp
   // In Program.cs
   builder.Services.AddSqlServerVectorStore(
       builder.Configuration.GetConnectionString("SqlServer"));
   
   // Verify before calling Build()
   ```

2. **Check service lifetime:**
   ```csharp
   // Correct lifetimes
   services.AddScoped<IDocumentRepository>(); // Per-request
   services.AddScoped<IPersistenceVectorStore>();       // Per-request
   
   // NOT Singleton for DbContext-dependent services
   services.AddSingleton<IDocumentRepository>(); // ❌ Wrong
   ```

3. **Verify dependency chain:**
   ```csharp
   // If IVectorStore depends on DbContext
   // Ensure DbContext is registered first
   services.AddDbContext<SqlServerVectorDbContext>();
   services.AddScoped<IPersistenceVectorStore, SqlServerVectorStore>();
   ```

### Issue 17: "DbContext Already Disposed"

**Symptoms:**
- `ObjectDisposedException: Cannot access a disposed object`
- Operations fail after request completes
- Issue in background tasks or long-running operations

**Solutions:**

1. **Inject DbContext correctly:**
   ```csharp
   // ✅ Correct: Injected per-request
   public class DocumentService
   {
       private readonly IDocumentRepository _repository; // Lives for request
       
       public DocumentService(IDocumentRepository repository)
       {
           _repository = repository;
       }
   }
   ```

2. **For background tasks, create scope:**
   ```csharp
   // In Hosted Service
   public class BackgroundTaskService : BackgroundService
   {
       private readonly IServiceProvider _serviceProvider;
       
       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           using var scope = _serviceProvider.CreateScope();
           var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
           // Now safe to use
       }
   }
   ```

---

## Health Check Failures

### Issue 18: "Health Check Endpoint Returns Unhealthy"

**Symptoms:**
- `/health` endpoint returns `Unhealthy` status
- `"description": "Failed to connect to database"`
- Health check timeout

**Solutions:**

1. **Check database connectivity:**
   ```bash
   # Verify database is running
   docker ps | grep sql
   docker logs deepwiki-sql | tail -20
   ```

2. **Verify health check registration:**
   ```csharp
   builder.Services.AddHealthChecks()
       .AddCheck<SqlServerHealthCheck>("sqlserver-vector");
   
   app.MapHealthChecks("/health");
   ```

3. **Check health check implementation:**
   ```csharp
   public class SqlServerHealthCheck : IHealthCheck
   {
       public async Task<HealthCheckResult> CheckHealthAsync(
           HealthCheckContext context, 
           CancellationToken cancellationToken = default)
       {
           try
           {
               await _context.Database.ExecuteScalarAsync(
                   "SELECT 1", cancellationToken: cancellationToken);
               return HealthCheckResult.Healthy("SQL Server is available");
           }
           catch (Exception ex)
           {
               return HealthCheckResult.Unhealthy("SQL Server is unavailable", ex);
           }
       }
   }
   ```

### Issue 19: "Version Check Fails in Health Check"

**Symptoms:**
- Health check fails with `Version not supported`
- `SQL Server version is below 2025`
- PostgreSQL version check fails

**Solutions:**

1. **Verify database version:**
   ```sql
   -- SQL Server
   SELECT @@VERSION;
   
   -- PostgreSQL
   SELECT version();
   ```

2. **Check health check version logic:**
   ```csharp
   // Must be SQL Server 2025+ (version >= 2025)
   var version = int.Parse(versionString.Split('(')[1].Split(')')[0]);
   if (version < 2025)
       return HealthCheckResult.Unhealthy($"SQL Server {version} not supported");
   ```

3. **Update database if needed:**
   ```bash
   # Stop old container
   docker stop deepwiki-sql
   docker rm deepwiki-sql
   
   # Run SQL Server 2025
   docker run --name deepwiki-sql -e SA_PASSWORD=YourPassword123! \
     -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest
   ```

---

## Entity Framework Issues

### Issue 20: "No DbSet<DocumentEntity> Found"

**Symptoms:**
- `Entity type 'DocumentEntity' is not part of the model for this context`
- DbSet is null or not configured
- Entity not mapped

**Solutions:**

1. **Verify DbSet exists:**
   ```csharp
   public class SqlServerVectorDbContext : DbContext
   {
       public DbSet<DocumentEntity> DocumentEntities { get; set; } = null!;
       
       // Configuration
       protected override void OnModelCreating(ModelBuilder modelBuilder)
       {
           modelBuilder.ApplyConfiguration(new DocumentEntityConfiguration());
       }
   }
   ```

2. **Check entity configuration:**
   ```csharp
   public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
   {
       public void Configure(EntityTypeBuilder<DocumentEntity> builder)
       {
           builder.HasKey(e => e.Id);
           builder.Property(e => e.RepoUrl).IsRequired();
           // ... rest of config
       }
   }
   ```

3. **Verify fluent API vs annotations:**
   - Use ONE approach: either fluent API or data annotations
   - Don't mix both for same entity

---

## Getting Help

If you encounter an issue not listed here:

1. **Check logs:**
   ```bash
   docker logs deepwiki-sql
   docker logs deepwiki-postgres
   ```

2. **Review test output:**
   ```bash
   dotnet test --logger "console;verbosity=detailed"
   ```

3. **Enable EF Core logging:**
   ```csharp
   optionsBuilder.LogTo(Console.WriteLine, LogLevel.Debug);
   ```

4. **Search issue tracker:**
   - [GitHub Issues](https://github.com/deepwiki/deepwiki-open-dotnet/issues)

5. **Provide context for support:**
   - Exact error message
   - Steps to reproduce
   - Database version and OS
   - Application logs
