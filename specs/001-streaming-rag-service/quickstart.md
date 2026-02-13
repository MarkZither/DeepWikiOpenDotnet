# Quickstart: MCP Streaming RAG Service

**Phase**: 1 - Design & Contracts  
**Date**: 2026-02-06  
**Audience**: Developers, QA, Product

## Overview

This guide demonstrates how to use the streaming RAG generation service via HTTP NDJSON baseline transport. Examples use `curl` for simplicity (no client libraries required).

## Prerequisites

- DeepWiki API service running on `http://localhost:5000` (or configured URL)
- Ollama running locally on `http://localhost:11434` (for local-first generation)
- Sample documents indexed in vector store (run ingestion pipeline first)

**Quick check**:
```bash
# Verify API is running
curl http://localhost:5000/health

# Verify Ollama is available
curl http://localhost:11434/api/tags
```

## Basic Workflow

### Step 1: Create a Session

```bash
curl -X POST http://localhost:5000/api/generation/session \
  -H "Content-Type: application/json" \
  -d '{
    "owner": "internal-dev",
    "context": { "environment": "development" }
  }'
```

**Response**:
```json
{
  "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479"
}
```

**Notes**:
- `sessionId` is required for all subsequent requests
- Sessions are in-memory (MVP) and expire after 1 hour of inactivity
- `owner` and `context` are optional metadata

### Step 2: Stream a Prompt

```bash
curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -N \
  -d '{
    "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "prompt": "Explain vector embeddings in C#",
    "topK": 10
  }'
```

**Response** (NDJSON stream):
```json
{"promptId":"abc-123","type":"token","seq":0,"text":"Vector embeddings ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":1,"text":"are dense numerical ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":2,"text":"representations of text. ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":3,"text":"In C#, you can store ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":4,"text":"embeddings as float[].","role":"assistant"}
{"promptId":"abc-123","type":"done","seq":5,"role":"assistant","metadata":{"tokenCount":25,"finishReason":"stop"}}
```

**Key Points**:
- `-N` flag disables buffering for line-by-line streaming
- Each line is a JSON object (NDJSON format)
- `seq` increases monotonically (0, 1, 2, ...)
- Final event has `type: "done"` with summary metadata
- `promptId` is same across all deltas for this prompt

### Step 3: Cancel an In-Flight Prompt (Optional)

If generation is taking too long, cancel it:

```bash
curl -X POST http://localhost:5000/api/generation/cancel \
  -H "Content-Type: application/json" \
  -d '{
    "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "promptId": "abc-123"
  }'
```

**Response**: `204 No Content` (success)

**Effect**: Server stops streaming and emits final delta:
```json
{"promptId":"abc-123","type":"done","seq":6,"role":"assistant","metadata":{"cancelled":true}}
```

## Advanced Scenarios

### Scenario A: Retry with Idempotency Key

Use `idempotencyKey` to safely retry requests:

```bash
curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -N \
  -d '{
    "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "prompt": "What is RAG?",
    "idempotencyKey": "client-req-001",
    "topK": 5
  }'
```

**Behavior**:
- First request: generates new response
- Retry with same `idempotencyKey`: returns cached response (no re-generation)
- Prevents duplicate generation on network failures

### Scenario B: Filtered Retrieval

Retrieve documents from specific repository:

```bash
curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -N \
  -d '{
    "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "prompt": "How does EF Core handle vector types?",
    "topK": 10,
    "filters": {
      "repoUrl": "https://github.com/MarkZither/DeepWikiOpenDotnet"
    }
  }'
```

**Effect**: RAG retrieval limited to documents from specified repository

### Scenario C: Parse NDJSON Stream in Script

