# Architecture Diagram: Streaming RAG Service

## System Overview

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                            Client Layer                                 │
├─────────────────────────────────────────────────────────────────────────┤
│  curl/fetch        VS Code Extension       TypeScript/.NET Client       │
│      │                    │                         │                    │
│      └────────────────────┴─────────────────────────┘                    │
│                             │                                            │
│                             ▼                                            │
│         ┌──────────────────────────────────────────┐                    │
│         │   HTTP NDJSON   │   SignalR Hub (opt)   │                    │
│         └──────────────────────────────────────────┘                    │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                       API Service Layer                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │              GenerationController                            │       │
│  │  POST /api/generation/session   (Create Session)            │       │
│  │  POST /api/generation/stream    (Stream Tokens - NDJSON)    │       │
│  │  POST /api/generation/cancel    (Cancel Generation)         │       │
│  │  GET  /api/generation/health    (Provider Health)           │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                         │                                                │
│                         │                                                │
│  ┌──────────────────────┴──────────────────────────────────────┐       │
│  │               Middleware Pipeline                            │       │
│  │  ┌────────────────┐  ┌──────────────┐  ┌────────────────┐  │       │
│  │  │ Rate Limiting  │→ │ CORS Policy  │→ │Security Headers│  │       │
│  │  │ (100 req/min)  │  │              │  │                │  │       │
│  │  └────────────────┘  └──────────────┘  └────────────────┘  │       │
│  └─────────────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      Service Orchestration Layer                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    GenerationService                             │  │
│  │  (Implements IGenerationService)                                 │  │
│  │                                                                  │  │
│  │  Responsibilities:                                               │  │
│  │  • RAG orchestration (retrieve + generate)                       │  │
│  │  • Provider selection & fallback logic                           │  │
│  │  • Cancellation propagation                                      │  │
│  │  • Idempotency key caching                                       │  │
│  │  • Timeout enforcement (30s)                                     │  │
│  │  • Error delta emission                                          │  │
│  │  • StreamNormalizer pipeline integration                         │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                         │                                                │
│           ┌─────────────┴─────────────┐                                 │
│           │                           │                                  │
│           ▼                           ▼                                  │
│  ┌────────────────┐         ┌─────────────────┐                         │
│  │ SessionManager │         │StreamNormalizer │                         │
│  │                │         │                 │                         │
│  │ • Session CRUD │         │ • Sequence #'s  │                         │
│  │ • Prompt CRUD  │         │ • Deduplication │                         │
│  │ • Expiration   │         │ • UTF-8 fix     │                         │
│  │   (1 hour)     │         │                 │                         │
│  └────────────────┘         └─────────────────┘                         │
└─────────────────────────────────────────────────────────────────────────┘
           │                           
           │                           
           ▼                           
┌─────────────────────────────────────────────────────────────────────────┐
│                        Provider Layer                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    IModelProvider                                │  │
│  │  • Name: string                                                  │  │
│  │  • IsAvailableAsync(): Task<bool>                                │  │
│  │  • StreamAsync(): IAsyncEnumerable<GenerationDelta>              │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                         │                                                │
│           ┌─────────────┴─────────────┐                                 │
│           │                           │                                  │
│           ▼                           ▼                                  │
│  ┌──────────────────┐       ┌──────────────────┐                        │
│  │ OllamaProvider   │       │ OpenAIProvider   │                        │
│  │                  │       │                  │                        │
│  │ • Local-first    │       │ • Cloud fallback │                        │
│  │ • HTTP streaming │       │ • SDK streaming  │                        │
│  │ • /api/generate  │       │ • Circuit breaker│                        │
│  │ • 30s timeout    │       │ • Health check   │                        │
│  └──────────────────┘       └──────────────────┘                        │
│           │                           │                                  │
│           ▼                           ▼                                  │
│  ┌──────────────────┐       ┌──────────────────┐                        │
│  │ Ollama (local)   │       │ OpenAI API       │                        │
│  │ localhost:11434  │       │ (Remote)         │                        │
│  └──────────────────┘       └──────────────────┘                        │
└─────────────────────────────────────────────────────────────────────────┘
           │
           │ (RAG Context Retrieval)
           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Data Layer                                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    IVectorStore                                  │  │
