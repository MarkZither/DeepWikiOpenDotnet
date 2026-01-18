# Dependency Injection Configuration

This document describes how to register and use the DeepWiki data access layer in your application.

## Quick Start

### ASP.NET Core

In `Program.cs`:

```csharp
// For SQL Server - always use configuration
builder.Services.AddSqlServerDataLayer(
    "ConnectionStrings:DefaultConnection", 
    builder.Configuration
);

// Or for PostgreSQL
builder.Services.AddPostgresDataLayer(
    "ConnectionStrings:PostgresConnection", 
    builder.Configuration
);
```

### Console Application

```csharp
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSqlServerDataLayer(
    "ConnectionStrings:DefaultConnection", 
    config
);

var serviceProvider = services.BuildServiceProvider();

var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
var repository = serviceProvider.GetRequiredService<IDocumentRepository>();

// Use services...
```

## Registration Methods

### SQL Server Registration

#### From Configuration (Recommended)

```csharp
services.AddSqlServerDataLayer(
    connectionStringKey: "ConnectionStrings:DefaultConnection",
    configuration: configuration
);
```

#### Direct Connection String (Not Recommended)

```csharp
// Only use with values from environment variables or secure configuration
var connectionString = Environment.GetEnvironmentVariable("DEEPWIKI_CONNECTION_STRING") 
    ?? throw new InvalidOperationException("Connection string not configured");

services.AddSqlServerDataLayer(
    connectionString: connectionString,
    configureOptions: null  // Optional DbContextOptions configuration
);
```

#### Configuration-Based

```csharp
services.AddSqlServerDataLayer(
    connectionStringKey: "ConnectionStrings:DefaultConnection",
    configuration: configuration
);
```

#### Custom DbContext Options

```csharp
services.AddSqlServerDataLayer(
    connectionString,
    configureOptions: options => 
    {
        // Custom configuration
        options.CommandTimeout(30);
    }
);
```

### PostgreSQL Registration

#### From Configuration (Recommended)

```csharp
services.AddPostgresDataLayer(
    connectionStringKey: "ConnectionStrings:PostgresConnection",
    configuration: configuration
);
```

#### Direct Connection String (Not Recommended)

```csharp
// Only use with values from environment variables or secure configuration
var connectionString = Environment.GetEnvironmentVariable("DEEPWIKI_POSTGRES_CONNECTION") 
    ?? throw new InvalidOperationException("Connection string not configured");

services.AddPostgresDataLayer(
    connectionString: connectionString
);
```

#### Configuration-Based

```csharp
services.AddPostgresDataLayer(
    connectionStringKey: "ConnectionStrings:PostgresConnection",
    configuration: configuration
);
```

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "",
    "PostgresConnection": ""
  },
  "DatabaseType": "SqlServer"
}
```

**Never commit connection strings to source control.** Use User Secrets for local development and environment variables/Key Vault for production.

### User Secrets (Development)

```bash
# Initialize User Secrets for the project
dotnet user-secrets init

# Set SQL Server connection string
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword;Encrypt=false;"

# Set PostgreSQL connection string
dotnet user-secrets set "ConnectionStrings:PostgresConnection" "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=password;"
```

### Environment Variables (Production)

```bash
# For ASP.NET Core, uses DOTNET_ prefix
export DOTNET_ConnectionStrings__DefaultConnection="<connection_string_from_key_vault>"
export DOTNET_ConnectionStrings__PostgresConnection="<connection_string_from_key_vault>"
```

Connection strings should come from Azure Key Vault, AWS Secrets Manager, or similar secure service in production.

## Registered Services

When you call `AddSqlServerDataLayer` or `AddPostgresDataLayer`, the following services are registered:

### Scoped Services

```csharp
services.AddScoped<IVectorStore, SqlServerVectorStore>();        // or PostgresVectorStore
services.AddScoped<IDocumentRepository, SqlServerDocumentRepository>(); // or PostgresDocumentRepository
```

### DbContext

```csharp
services.AddDbContext<SqlServerVectorDbContext>(options => ...);   // or PostgresVectorDbContext
```

## Automatic Retry Policy

Both registrations include automatic retry on transient failures:

- **Max retries**: 3 attempts
- **Backoff**: Exponential (default EF Core configuration)
- **Transient errors**: Network timeouts, deadlocks, etc.

## Database Switching

To switch between SQL Server and PostgreSQL at runtime:

```csharp
public static void ConfigureDataLayer(
    IServiceCollection services,
    IConfiguration config)
{
    var databaseType = config["DatabaseType"];
    var connectionString = config["ConnectionStrings:DefaultConnection"];
    
    if (databaseType == "SqlServer")
    {
        services.AddSqlServerDataLayer(connectionString);
    }
    else if (databaseType == "Postgres")
    {
        services.AddPostgresDataLayer(connectionString);
    }
    else
    {
        throw new InvalidOperationException($"Unknown database: {databaseType}");
    }
}

