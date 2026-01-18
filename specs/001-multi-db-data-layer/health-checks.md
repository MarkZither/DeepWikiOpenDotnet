# Health Check Implementation Guide

Integrated health check endpoints for monitoring database connectivity, version compatibility, and performance.

## Overview

Both SQL Server and PostgreSQL implementations provide health check endpoints that validate:

- ✅ Database connectivity
- ✅ Database version compatibility
- ✅ Vector extension availability (PostgreSQL)
- ✅ Query performance baseline
- ✅ Schema readiness

## Quick Start

### ASP.NET Core Integration

```csharp
// Program.cs
using DeepWiki.Data.SqlServer;
using DeepWiki.Data.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver-vector")
    // OR
    .AddCheck<PostgresHealthCheck>("postgres-vector");

// Add database services
builder.Services.AddSqlServerVectorStore(
    builder.Configuration.GetConnectionString("SqlServer"));

var app = builder.Build();

// Map health check endpoint
app.MapHealthChecks("/health");
app.Run();
```

### Accessing Health Checks

```bash
# Check status
curl http://localhost:5000/health

# Detailed JSON response
curl http://localhost:5000/health -H "Accept: application/json"
```

### Response Format

**Healthy Response**:
```json
{
  "status": "Healthy",
  "checks": {
    "sqlserver-vector": {
      "status": "Healthy",
      "description": "SQL Server 2025 is available",
      "data": {
        "version": "2025",
        "responseTime": "42ms"
      }
    }
  },
  "totalDuration": "00:00:00.0420000"
}
```

**Unhealthy Response**:
```json
{
  "status": "Unhealthy",
  "checks": {
    "sqlserver-vector": {
      "status": "Unhealthy",
      "description": "SQL Server is unavailable",
      "exception": "Microsoft.Data.SqlClient.SqlException: Connection timeout"
    }
  },
  "totalDuration": "00:00:05.0120000"
}
```

---

## SQL Server Health Check

### Implementation Details

The `SqlServerHealthCheck` validates:

```csharp
public class SqlServerHealthCheck : IHealthCheck
{
    private readonly SqlServerVectorDbContext _context;

    public SqlServerHealthCheck(SqlServerVectorDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Test connectivity
            var serverVersion = await _context.Database.ExecuteScalarAsync<string>(
                "SELECT @@VERSION", cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(serverVersion))
                return HealthCheckResult.Unhealthy("Unable to retrieve SQL Server version");

            // 2. Verify version (must be 2025+)
            var versionNumber = int.Parse(serverVersion.Split('(')[1].Split(')')[0]);
            if (versionNumber < 2025)
                return HealthCheckResult.Unhealthy(
                    $"SQL Server {versionNumber} not supported. Require 2025+");

            // 3. Verify DocumentEntities table exists
            var tableExists = await _context.Database.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                  WHERE TABLE_NAME = 'DocumentEntities'",
                cancellationToken: cancellationToken);

            if (tableExists == 0)
                return HealthCheckResult.Unhealthy("DocumentEntities table not found");

            // 4. Verify vector index exists
            var indexExists = await _context.Database.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM sys.indexes 
                  WHERE object_id = OBJECT_ID('dbo.DocumentEntities') 
                  AND type_desc = 'NONCLUSTERED'",
                cancellationToken: cancellationToken);

            if (indexExists == 0)
                return HealthCheckResult.Degraded("Vector index not found");

            return HealthCheckResult.Healthy($"SQL Server {versionNumber} is available");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server is unavailable", ex);
        }
    }
}
```

### What It Checks

| Check | Purpose | Impact if Failed |
|-------|---------|-----------------|
| **Connectivity** | Can connect to database | Application cannot run |
| **Version** | SQL Server 2025+ | Vector type not available |
| **Table Exists** | DocumentEntities created | Schema not initialized |
| **Index Exists** | HNSW vector index created | Queries will be slow |

### Response Time

- **Healthy**: Typically 10-50ms
- **Degraded**: 50-200ms (slow query)
- **Unhealthy**: 5000ms+ (timeout waiting for connection)

### Configuration

```csharp
// In Startup.cs - Optional detailed health checks
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>(
        "sqlserver-vector",
        failureStatus: HealthStatus.Unhealthy,
        timeout: TimeSpan.FromSeconds(10))  // 10 second timeout
    .AddSqlServer(
        builder.Configuration.GetConnectionString("SqlServer"),
        name: "sqlserver-connection");
```

---

## PostgreSQL Health Check

### Implementation Details

The `PostgresHealthCheck` validates:

```csharp
public class PostgresHealthCheck : IHealthCheck
{
    private readonly PostgresVectorDbContext _context;

    public PostgresHealthCheck(PostgresVectorDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Test connectivity
            var version = await _context.Database.ExecuteScalarAsync<string>(
                "SELECT version()", cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(version))
                return HealthCheckResult.Unhealthy("Unable to retrieve PostgreSQL version");

            // 2. Verify version (must be 17+)
            var versionNumber = int.Parse(version.Split(' ')[1].Split('.')[0]);
            if (versionNumber < 17)
                return HealthCheckResult.Unhealthy(
                    $"PostgreSQL {versionNumber} not supported. Require 17+");

            // 3. Verify pgvector extension exists
            var vectorExists = await _context.Database.ExecuteScalarAsync<bool>(
                @"SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')",
                cancellationToken: cancellationToken);

            if (!vectorExists)
                return HealthCheckResult.Unhealthy("pgvector extension not installed");

            // 4. Verify document_entities table exists
            var tableExists = await _context.Database.ExecuteScalarAsync<bool>(
                @"SELECT EXISTS(SELECT 1 FROM information_schema.tables 
                  WHERE table_name = 'document_entities')",
                cancellationToken: cancellationToken);

            if (!tableExists)
                return HealthCheckResult.Unhealthy("document_entities table not found");

            // 5. Verify vector index exists
            var indexExists = await _context.Database.ExecuteScalarAsync<bool>(
                @"SELECT EXISTS(SELECT 1 FROM pg_indexes 
                  WHERE indexname LIKE '%embedding%' 
                  AND tablename = 'document_entities')",
                cancellationToken: cancellationToken);

            if (!indexExists)
                return HealthCheckResult.Degraded("Vector index not found");

            return HealthCheckResult.Healthy(
                $"PostgreSQL {versionNumber} with pgvector is available");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable", ex);
        }
    }
}
```

