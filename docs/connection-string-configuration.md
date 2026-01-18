# Connection String Configuration Guide

Comprehensive guide for configuring database connections in all environments.

## Quick Reference

### SQL Server 2025

**Development** (local, TrustServerCertificate, no encryption):
```
Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Encrypt=false
```

**Staging/Production** (encrypted, certificate validation):
```
Server=prod-server;Database=deepwiki;User Id=deepwiki_user;Password=***;Encrypt=true;TrustServerCertificate=false
```

### PostgreSQL 17+

**Development** (local):
```
Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres
```

**Staging/Production**:
```
Host=prod-postgres;Port=5432;Database=deepwiki;Username=deepwiki_user;Password=***;SSL Mode=Require
```

---

## Development Environment

### Local SQL Server (Docker)

```bash
# Start SQL Server container
docker run --name deepwiki-sql \
  -e SA_PASSWORD=YourPassword123! \
  -p 1433:1433 \
  -d mcr.microsoft.com/mssql/server:2025-latest
```

**appsettings.Development.json**:
```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Encrypt=false"
  }
}
```

### Local PostgreSQL (Docker)

```bash
# Start PostgreSQL container with pgvector
docker run --name deepwiki-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  -d pgvector/pgvector:pg17
```

**appsettings.Development.json**:
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres"
  }
}
```

### Using User Secrets (Recommended)

```bash
# Initialize user secrets (one time)
dotnet user-secrets init -p src/deepwiki-open-dotnet.ApiService

# Set connection string (development)
dotnet user-secrets set ConnectionStrings:SqlServer \
  "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Encrypt=false"

# Verify
dotnet user-secrets list
```

**Benefits**:
- ✅ Never commit secrets to git
- ✅ Per-machine configuration
- ✅ Team members use own databases
- ✅ Easy to change without code edits

---

## SQL Server Connection String Components

### Required Components

| Parameter | Value | Example |
|-----------|-------|---------|
| `Server` | Server hostname/IP and port | `localhost,1433` or `prod.database.windows.net` |
| `Database` | Database name | `deepwiki` |
| `User Id` | Login name | `sa` or `deepwiki_user` |
| `Password` | Login password | (secure password) |

### Optional Security Components

| Parameter | Dev | Staging | Prod | Purpose |
|-----------|-----|---------|------|---------|
| `Encrypt` | `false` | `true` | `true` | Encrypt connection traffic |
| `TrustServerCertificate` | `true` | `false` | `false` | Validate server certificate |
| `Timeout` | 15s | 30s | 30s | Connection timeout |

### Optional Performance Components

| Parameter | Purpose | Typical Value |
|-----------|---------|---------------|
| `Max Pool Size` | Connection pool limit | 100 (default) |
| `Min Pool Size` | Minimum connections kept | 5 |
| `Pooling` | Enable connection pooling | `true` (default) |
| `MultipleActiveResultSets` | MARS for parallel queries | `false` (not needed for EF Core) |

### Full Example with All Options

```
Server=localhost,1433;
Database=deepwiki;
User Id=sa;
Password=YourPassword123!;
Encrypt=false;
TrustServerCertificate=true;
Timeout=30;
Max Pool Size=100;
Min Pool Size=5;
Pooling=true;
Application Name=DeepWiki
```

### Format Variations

**Using DNS name (Azure SQL Database)**:
```
Server=myserver.database.windows.net,1433;Database=deepwiki;User Id=admin@myserver;Password=***
```

**Using Windows authentication (on-premises)**:
```
Server=localhost;Database=deepwiki;Integrated Security=true;Encrypt=false
```

**Using connection string builder (C#)**:
```csharp
var builder = new SqlConnectionStringBuilder
{
    DataSource = "localhost,1433",
    InitialCatalog = "deepwiki",
    UserID = "sa",
    Password = "YourPassword123!",
    Encrypt = SqlConnectionEncryptOption.Optional,
    TrustServerCertificate = true,
    ConnectTimeout = 30
};