**Bash script**:
```bash
#!/bin/bash
SESSION_ID=$(curl -s -X POST http://localhost:5000/api/generation/session \
  -H "Content-Type: application/json" \
  -d '{"owner":"script"}' | jq -r '.sessionId')

echo "Session: $SESSION_ID"

curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -N \
  -d "{\"sessionId\":\"$SESSION_ID\",\"prompt\":\"Explain streaming\"}" \
  | while read -r line; do
      TYPE=$(echo "$line" | jq -r '.type')
      TEXT=$(echo "$line" | jq -r '.text // empty')
      if [ "$TYPE" = "token" ]; then
        echo -n "$TEXT"
      elif [ "$TYPE" = "done" ]; then
        echo ""
        echo "Generation complete"
      elif [ "$TYPE" = "error" ]; then
        echo "Error: $(echo "$line" | jq -r '.metadata.message')"
      fi
    done
```

Tip: A ready-to-run example script is available at `specs/001-streaming-rag-service/examples/curl-demo.sh`.

#### Test helper: `scripts/test-ollama-openai.sh`
A convenience script is provided to validate the OpenAI provider against an Ollama (or OpenAI‑compatible) endpoint. Behavior:

- Probes for a running API on the provided URL, `http://localhost:5000`, or `http://localhost:5484` and will *use* the running service if found.
- If no service is found and `--no-start` is **not** passed, the script will start the API locally (dotnet run) with `OpenAI:BaseUrl` set to the supplied Ollama URL.
- If `--no-start` is passed the script only probes and fails if no API is running.

Usage examples:

- Run against an already-running API (no start):

```bash
./scripts/test-ollama-openai.sh --no-start
```

- Start the API (if needed) and test OpenAI→Ollama streaming:

```bash
./scripts/test-ollama-openai.sh http://localhost:11434 http://localhost:5000
```

- Force the script to use a specific API port (probe first, start only if not in use):

```bash
./scripts/test-ollama-openai.sh http://localhost:11434 http://localhost:5484
```

**Output** (example):
```
Using session: f47ac10b-58cc-4372-a567-0e02b2c3d479
Running streaming prompt (OpenAI -> Ollama)...
Hello from Ollama!
-- Generation complete --
```

**Notes**:
- Configure `OpenAI:BaseUrl` and `OpenAI:Provider` in `appsettings.json` to point the OpenAI provider at Ollama for persistent setups.
- The script is intended for local testing and CI smoke tests; it is not required for normal production use.

**Output**:
```
Session: f47ac10b-58cc-4372-a567-0e02b2c3d479
Streaming allows incremental delivery of content...
Generation complete
```

## Error Handling

### Error Response: Invalid Request

```bash
curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -N \
  -d '{
    "sessionId": "invalid-uuid",
    "prompt": ""
  }'
```

**Response**: `400 Bad Request`
```json
{
  "code": "invalid_request",
  "message": "Prompt text cannot be empty"
}
```

### Error Event in Stream: Provider Timeout

If provider stalls (no tokens for 30s):

```json
{"promptId":"abc-123","type":"token","seq":0,"text":"Starting...","role":"assistant"}
{"promptId":"abc-123","type":"error","seq":1,"role":"system","metadata":{"code":"provider_timeout","message":"Provider stalled for 30s with no tokens"}}
```

**Client Handling**:
- Detect `type: "error"` events in stream
- Display error message from `metadata.message`
- Optionally retry with exponential backoff

### Rate Limiting Response

```bash
# After 100 requests in 1 minute
curl -X POST http://localhost:5000/api/generation/stream \
  -H "Content-Type: application/json" \
  -d '{...}'
```

**Response**: `429 Too Many Requests`
```json
{
  "code": "rate_limit_exceeded",
  "message": "Too many requests from this IP. Retry after 60 seconds."
}
```

