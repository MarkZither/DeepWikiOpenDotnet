# Research: MCP Streaming RAG Service

**Phase**: 0 - Research & Technology Decisions  
**Date**: 2026-02-06  
**Status**: Complete

## Overview

This document consolidates research findings for implementing a transport-agnostic streaming RAG service in .NET 10 with Ollama and OpenAI provider adapters, HTTP NDJSON baseline streaming, optional SignalR hub, and IP-based rate-limiting.

## Technology Decisions

### Decision 1: Server-Side Streaming Mechanism

**Decision**: Use `IAsyncEnumerable<T>` for server-side streaming with ASP.NET Core controller support

**Rationale**:
- Native .NET async streams provide backpressure and cancellation via `CancellationToken`
- ASP.NET Core 6+ natively supports `IAsyncEnumerable<T>` return types in controllers
- Zero-allocation enumeration for high-throughput token streaming
- Composable with LINQ-style operators (`SelectAwait`, `WhereAwait`) for stream transformation
- Compatible with both HTTP streaming (NDJSON) and SignalR streaming methods

**Alternatives Considered**:
- **Channels (System.Threading.Channels)**: More complex; `IAsyncEnumerable` sufficient for one-to-one streaming
- **Reactive Extensions (Rx.NET)**: Heavier dependency; async streams are more idiomatic in modern .NET
- **Manual Response.WriteAsync loops**: Lower-level; `IAsyncEnumerable` provides better abstraction

**Implementation Pattern**:
```csharp
public async IAsyncEnumerable<GenerationDelta> GenerateAsync(
    GenerationRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await foreach (var delta in provider.StreamAsync(request, cancellationToken))
    {
        yield return delta;
    }
}
```

### Decision 2: HTTP Streaming Format

**Decision**: Line-delimited JSON (NDJSON) as baseline transport

**Rationale**:
- Simple, curl/fetch-compatible, no framework-specific client required
- Standard format for streaming JSON events (RFC 7464 JSON Text Sequences compatible)
- Works with `IAsyncEnumerable<T>` controller return type + `application/x-ndjson` content type
- Easy to test and debug with standard HTTP tools
- Compatible with server-sent events (SSE) philosophy but simpler

**Alternatives Considered**:
- **Server-Sent Events (SSE)**: More structured but requires `data:` prefix and event parsing; NDJSON simpler
- **WebSocket**: Bidirectional, but MVP only needs server→client streaming; WebSocket adds complexity
- **gRPC streaming**: Requires protobuf and client codegen; overkill for simple token streaming

**Implementation Pattern**:
```csharp
[HttpPost("stream")]
[Produces("application/x-ndjson")]
public async IAsyncEnumerable<GenerationDelta> StreamGeneration(
    [FromBody] PromptRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var delta in generationService.GenerateAsync(request, cancellationToken))
    {
        yield return delta;
    }
}
```

### Decision 3: Optional SignalR Hub for Rich Clients

**Decision**: Provide optional SignalR hub for TypeScript/.NET clients requiring bidirectional communication

**Rationale**:
- SignalR provides native reconnection, automatic transport fallback (WebSocket → SSE → long polling)
- Better developer experience for TypeScript clients (typed hub proxies)
- Not required for MVP (NDJSON baseline sufficient) but valuable for future extension work
- Must maintain contract parity with HTTP NDJSON (same `GenerationDelta` schema)

**Contract Parity Requirement**:
- SignalR streaming method MUST emit identical `GenerationDelta` JSON objects
- Contract tests validate both transports produce same event sequence for same input

### Decision 4: Provider Abstraction & Adapters

**Decision**: Define `IModelProvider` interface with Ollama and OpenAI implementations

**Rationale**:
- Local-first strategy (Constitution requirement): Ollama as primary, OpenAI as fallback
- Pluggable design enables future provider additions (Azure OpenAI, Anthropic, etc.)
- Each provider adapter handles SDK-specific streaming APIs and normalizes to `GenerationDelta`
- Configuration-driven provider selection (per-deployment ordered list)

