# Configuration Guide: Enabling Real Vector Store

⚠️ **IMPORTANT**: The API requires a configured vector store (Postgres or SQL Server). Startup will fail if `VectorStore:Provider` is unset or the configured provider is unavailable. Configure a real vector store before starting the API.

## Current Status

Without a configured vector store the service will not start — you must configure `VectorStore:Provider` and the corresponding connection string (see examples below).

## Option 1: SQL Server 2025 (Recommended for Production)

### Prerequisites
- SQL Server 2025 with native vector support
- Database created

### Configuration (appsettings.Development.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "VectorStore": {
    "Provider": "sqlserver",
    "SqlServer": {
      "ConnectionString": "Server=localhost;Database=DeepWiki;User Id=sa;Password=YourPassword;TrustServerCertificate=True;",
      "HnswM": 16,
      "HnswEfConstruction": 200
    }
  },
  "Embedding": {
    "Provider": "openai",
    "OpenAI": {
      "ApiKey": "sk-your-openai-api-key",
      "Model": "text-embedding-3-small"
    }
  }
}
```

### Database Setup

```sql
-- Create database
CREATE DATABASE DeepWiki;
GO

USE DeepWiki;
GO

-- Run migrations (from project root)
-- dotnet ef database update --project src/DeepWiki.Data.SqlServer
```

## Option 2: PostgreSQL with pgvector (Recommended for Development)

### Prerequisites
- PostgreSQL 17+ with pgvector extension
- Database created with extension enabled

### Configuration (appsettings.Development.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "VectorStore": {
    "Provider": "postgres",
    "Postgres": {
      "ConnectionString": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres;",
      "HnswM": 16,
      "HnswEfConstruction": 200
    }
  },
  "Embedding": {
    "Provider": "openai",
    "OpenAI": {
      "ApiKey": "sk-your-openai-api-key",
      "Model": "text-embedding-3-small"
    }
  }
}
```

### Database Setup

```sql
-- Create database and extension
CREATE DATABASE deepwiki;

\c deepwiki

CREATE EXTENSION IF NOT EXISTS vector;

-- Run migrations (from project root)
-- dotnet ef database update --project src/DeepWiki.Data.Postgres
```

## Option 3: Local Ollama (No API Key Required)

## Option 3: Local Databases with Podman / Docker Compose (recommended)

For reliable development and migrations you should run persistent DB containers for Postgres (pgvector) and SQL Server. This repository includes `docker-compose.yml` (persistent) and `docker-compose.test.yml` (ephemeral test DBs).

Quick setup (recommended):

1. Copy `.env.example` to `.env` and fill in secure passwords (do NOT commit `.env`).

2. Start the debug (persistent) databases:

   - podman-compose:
     ```bash
     cp .env.example .env
     # edit .env and set strong passwords
     podman-compose -f docker-compose.yml up -d
     ```

   - docker compose:
     ```bash
     docker compose -f docker-compose.yml up -d
     ```

3. Use the helper script to wait for readiness and run EF migrations (applies Postgres & SQL Server migrations):

   ```bash
   chmod +x scripts/up-db.sh
   ./scripts/up-db.sh
   ```

   The script sets `DEEPWIKI_POSTGRES_CONNECTION` and `DEEPWIKI_SQLSERVER_CONNECTION` environment variables for design-time EF and applies migrations using the API startup project.

4. Verify:

   - Health: `curl http://localhost:5484/health`
   - Postgres data: `docker exec -it <pg-container> psql -U postgres -d deepwikidb -c "SELECT count(*) FROM documents;"`

Notes:
- For ephemeral test runs, use `docker-compose.test.yml` which does not mount persistent volumes and maps ports to non-default ports to avoid collisions.
- Store production/CI secrets in a secure store (Azure Key Vault, GitHub Secrets, etc.). For local dev, prefer dotnet user-secrets or .env files that are not committed.
- If you use Podman: `podman-compose` is recommended for `docker-compose` compatibility. The compose files are intentionally simple and should work with either runtime.

Security reminder: Do not commit `.env` with passwords. Use `dotnet user-secrets` for per-developer secrets when running the API locally (see below).

---

### Setting design-time secrets for EF (local)

Inside the API project folder:

```bash
cd src/deepwiki-open-dotnet.ApiService
# initialize user-secrets once per developer
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=deepwikidb;Username=postgres;Password=YOUR_PG_PASS"
# or set env var for design-time EF tooling
export DEEPWIKI_POSTGRES_CONNECTION="Host=localhost;Port=5432;Database=deepwikidb;Username=postgres;Password=YOUR_PG_PASS"
```

When running CI, set equivalent variables in the pipeline secrets.

### Prerequisites
- Ollama installed and running locally
- Model with embeddings support (e.g., `nomic-embed-text`)

### Configuration (appsettings.Development.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "VectorStore": {
    "Provider": "postgres",  // or "sqlserver"
    "Postgres": {
      "ConnectionString": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=postgres;"
    }
  },
  "Embedding": {
    "Provider": "ollama",
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "Model": "nomic-embed-text"
    }
  }
}
```

### Ollama Setup

```bash
# Install Ollama (if not installed)
curl -fsSL https://ollama.com/install.sh | sh

# Pull embedding model
ollama pull nomic-embed-text

# Verify it's running
ollama list
```

## Testing After Configuration

1. **Start the API**:
   ```bash
   cd src/deepwiki-open-dotnet.AppHost
   dotnet run
   ```

2. **Open the HTTP file**: `test-ingest-query.http`

3. **Test ingestion** (Request #1):
   - Should return 200 OK with `successCount: 1`
   - Check logs for "Ingestion completed" message

4. **Test query** (Request #4):
   - Should return results matching your ingested documents
   - `similarityScore` should be between 0.0 and 1.0

## Verification

After configuration, check the logs when the API starts:

✅ **Good** (real vector store):
```
info: Using SQL Server vector store with connection: Server=localhost;Database=DeepWiki...
info: Using OpenAI embedding service with model: text-embedding-3-small
```

❌ **Bad** (NoOp):
```
warn: Vector store provider 'sqlserver' not configured or not available. Using NoOpVectorStore.
warn: Embedding provider 'openai' not configured or not available. Using NoOpEmbeddingService.
```

## Environment Variables Alternative

Instead of appsettings.json, you can use environment variables:

```bash
export VectorStore__Provider="postgres"
export VectorStore__Postgres__ConnectionString="Host=localhost;..."
export Embedding__Provider="openai"
export Embedding__OpenAI__ApiKey="sk-..."
```

## Quick Start with Docker Compose (PostgreSQL)

The easiest way to get started:

```bash
# From project root
docker-compose up -d postgres

# Wait for PostgreSQL to start, then run migrations
dotnet ef database update --project src/DeepWiki.Data.Postgres

# Update appsettings.Development.json with postgres config above
# Then start the app
cd src/deepwiki-open-dotnet.AppHost
dotnet run
```