**Headers**:
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1675872000
Retry-After: 60
```

**Client Handling**:
- Check `Retry-After` header for wait duration
- Exponential backoff for repeated 429s

## Performance Expectations

| Metric | Target (Local Ollama) | Target (Remote OpenAI) |
|--------|----------------------|------------------------|
| Time-to-First-Token (TTF) | <500ms | <1s |
| Token Throughput | >50 tokens/sec | >20 tokens/sec |
| Cancellation Latency | <200ms | <200ms |
| Rate Limit | 100 requests/min per IP | Same |

**Typical Latency Breakdown** (local Ollama):
- Session creation: <10ms
- RAG retrieval (10 docs): 50-150ms
- First token: 100-400ms (depends on prompt length)
- Subsequent tokens: 20-50ms each

## Testing Checklist

### Manual Testing
- [ ] Create session and receive valid `sessionId`
- [ ] Submit prompt and receive NDJSON stream with monotonic `seq`
- [ ] Verify final delta has `type: "done"`
- [ ] Cancel in-flight prompt and verify cancellation delta
- [ ] Retry with same `idempotencyKey` and verify cached response
- [ ] Submit invalid request (empty prompt) and verify 400 error
- [ ] Exceed rate limit and verify 429 with `Retry-After` header

### Automated Testing (Integration Tests)
```csharp
[Fact]
public async Task StreamGeneration_ValidRequest_ReturnsNDJSONStream()
{
    // Arrange
    var client = _factory.CreateClient();
    var sessionResponse = await client.PostAsJsonAsync("/api/generation/session", 
        new SessionRequest { Owner = "test" });
    var session = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>();
    
    // Act
    var streamRequest = new PromptRequest
    {
        SessionId = session.SessionId,
        Prompt = "Test prompt",
        TopK = 5
    };
    var response = await client.PostAsJsonAsync("/api/generation/stream", streamRequest);
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType.MediaType);
    
    var stream = await response.Content.ReadAsStreamAsync();
    var reader = new StreamReader(stream);
    var deltas = new List<GenerationDelta>();
    
    string line;
    while ((line = await reader.ReadLineAsync()) != null)
    {
        var delta = JsonSerializer.Deserialize<GenerationDelta>(line);
        deltas.Add(delta);
    }
    
    // Verify sequence integrity
    Assert.All(deltas, (d, i) => Assert.Equal(i, d.Seq));
    Assert.Equal("done", deltas.Last().Type);
}
```

## Troubleshooting

### Issue: No tokens streaming

**Symptoms**: Request hangs with no response

**Causes**:
1. Ollama not running → verify `curl http://localhost:11434/api/tags`
2. No documents indexed → run ingestion pipeline first
3. Provider timeout (30s) → check Ollama logs for errors

**Resolution**: Check health endpoints and restart Ollama if needed

### Issue: Rate limit hit unexpectedly

**Symptoms**: 429 responses after few requests

**Causes**:
1. IP shared with other clients → check `X-RateLimit-Remaining` header
2. Limit too conservative (100/min default)

**Resolution**: Adjust rate limit config in `appsettings.json`:
```json
{
  "IpRateLimiting": {
    "GeneralRules": [
      {
        "Endpoint": "POST:/api/generation/stream",
        "Period": "1m",
        "Limit": 200
      }
    ]
  }
}
```

### Issue: Incomplete UTF-8 characters

**Symptoms**: Garbled text in token deltas

**Cause**: Provider emitted incomplete multi-byte UTF-8 sequence

**Resolution**: Stream normalizer buffers incomplete sequences (handled automatically)

## SignalR Hub (Optional Enhancement)

**Task T071**: SignalR provides richer bidirectional communication for TypeScript and .NET clients.

### SignalR vs HTTP NDJSON

| Feature | HTTP NDJSON | SignalR |
|---------|-------------|---------|
| **Transport** | HTTP/1.1 streaming | WebSockets (preferred), Server-Sent Events, Long Polling |
| **Client Libraries** | None (curl/fetch) | `@microsoft/signalr` (TypeScript), `Microsoft.AspNetCore.SignalR.Client` (.NET) |
| **Bidirectional** | No | Yes (server can push events to client) |
| **Reconnection** | Manual | Automatic with backoff |
| **Best For** | CLI tools, scripts, simple clients | Web UIs, real-time dashboards, TypeScript/.NET apps |

### TypeScript SignalR Client Example

**Installation**:
```bash
npm install @microsoft/signalr
```