### What It Checks

| Check | Purpose | Impact if Failed |
|-------|---------|-----------------|
| **Connectivity** | Can connect to database | Application cannot run |
| **Version** | PostgreSQL 17+ | May lack features |
| **pgvector** | Extension installed | Vector operations fail |
| **Table Exists** | document_entities created | Schema not initialized |
| **Index Exists** | HNSW vector index created | Queries will be slow |

### Response Time

- **Healthy**: Typically 15-70ms
- **Degraded**: 70-250ms (slow query)
- **Unhealthy**: 5000ms+ (timeout)

### Configuration

```csharp
// In Startup.cs - Optional detailed health checks
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>(
        "postgres-vector",
        failureStatus: HealthStatus.Unhealthy,
        timeout: TimeSpan.FromSeconds(10))
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres"),
        name: "postgres-connection");
```

---

## Advanced Configuration

### Custom Health Check Endpoint

```csharp
// Map with custom status code
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthCheckResponse,
    AllowCachingResponses = false,
    Predicate = check => check.Tags.Contains("ready")
});

// Custom response writer
private static async Task WriteHealthCheckResponse(
    HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    context.Response.StatusCode = report.Status == HealthStatus.Healthy ? 200 : 503;

    var response = new
    {
        status = report.Status.ToString(),
        timestamp = DateTime.UtcNow,
        checks = report.Entries.ToDictionary(
            e => e.Key,
            e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
    };

    await context.Response.WriteAsJsonAsync(response);
}
```

### Detailed Health Check Tags

```csharp
// Add specific tags for Kubernetes/orchestration
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>(
        "sqlserver-vector",
        tags: new[] { "database", "ready", "live" })
    .AddCheck<SqlServerHealthCheck>(
        "sqlserver-vector-performance",
        tags: new[] { "database", "performance" });

// Map specific endpoints
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

---

## Kubernetes Integration

### Liveness and Readiness Probes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: deepwiki-api
spec:
  template:
    spec:
      containers:
      - name: deepwiki
        image: deepwiki:v1.0.0
        
        # Readiness probe - app ready to accept traffic
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
        
        # Liveness probe - app still running
        livenessProbe:
          httpGet:
            path: /health/live
            port: 5000
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
```

### Docker Healthcheck

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10

COPY --from=builder /app/bin/Release/net10.0/publish /app

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "deepwiki-open-dotnet.ApiService.dll"]
```

---

## Monitoring & Alerting

### Application Insights Integration

```csharp
// In Program.cs
builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver-vector")
    .AddApplicationInsightsPublisher();
```

### Prometheus Metrics

```csharp
// Using App.Metrics (third-party)
builder.Services.AddMetrics()
    .AddReporting(factory =>
    {
        factory.AddConsole();
    });

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver-vector")
    .ForwardToPrometheus();
```

### Custom Logging

```csharp
public class LoggingHealthCheck : IHealthCheck
{
    private readonly ILogger<LoggingHealthCheck> _logger;
    private readonly SqlServerVectorDbContext _context;

    public LoggingHealthCheck(ILogger<LoggingHealthCheck> logger, 
        SqlServerVectorDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // ... health check logic ...
            sw.Stop();
            _logger.LogInformation(
                "Health check passed. Duration: {Duration}ms",
                sw.ElapsedMilliseconds);
            
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Health check failed. Duration: {Duration}ms",
                sw.ElapsedMilliseconds);
            
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}
```

---

## Troubleshooting

### Issue: Health Check Always Returns Unhealthy

**Cause**: Database unreachable or credentials invalid

**Solution**:
1. Verify database is running
2. Check connection string in configuration
3. Verify database user has proper permissions
4. Check firewall/network access

### Issue: Health Check Timeout

**Cause**: Database slow or unresponsive

**Solution**:
1. Increase timeout in health check configuration
2. Check database performance (CPU, memory, I/O)
3. Verify vector index is created
4. Review slow query logs

### Issue: Version Check Fails

**Cause**: Database version below minimum requirement

**Solution**:
1. Check current database version
2. Upgrade database to required version (SQL Server 2025+, PostgreSQL 17+)
3. Verify pgvector extension installed (PostgreSQL)

---

## Best Practices

1. **Separate Probes**: Use different endpoints for readiness vs liveness
2. **Reasonable Timeouts**: Set 5-10 second timeouts to avoid cascading failures
3. **Log Failures**: Always log health check failures for debugging
4. **Monitor Response Time**: Track health check duration to detect DB issues early
5. **Don't Overload**: Health checks run frequently; keep queries simple
6. **Version Validation**: Always verify database version in startup

---

**Last Updated**: January 18, 2026  
**Version**: 1.0.0
