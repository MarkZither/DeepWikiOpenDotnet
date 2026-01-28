# Configuration Guide: Enabling Real Vector Store

⚠️ **IMPORTANT**: By default, the API uses `NoOpVectorStore` which doesn't persist data.
To actually store and search documents, you need to configure a real vector store.

## Current Status

Without configuration:
- ✅ Ingestion endpoint accepts requests (returns 200 OK)
- ❌ Documents are NOT persisted (NoOpVectorStore discards them)
- ❌ Query endpoint returns empty results

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