**Usage**:
```typescript
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';

async function streamRAGPrompt() {
  // Connect to hub
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl("http://localhost:5000/hubs/generation")
    .withAutomaticReconnect()
    .build();

  await connection.start();
  console.log("SignalR connected");

  // Create session
  const sessionResponse = await connection.invoke("StartSession", {
    owner: "typescript-client",
    context: { environment: "development" }
  });
  
  console.log("Session created:", sessionResponse.sessionId);

  // Stream prompt and collect deltas
  connection.stream("SendPrompt", {
    sessionId: sessionResponse.sessionId,
    prompt: "Explain vector embeddings in C#",
    topK: 10,
    idempotencyKey: crypto.randomUUID()
  }).subscribe({
    next: (delta) => {
      // Handle each token delta
      if (delta.type === "token") {
        process.stdout.write(delta.text); // Print token by token
      } else if (delta.type === "done") {
        console.log("\nGeneration complete:", delta.metadata);
      } else if (delta.type === "error") {
        console.error("\nError:", delta.metadata);
      }
    },
    complete: () => {
      console.log("\nStream completed");
    },
    error: (err) => {
      console.error("Stream error:", err);
    }
  });

  // Optional: Cancel prompt (call this from another handler)
  // await connection.invoke("Cancel", {
  //   sessionId: sessionResponse.sessionId,
  //   promptId: "prompt-id-from-delta"
  // });
}

streamRAGPrompt().catch(console.error);
```

### C# SignalR Client Example

**Installation**:
```bash
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

**Usage**:
```csharp
using Microsoft.AspNetCore.SignalR.Client;
using DeepWiki.Data.Abstractions.Models;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/generation")
    .WithAutomaticReconnect()
    .Build();

await connection.StartAsync();
Console.WriteLine("SignalR connected");

// Create session
var sessionResponse = await connection.InvokeAsync<SessionResponse>(
    "StartSession", 
    new SessionRequest { Owner = "csharp-client" });

Console.WriteLine($"Session created: {sessionResponse.SessionId}");

// Stream prompt
var stream = connection.StreamAsync<GenerationDelta>(
    "SendPrompt",
    new PromptRequest
    {
        SessionId = sessionResponse.SessionId,
        Prompt = "Explain vector embeddings in C#",
        TopK = 10,
        IdempotencyKey = Guid.NewGuid().ToString()
    });

await foreach (var delta in stream)
{
    if (delta.Type == "token")
    {
        Console.Write(delta.Text); // Print token by token
    }
    else if (delta.Type == "done")
    {
        Console.WriteLine($"\nGeneration complete: {delta.Metadata}");
    }
}

await connection.StopAsync();
```

### Health Check for SignalR Hub

**Verify hub is registered**:
```bash
curl http://localhost:5000/hubs/generation
```

**Expected Response**: `404` (hub only accepts WebSocket connections, not GET requests)

**Check hub connectivity with JavaScript**:
```javascript
// Quick health check in browser console or Node.js
const { HubConnectionBuilder } = require('@microsoft/signalr');

const connection = new HubConnectionBuilder()
  .withUrl("http://localhost:5000/hubs/generation")
  .build();

connection.start()
  .then(() => {
    console.log("✓ SignalR hub available");
    return connection.stop();
  })
  .catch(err => {
    console.error("✗ SignalR hub unavailable:", err.message);
  });
```

### SignalR Configuration

**CORS Origins** (edit `appsettings.json`):
```json
{
  "SignalR": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173",
      "http://localhost:8080",
      "https://your-frontend-domain.com"
    ]
  }
}
```

**Default origins**: `localhost:3000` (React), `localhost:5173` (Vite), `localhost:8080` (Vue)

---

## Next Steps

1. **Integration**: Integrate with VS Code extension client (see `extensions/copilot-private`)
2. **Monitoring**: Set up Grafana dashboards for TTF, token throughput, error rates
3. **Load Testing**: Run k6 or Locust to validate rate limits and concurrency handling
4. **SignalR Clients**: Try TypeScript or C# SignalR clients for richer bidirectional communication

## References

- [OpenAPI Specification](contracts/generation-service.yaml)
- [GenerationDelta JSON Schema](contracts/generation-delta.schema.json)
- [Data Model](data-model.md)
- [Research Document](research.md)

---

**Phase 1 Quickstart Complete**: Ready for implementation (Phase 2 - `/speckit.tasks`).