var connectionString = builder.ConnectionString;
```

---

## PostgreSQL Connection String Components

### Required Components

| Parameter | Value | Example |
|-----------|-------|---------|
| `Host` | Server hostname/IP | `localhost` or `prod-postgres.azure.com` |
| `Port` | Database port | `5432` (default) |
| `Database` | Database name | `deepwiki` |
| `Username` | Login name | `postgres` or `deepwiki_user` |
| `Password` | Login password | (secure password) |

### Optional Security Components

| Parameter | Dev | Staging | Prod | Purpose |
|-----------|-----|---------|------|---------|
| `SSL Mode` | `Disable` | `Require` | `Require` | SSL/TLS requirement |
| `Trust Server Certificate` | `false` | `false` | `false` | (Use SSL Mode instead) |
| `Root Certificate` | Not set | Path | Path | CA certificate path |

### Optional Performance Components

| Parameter | Purpose | Typical Value |
|-----------|---------|---------------|
| `Max Pool Size` | Connection pool limit | 100 (default) |
| `Min Pool Size` | Minimum connections kept | 1 |
| `Connection Idle Lifetime` | Idle connection timeout | 300 seconds |
| `Connection Lifetime` | Max connection age | 600 seconds |

### Full Example with All Options

```
Host=localhost;
Port=5432;
Database=deepwiki;
Username=postgres;
Password=postgres;
SSL Mode=Disable;
Max Pool Size=100;
Min Pool Size=1;
Application Name=DeepWiki
```

### SSL/TLS Configuration

**Development (no encryption)**:
```
Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres;SSL Mode=Disable
```

**Staging (require SSL)**:
```
Host=prod-postgres.azure.com;Port=5432;Database=deepwiki;Username=postgres@server;Password=***;SSL Mode=Require
```

**Production (require SSL + custom CA)**:
```
Host=prod-postgres.azure.com;Port=5432;Database=deepwiki;Username=postgres@server;Password=***;SSL Mode=Require;Root Certificate=/etc/ssl/certs/custom-ca.crt
```

### Format Variations

**Using connection string builder (C#)**:
```csharp
var builder = new NpgsqlConnectionStringBuilder
{
    Host = "localhost",
    Port = 5432,
    Database = "deepwiki",
    Username = "postgres",
    Password = "postgres",
    SslMode = SslMode.Disable
};

var connectionString = builder.ConnectionString;
```

**For Azure Database for PostgreSQL**:
```
Host=myserver.postgres.database.azure.com;
Port=5432;
Database=deepwiki;
Username=admin@myserver;
Password=***;
SSL Mode=Require
```

---

## Environment-Specific Configuration

### appsettings.json (Shared Settings)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### appsettings.Development.json (Dev Overrides)

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Encrypt=false"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### appsettings.Staging.json (Staging Overrides)

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=staging-sql.azure.com;Database=deepwiki_staging;User Id=deepwiki_user;Password=***;Encrypt=true;TrustServerCertificate=false"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### appsettings.Production.json (Prod Overrides)

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=prod-sql.azure.com;Database=deepwiki;User Id=deepwiki_user;Password=***;Encrypt=true;TrustServerCertificate=false"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

---

## Environment Variables

### Setting via Environment Variables

**Shell**:
```bash
# SQL Server
export ConnectionStrings__SqlServer="Server=localhost,1433;Database=deepwiki;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true"

# PostgreSQL
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres"

dotnet run
```

**Docker**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10

ENV ConnectionStrings__SqlServer="Server=sql-server;Database=deepwiki;User Id=sa;Password=YourPassword123!;Encrypt=true;TrustServerCertificate=false"

COPY . /app
WORKDIR /app

ENTRYPOINT ["dotnet", "deepwiki-open-dotnet.ApiService.dll"]
```

**Docker Compose**:
```yaml
version: '3.8'
services:
  api:
    build: .
    environment:
      - ConnectionStrings__SqlServer=Server=sqlserver;Database=deepwiki;User Id=sa;Password=YourPassword123!;Encrypt=false;TrustServerCertificate=true
    depends_on:
      - sqlserver
  
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2025-latest
    environment:
      - SA_PASSWORD=YourPassword123!
    ports:
      - "1433:1433"
```