// In Program.cs
var builder = WebApplicationBuilder.CreateBuilder(args);
ConfigureDataLayer(builder.Services, builder.Configuration);
```

Or use a factory pattern:

```csharp
public interface IDataLayerFactory
{
    void Configure(IServiceCollection services, string connectionString);
}

public class SqlServerDataLayerFactory : IDataLayerFactory
{
    public void Configure(IServiceCollection services, string connectionString)
    {
        services.AddSqlServerDataLayer(connectionString);
    }
}

public class PostgresDataLayerFactory : IDataLayerFactory
{
    public void Configure(IServiceCollection services, string connectionString)
    {
        services.AddPostgresDataLayer(connectionString);
    }
}

// In Program.cs
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("Connection string not configured");

var factory = databaseType switch
{
    "SqlServer" => new SqlServerDataLayerFactory(),
    "Postgres" => new PostgresDataLayerFactory(),
    _ => throw new InvalidOperationException($"Unknown database: {databaseType}")
};

factory.Configure(builder.Services, connectionString);
```

## Usage Examples

### In an API Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _repository;

    public DocumentsController(
        IVectorStore vectorStore,
        IDocumentRepository repository)
    {
        _vectorStore = vectorStore;
        _repository = repository;
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<DocumentEntity>>> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        var results = await _vectorStore.QueryNearestAsync(
            request.Embedding,
            k: request.K ?? 10,
            cancellationToken: cancellationToken
        );
        return Ok(results);
    }

    [HttpPost("bulk")]
    public async Task<ActionResult> BulkAdd(
        [FromBody] List<DocumentEntity> documents,
        CancellationToken cancellationToken)
    {
        await _vectorStore.BulkUpsertAsync(documents, cancellationToken);
        return Ok(new { count = documents.Count, message = "Documents added successfully" });
    }
}
```

### In a Background Service

```csharp
public class DocumentIngestionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        IServiceProvider serviceProvider,
        ILogger<DocumentIngestionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();

                // Process documents...
                var documents = await FetchDocumentsFromQueue();
                if (documents.Any())
                {
                    await vectorStore.BulkUpsertAsync(documents, stoppingToken);
                    _logger.LogInformation("Ingested {DocumentCount} documents", documents.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during document ingestion");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

## Migrations

After registering the data layer, apply database migrations to create the schema:

```csharp
// Using DbContext directly
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<SqlServerVectorDbContext>();
await context.Database.MigrateAsync();
```

Or in a startup task:

```csharp
public class MigrationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public MigrationHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Register in Program.cs
builder.Services.AddHostedService<MigrationHostedService>();
```

## Troubleshooting

### Connection String Errors

**Problem**: "Connection string 'X' not found in configuration"

**Solution**: Verify the configuration key matches:
```csharp
// In appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."  // Key path: ConnectionStrings:DefaultConnection
  }
}
```

### Missing DbContext

**Problem**: "No service for type 'SqlServerVectorDbContext' has been registered"

**Solution**: Ensure you called `AddSqlServerDataLayer` or `AddPostgresDataLayer`:
```csharp
services.AddSqlServerDataLayer(connectionString);
```

### pgvector Extension Not Found

**Problem**: "Npgsql.PostgresException: type 'vector' does not exist"

**Solution**: The pgvector extension must be installed in PostgreSQL. Use the pgvector/pgvector:pg17 Docker image which includes it pre-installed.

## See Also

- [Bulk Operations Guide](bulk-operations.md)
- [Example Code](../examples/DIRegistrationExample.cs)
- [Data Model](data-model.md)