│  │  • QueryNearestAsync(embedding, topK)                            │  │
│  │  • Filter by metadata                                            │  │
│  │  • Returns DocumentEntity[]                                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                         │                                                │
│           ┌─────────────┴─────────────┐                                 │
│           │                           │                                  │
│           ▼                           ▼                                  │
│  ┌──────────────────┐       ┌──────────────────┐                        │
│  │ SQL Server 2025  │       │ PostgreSQL 17+   │                        │
│  │ (HNSW Index)     │       │ (pgvector)       │                        │
│  └──────────────────┘       └──────────────────┘                        │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                    Observability Layer                                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                 GenerationMetrics                                │  │
│  │  • Time To First Token (TTF) - Histogram                         │  │
│  │  • Tokens Per Second - Counter                                   │  │
│  │  • Token Count (total) - Counter                                 │  │
│  │  • Error Rate by Type - Counter                                  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                         │                                                │
│                         ▼                                                │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │              OpenTelemetry Exporter                              │  │
│  │  → Prometheus (metrics)                                          │  │
│  │  → Grafana (dashboards)                                          │  │
│  │  → Application Insights (optional)                               │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

## Component Interaction Flow

### 1. Session Creation
```text
Client → GenerationController.CreateSession
       → SessionManager.CreateSessionAsync
       → Returns SessionResponse (sessionId, createdAt, expiresAt)
```

### 2. Streaming Generation (Happy Path)
```text
Client → GenerationController.StreamGeneration (POST with PromptRequest)
       → GenerationService.GenerateAsync
       → IVectorStore.QueryNearestAsync (retrieve documents)
       → Format RAG context (system prompt + retrieved docs)
       → Provider selection: try Ollama → fallback OpenAI if unavailable
       → IModelProvider.StreamAsync (returns IAsyncEnumerable<GenerationDelta>)
       → StreamNormalizer (sequence, dedupe, UTF-8 fix)
       → Yield GenerationDelta tokens to client (NDJSON/SignalR)
       → Final delta: { type: "done", done: true }
```

### 3. Cancellation
```text
Client → GenerationController.CancelGeneration (POST with CancelRequest)
       → GenerationService.CancelAsync
       → CancellationTokenSource.Cancel()
       → Provider stream breaks
       → SessionManager marks Prompt as Cancelled
       → Final delta: { type: "done", done: true, cancelled: true }
```

### 4. Provider Failover
```text
GenerationService tries providers in order:
  1. OllamaProvider.IsAvailableAsync() → false (connection timeout)
  2. Circuit breaker: skip Ollama for 60s
  3. OpenAIProvider.IsAvailableAsync() → true
  4. OpenAIProvider.StreamAsync() → success
  5. Log provider switch
```

### 5. Error Handling
```text
Provider throws exception OR times out (30s stall):
  → GenerationService catches
  → Emits error delta: { type: "error", error: { code, message, metadata } }
  → Emits done delta: { type: "done", done: true }
  → SessionManager marks Prompt as Failed
```

## Data Flow

### GenerationDelta Schema
```json
{
  "type": "token|done|error",
  "sequence": 42,
  "text": "token text",
  "role": "assistant",
  "done": false,
  "cancelled": false,
  "error": {
    "code": "provider_timeout",
    "message": "Provider stalled for >30s",
    "metadata": { "provider": "Ollama" }
  },
  "metadata": {
    "model": "vicuna-13b",
    "provider": "Ollama",
    "sessionId": "uuid",
    "promptId": "uuid"
  }
}
```

### Session Lifecycle
```text
Created (expires in 1h) → Active (prompts in progress) → Expired (auto-cleanup)
                                    ↓
                          Prompts: Pending → Streaming → Completed/Cancelled/Failed
```

## Transport Layers

### HTTP NDJSON (Baseline - MVP)
- **Content-Type**: `application/x-ndjson`
- **Streaming**: Server-sent events (SSE-like), one JSON per line
- **Compatibility**: curl, fetch, any HTTP client
- **Example**:
  ```bash
  curl -X POST http://localhost:5000/api/generation/stream \
    -H "Content-Type: application/json" \
    -d '{"sessionId":"...","prompt":"..."}' \
    --no-buffer
  ```