---

## Azure Key Vault Integration (Production)

### Setup

```csharp
// Program.cs
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Load from Key Vault
var keyVaultUrl = new Uri(builder.Configuration["KeyVault:VaultUri"]);
var secretClient = new SecretClient(keyVaultUrl, new DefaultAzureCredential());

var sqlServerSecret = await secretClient.GetSecretAsync("ConnectionStrings--SqlServer");
builder.Configuration["ConnectionStrings:SqlServer"] = sqlServerSecret.Value.Value;

// Or use configuration provider
builder.ConfigurationBuilder.AddAzureKeyVault(
    keyVaultUrl,
    new DefaultAzureCredential());

var app = builder.Build();
app.Run();
```

### Benefits

- ✅ Secrets never in code or config files
- ✅ Centralized secret management
- ✅ Audit trail of access
- ✅ Automatic rotation support
- ✅ Managed Identity authentication (no credentials needed)

---

## Kubernetes Secrets

### Create Secret from Connection String

```bash
kubectl create secret generic deepwiki-db-secrets \
  --from-literal=SqlServer='Server=sql-server;Database=deepwiki;...' \
  --from-literal=Postgres='Host=postgres;Port=5432;...'
```

### Mount in Deployment

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: deepwiki-api
spec:
  containers:
  - name: deepwiki
    image: deepwiki:v1.0.0
    env:
    - name: ConnectionStrings__SqlServer
      valueFrom:
        secretKeyRef:
          name: deepwiki-db-secrets
          key: SqlServer
    - name: ConnectionStrings__Postgres
      valueFrom:
        secretKeyRef:
          name: deepwiki-db-secrets
          key: Postgres
```

---

## Connection Pool Configuration

### Default Settings

**SQL Server (Npgsql driver defaults)**:
- Min Pool Size: 5
- Max Pool Size: 100
- Pool Timeout: 15 seconds
- Connection Lifetime: unlimited

### Tuning for High Load

```
Server=localhost,1433;
Database=deepwiki;
User Id=sa;
Password=***;
Max Pool Size=200;
Min Pool Size=20;
Timeout=60;
Pooling=true
```

### Monitoring Pool Usage

```csharp
// Log pool statistics
var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

var poolStats = SqlConnection.ClearAllPools(); // Returns void, but forces clear
Console.WriteLine("Connection pool cleared");

// Use connection...
```

---

## Troubleshooting Connection Issues

### Issue: "Cannot Open Database"

**Cause**: Database doesn't exist

**Solution**:
```sql
CREATE DATABASE deepwiki;
GO
```

### Issue: "Login Failed"

**Cause**: Wrong username/password

**Solution**:
1. Verify credentials are correct
2. Test with SQL Server Management Studio
3. Check user permissions

### Issue: "Connection Timeout"

**Cause**: Database unreachable

**Solution**:
```bash
# Test connectivity
telnet localhost 1433
# OR
psql -h localhost -U postgres
```

### Issue: "Encrypt=true" But Certificate Error

**For Development ONLY**:
```
Encrypt=false;TrustServerCertificate=true
```

**For Production**:
```
Encrypt=true;TrustServerCertificate=false;
# AND ensure certificate is valid
```

---

## Best Practices

1. **Never Commit Secrets**: Use appsettings.Production.json only in CI/CD
2. **Use User Secrets (Dev)**: Keep local passwords out of git
3. **Use Azure Key Vault (Prod)**: Centralized secret management
4. **Encrypt Connections (Prod)**: Always set Encrypt=true
5. **Validate Certificates (Prod)**: Set TrustServerCertificate=false
6. **Pool Connections**: Keep Pooling=true for performance
7. **Set Reasonable Timeouts**: 30+ seconds for production
8. **Test Failover**: Verify connection string works in all environments

---

**Last Updated**: January 18, 2026  
**Version**: 1.0.0
