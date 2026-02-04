# Quickstart: RAG Query API

**Feature**: 003-rag-query-api  
**Date**: 2026-01-26

---

## Prerequisites

- .NET 10 SDK
- Docker (for Testcontainers and local databases)
- One of:
  - SQL Server 2025 (native vector support)
  - PostgreSQL 17+ with pgvector extension

---

## Configuration

### 1. Vector Store Provider

Add to `appsettings.json`:

```json
{
  "VectorStore": {
    "Provider": "sqlserver",
    "SqlServer": {
      "ConnectionString": "Server=localhost;Database=DeepWiki;User Id=sa;Password=Your_password123;TrustServerCertificate=true",
      "HnswM": 16,
      "HnswEfConstruction": 200
    }
  }
}
```

Or for PostgreSQL:

```json
{
  "VectorStore": {
    "Provider": "postgres",
    "Postgres": {
      "ConnectionString": "Host=localhost;Port=5432;Database=deepwiki;Username=postgres;Password=password",
      "HnswM": 16,
      "HnswEfConstruction": 200
    }
  }
}
```

### 2. Embedding Service

Add embedding provider configuration:

```json
{
  "Embedding": {
    "Provider": "ollama",
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "Model": "nomic-embed-text"
    }
  }
}
```

Or for OpenAI:

```json
{
  "Embedding": {
    "Provider": "openai",
    "OpenAI": {
      "ApiKey": "sk-...",
      "Model": "text-embedding-3-small"
    }
  }
}
```

### 3. Environment Variables (Production)

Override via environment variables:
```bash
export VectorStore__Provider=postgres
export VectorStore__Postgres__ConnectionString="Host=..."
export Embedding__Provider=openai
export Embedding__OpenAI__ApiKey="sk-..."
```

---

## Running the API

### Option 1: Aspire (Recommended)

```bash
cd src/deepwiki-open-dotnet.AppHost
dotnet run
```

Access:
- API: `http://localhost:5000`
- Aspire Dashboard: `http://localhost:18888`
- OpenAPI: `http://localhost:5000/openapi/v1.json`

### Option 2: Standalone

```bash
cd src/deepwiki-open-dotnet.ApiService
dotnet run
```

---

## API Usage

### 1. Semantic Search

**Request:**
```bash
curl -X POST http://localhost:5000/api/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "How do I implement authentication?",
    "k": 5,
    "includeFullText": true
  }'
```

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "repoUrl": "https://github.com/example/repo",
    "filePath": "docs/authentication.md",
    "title": "Authentication Guide",
    "text": "# Authentication\n\nThis guide covers...",
    "similarityScore": 0.92,
    "metadata": null
  }
]
```

### 2. Ingest Documents

**Request:**
```bash
curl -X POST http://localhost:5000/api/documents/ingest \
  -H "Content-Type: application/json" \
  -d '{
    "documents": [
      {
        "repoUrl": "https://github.com/example/repo",
        "filePath": "docs/README.md",
        "title": "Project README",
        "text": "# My Project\n\nThis is the documentation..."
      }
    ],
    "continueOnError": true
  }'
```

**Response:**
```json
{
  "successCount": 1,
  "failureCount": 0,
  "totalChunks": 1,
  "durationMs": 523,
  "ingestedDocumentIds": ["550e8400-e29b-41d4-a716-446655440000"],
  "errors": []
}
```

### 3. Get Document

```bash
curl http://localhost:5000/api/documents/550e8400-e29b-41d4-a716-446655440000
```

### 4. List Documents

```bash
curl "http://localhost:5000/api/documents?page=1&pageSize=20&repoUrl=https://github.com/example/repo"
```

**Response:**
```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "repoUrl": "https://github.com/example/repo",
      "filePath": "docs/README.md",
      "title": "Project README",
      "createdAt": "2026-01-26T10:30:00Z",
      "updatedAt": "2026-01-26T10:30:00Z",
      "tokenCount": 150,
      "fileType": "md",
      "isCode": false
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1
}
```

### 5. Delete Document

```bash
curl -X DELETE http://localhost:5000/api/documents/550e8400-e29b-41d4-a716-446655440000
```

**Response:** 204 No Content

---

## Error Handling

All errors return JSON with `detail` field (Python API parity):

```json
{
  "detail": "Document not found"
}
```

| Status | Meaning |
|--------|---------|
| 400 | Bad Request - validation errors |
| 404 | Not Found - document doesn't exist |
| 429 | Too Many Requests - rate limit exceeded |
| 503 | Service Unavailable - embedding service down |

---

## Testing

### Run All Tests

```bash
dotnet test
```

### Run API Tests Only

```bash
dotnet test --filter "FullyQualifiedName~Api"
```

### Test with Real Database (Testcontainers)

```bash
# Requires Docker running
dotnet test --filter "Category=Integration"
```

---

## Troubleshooting

### "Embedding provider not configured"

Check `appsettings.json` has valid `Embedding:Provider` and credentials.

### "Vector store not available"

1. Verify database is running and accessible
2. Check connection string in configuration
3. Ensure SQL Server 2025 or PostgreSQL with pgvector

### "Rate limit exceeded"

Wait 60 seconds or reduce request frequency. Limit is 100 requests/minute per IP.

### Query returns empty results

1. Verify documents have been ingested
2. Check embedding service is working (logs show embedding calls)
3. Ensure query is relevant to ingested content

---

## Performance Tips

1. **Batch ingestion**: Send multiple documents per request (up to 1000)
2. **Limit results**: Use smaller `k` values when possible
3. **Skip full text**: Set `includeFullText: false` for list views
4. **Use filters**: Add `repoUrl` filter to narrow search scope

---

## Python API Differences

| Aspect | Python API | .NET API |
|--------|-----------|----------|
| Query endpoint | Embedded in /chat/completions/stream | Dedicated POST /api/query |
| Response format | Raw JSON | Raw JSON (same) |
| Error format | `{"detail": "..."}` | `{"detail": "..."}` (same) |
| Authentication | Anonymous | Anonymous (same) |
| Ingestion | N/A (FAISS in-memory) | POST /api/documents/ingest |

The .NET API extends Python functionality by exposing RAG operations as explicit REST endpoints, enabling external integrations and document management.