### SignalR Hub (Optional - Rich Clients)
- **Transport**: WebSocket (preferred) or long-polling
- **Methods**: `StartSession`, `SendPrompt` (streaming), `Cancel`
- **Compatibility**: TypeScript, .NET clients
- **Example** (TypeScript):
  ```typescript
  const connection = new HubConnectionBuilder()
    .withUrl("/hubs/generation")
    .build();
  
  for await (const delta of connection.stream("SendPrompt", request)) {
    console.log(delta.text);
  }
  ```

## Configuration

### appsettings.json
```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "DefaultModel": "vicuna-13b",
    "Timeout": 30
  },
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4",
    "Enabled": false
  },
  "RateLimiting": {
    "RequestsPerMinute": 100,
    "BurstSize": 20
  },
  "Generation": {
    "SessionExpirationMinutes": 60,
    "DefaultTopK": 10,
    "MaxTopK": 20
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "DeepWiki.Rag.Core": "Debug"
    }
  }
}
```

## Key Design Decisions

1. **Local-First ML**: Ollama as primary provider, OpenAI as fallback (configurable)
2. **Transport Agnostic**: Service layer returns `IAsyncEnumerable<GenerationDelta>`, works with HTTP/SignalR
3. **Test-First**: All components have unit tests, integration tests, contract parity tests
4. **Observability**: OpenTelemetry metrics for TTF, throughput, error rates
5. **Graceful Degradation**: Circuit breaker skips unavailable providers, rate limiting prevents abuse
6. **Session Isolation**: Each session manages its own prompts and lifecycle
7. **Idempotency**: Duplicate requests with same idempotencyKey return cached results
8. **Backpressure**: StreamNormalizer buffers tokens if client consumption is slow

## Performance Characteristics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Time To First Token (TTF) | <500ms (local Ollama) | Stopwatch from request to first delta |
| Token Throughput | >50 tokens/sec | Token count / elapsed time |
| Cancellation Latency | <200ms | Time from cancel request to final delta |
| Session Expiration | 1 hour inactivity | Auto-cleanup background task |
| Rate Limit | 100 req/min per IP | Token bucket algorithm |

## Security Considerations

- **No Authentication (MVP)**: Internal-only deployment, IP-based rate limiting
- **Rate Limiting**: Prevents abuse, 100 req/min per IP with burst allowance
- **Security Headers**: X-Content-Type-Options, X-Frame-Options for production
- **Input Validation**: Prompt length limits, topK range checks, sessionId format
- **Error Sanitization**: Stack traces suppressed in production, structured error deltas

## Deployment Architecture

```text
┌────────────────────────────────────────────────────────────┐
│                     Docker Compose                         │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │ API Service  │  │ Ollama       │  │ SQL Server/  │     │
│  │ (Port 5000)  │→ │ (Port 11434) │  │ PostgreSQL   │     │
│  │              │  │              │  │ (Vector DB)  │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│         │                  │                  │             │
│         └──────────────────┴──────────────────┘             │
│                        Network                              │
└────────────────────────────────────────────────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │ Prometheus + Grafana  │
              │ (Metrics & Dashboards)│
              └───────────────────────┘
```

## Testing Strategy

1. **Unit Tests**: StreamNormalizer, SessionManager, Providers (mocked HTTP)
2. **Contract Tests**: IGenerationService, IModelProvider interface compliance
3. **Integration Tests**: Full RAG flow with TestServer, Testcontainers (databases)
4. **Transport Parity Tests**: HTTP NDJSON vs SignalR produce identical deltas
5. **Performance Tests**: TTF, throughput, cancellation latency benchmarks
6. **E2E Tests**: Stub server + VS Code extension, curl scenarios

## References

- [Implementation Plan](plan.md) - Technical decisions and architecture rationale
- [Data Model](data-model.md) - Entity schemas and relationships
- [API Contracts](contracts/) - OpenAPI specs and JSON schemas
- [Quick Start](quickstart.md) - Usage examples and integration guide