**Provider Interface**:
```csharp
public interface IModelProvider
{
    string Name { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct);
    IAsyncEnumerable<GenerationDelta> StreamAsync(
        GenerationRequest request, 
        CancellationToken ct);
}
```

**Provider Selection Strategy**:
- Configuration: `"Providers": ["Ollama", "OpenAI"]` (ordered list)
- Runtime: try first available provider, fallback on connection failure
- Health checks: periodic availability checks for circuit-breaker behavior

### Decision 5: Ollama Streaming Integration

**Research**: Ollama HTTP API provides streaming via `/api/generate` endpoint with `stream: true`

**API Details**:
- Endpoint: `POST http://localhost:11434/api/generate`
- Request: `{ "model": "vicuna-13b", "prompt": "...", "stream": true }`
- Response: NDJSON stream of `{ "model": "...", "response": "token", "done": false }`
- Final chunk: `{ "response": "", "done": true, "context": [...], "total_duration": ... }`

**Adapter Implementation Notes**:
- Use `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` for streaming
- Parse NDJSON chunks with `System.Text.Json.JsonSerializer.DeserializeAsync`
- Map Ollama response chunks to `GenerationDelta` (seq, type, text, role)
- Handle `done: true` chunk as `GenerationDelta` with `type: "done"`
- Implement timeout for stalled streams (30s per Constitution, 5s for tests)

**Example Ollama Response**:
```json
{"model":"vicuna-13b","created_at":"2026-02-06T10:00:00Z","response":"This ","done":false}
{"model":"vicuna-13b","created_at":"2026-02-06T10:00:00.1Z","response":"is ","done":false}
{"model":"vicuna-13b","created_at":"2026-02-06T10:00:01Z","response":"","done":true}
```

### Decision 6: OpenAI Streaming Integration

**Research**: OpenAI SDK (`OpenAI` NuGet package 2.x) provides streaming via `CreateChatCompletionAsync` with `stream: true`

**SDK Details**:
- Package: `OpenAI` 2.0.0+ (official SDK with async stream support)
- Method: `ChatClient.CompleteChatStreamingAsync(messages, options)`
- Returns: `IAsyncEnumerable<StreamingChatCompletionUpdate>`
- Chunks contain: `ContentUpdate` (delta text), `FinishReason`, `Role`

**Adapter Implementation Notes**:
- Iterate `await foreach` over `CompleteChatStreamingAsync` result
- Map `StreamingChatCompletionUpdate` to `GenerationDelta`
- Track sequence numbers (OpenAI doesn't provide them)
- Emit `type: "done"` when `FinishReason != null`
- Handle API errors as `GenerationDelta` with `type: "error"`

**Example OpenAI Stream**:
```csharp
await foreach (var update in chatClient.CompleteChatStreamingAsync(messages))
{
    if (update.ContentUpdate.Count > 0)
    {
        yield return new GenerationDelta
        {
            PromptId = promptId,
            Type = "token",
            Seq = seq++,
            Text = update.ContentUpdate[0].Text,
            Role = "assistant"
        };
    }
    if (update.FinishReason != null)
    {
        yield return new GenerationDelta
        {
            PromptId = promptId,
            Type = "done",
            Seq = seq++,
            Metadata = new { FinishReason = update.FinishReason.ToString() }
        };
    }
}
```

### Decision 7: Stream Normalization & Validation

**Decision**: Implement `StreamNormalizer` to enforce sequence integrity, deduplication, and UTF-8 safety

**Requirements**:
- **Sequence numbers**: Assign monotonic sequence numbers to each delta (providers may not provide)
- **Deduplication**: Detect duplicate tokens (same seq or identical consecutive text) and filter
- **UTF-8 validation**: Ensure incomplete multi-byte UTF-8 sequences are buffered until complete
- **Error wrapping**: Convert provider exceptions to structured `GenerationDelta` error events

**Implementation Pattern**:
```csharp
public async IAsyncEnumerable<GenerationDelta> NormalizeAsync(
    IAsyncEnumerable<GenerationDelta> source,
    [EnumeratorCancellation] CancellationToken ct)
{
    int seq = 0;
    string? lastText = null;
    
    await foreach (var delta in source.WithCancellation(ct))
    {
        // Skip duplicates
        if (delta.Text == lastText && delta.Type == "token") continue;
        
        // Assign sequence
        delta.Seq = seq++;
        lastText = delta.Text;
        
        yield return delta;
    }
}
```

### Decision 8: IP-Based Rate Limiting

**Decision**: Token-bucket rate limiter with per-IP tracking using `AspNetCoreRateLimit` library

**Rationale**:
- Simple, proven library for ASP.NET Core rate limiting
- Token-bucket algorithm provides smooth rate limiting with burst tolerance
- Per-IP tracking sufficient for MVP (internal-only deployment)
- Configurable limits (e.g., 100 requests/minute per IP)
- Returns standard `429 Too Many Requests` with `Retry-After` header

**Alternatives Considered**:
- **Built-in .NET 7+ rate limiting**: Good but less mature than AspNetCoreRateLimit for production
- **Manual middleware**: Reinventing the wheel; library provides battle-tested implementation
- **Fixed-window**: Simpler but allows burst at window boundary; token-bucket smoother

**Configuration**:
```json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Real-IP",
    "GeneralRules": [
      {
        "Endpoint": "POST:/api/generation/stream",
        "Period": "1m",
        "Limit": 100
      }
    ]
  }
}
```

### Decision 9: Observability & Metrics

**Decision**: Use .NET built-in metrics (`System.Diagnostics.Metrics`) with OpenTelemetry export

**Metrics to Track**:
- **Time-to-First-Token (TTF)**: Histogram (ms) - measures latency from request to first delta
- **Tokens per Second**: Rate - throughput measurement per session
- **Total Tokens per Session**: Counter - cost estimation
- **Error Rate**: Counter by error type (provider failure, timeout, cancellation)
- **Active Sessions**: Gauge - concurrent session count

**Implementation Pattern**:
```csharp
public class GenerationMetrics
{
    private readonly Meter _meter;
    private readonly Histogram<long> _ttfHistogram;
    private readonly Counter<long> _tokenCounter;
    
    public GenerationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("DeepWiki.Generation");
        _ttfHistogram = _meter.CreateHistogram<long>("generation.ttf", "ms");
        _tokenCounter = _meter.CreateCounter<long>("generation.tokens");
    }
    
    public void RecordTTF(long milliseconds) => _ttfHistogram.Record(milliseconds);
    public void RecordToken() => _tokenCounter.Add(1);
}
```

### Decision 10: Cancellation & Timeout Handling

**Decision**: Respect `CancellationToken` throughout stack with 30s provider stall timeout

**Requirements**:
- Client disconnect → immediate cancellation propagation
- Provider stall (no tokens for 30s) → timeout and emit error delta
- Test environments use shorter timeout (5s) for fast feedback
- Cancellation emits final `type: "done"` or `type: "error"` delta within 200ms

**Timeout Pattern**:
```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    cancellationToken, timeoutCts.Token);

try
{
    await foreach (var delta in provider.StreamAsync(request, linkedCts.Token))
    {
        yield return delta;
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // Reset timeout on each token
    }
}
catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
{
    yield return new GenerationDelta
    {
        PromptId = request.PromptId,
        Type = "error",
        Metadata = new { Code = "provider_timeout", Message = "Provider stalled" }
    };
}
```

## Best Practices from Ecosystem

### ASP.NET Core Streaming Best Practices
- Use `[Produces("application/x-ndjson")]` attribute for NDJSON endpoints
- Return `IAsyncEnumerable<T>` from controller actions for automatic streaming
- Include `[EnumeratorCancellation]` attribute on `CancellationToken` parameter
- Set `HttpCompletionOption.ResponseHeadersRead` for client-side streaming consumption

### Ollama Integration Best Practices
- Health check: `GET /api/tags` returns available models (use for availability check)
- Model loading: first request may be slow (model load time); subsequent requests fast
- Context window: track context size to avoid exceeding model limits
- Streaming reliability: Ollama may stall on very long prompts; enforce timeout

### OpenAI SDK Best Practices
- Use streaming for long responses (>100 tokens) to reduce perceived latency
- Handle rate limits: OpenAI returns 429 with `Retry-After` header
- Token counting: use `Tiktoken` library for accurate token estimation pre-request
- Cost tracking: log prompt + completion tokens for billing correlation

## Testing Strategy

### Unit Tests
- **Provider adapters**: Mock HTTP responses, validate delta mapping, test cancellation
- **Stream normalizer**: Test sequence assignment, dedup, UTF-8 handling
- **Rate limiter**: Test limit enforcement, burst tolerance, reset behavior

### Integration Tests
- **HTTP NDJSON**: TestServer request, parse NDJSON stream, validate delta sequence
- **SignalR hub**: Connect via SignalR client, validate stream parity with HTTP
- **End-to-end RAG**: Stub vector store, mock provider, validate retrieval → generation flow

### Contract Tests
- **Transport parity**: Same input to HTTP and SignalR must produce identical delta sequences
- **Schema validation**: Validate `GenerationDelta` JSON schema for all transports
- **Error contracts**: Validate structured error deltas conform to schema

## Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| ASP.NET Core | 10.0 | HTTP server, controllers, SignalR |
| Microsoft.AspNetCore.SignalR | 10.0 | Optional SignalR hub |
| OpenAI | 2.0.0+ | OpenAI streaming SDK |
| AspNetCoreRateLimit | 5.0+ | IP-based rate limiting |
| System.Threading.Channels | (built-in) | Optional buffering (if needed) |
| Microsoft.Extensions.Diagnostics | (built-in) | Metrics and telemetry |
| xUnit | 2.6+ | Unit testing |
| Microsoft.AspNetCore.TestHost | 10.0 | Integration testing |

## References

- [ASP.NET Core IAsyncEnumerable Streaming](https://learn.microsoft.com/en-us/aspnet/core/web-api/action-return-types?view=aspnetcore-8.0#iasyncenumerablet-type)
- [Ollama API Reference](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [OpenAI .NET SDK Streaming](https://github.com/openai/openai-dotnet)
- [AspNetCoreRateLimit Documentation](https://github.com/stefanprodan/AspNetCoreRateLimit)
- [RFC 7464: JSON Text Sequences](https://datatracker.ietf.org/doc/html/rfc7464)

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Ollama unavailable in CI | Medium | High | Mock provider for CI; integration tests use Testcontainers |
| Provider stalls/hangs | Medium | Medium | 30s timeout with error delta emission |
| SignalR complexity | Low | Medium | Keep SignalR optional; NDJSON baseline sufficient for MVP |
| Rate limit too restrictive | Low | Low | Make limits configurable; start conservative (100/min) |
| UTF-8 encoding issues | Low | High | Explicit UTF-8 validation in normalizer; unit tests for edge cases |

## Open Questions

- **Q: Should provider selection be runtime-configurable per request?**  
  A: No, per-deployment config only for MVP (clarified in spec)

- **Q: Should we support streaming from multiple providers simultaneously?**  
  A: No, single provider per request; fallback only on connection failure

- **Q: Should client code (VS Code extension) be in same repo?**  
  A: Yes, `extensions/copilot-private` scaffolded in repo for future work

---

**Phase 0 Complete**: All technology decisions resolved. Proceed to Phase 1 (data model, contracts, quickstart).
