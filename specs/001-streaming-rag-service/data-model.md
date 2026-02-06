# Data Model: MCP Streaming RAG Service

**Phase**: 1 - Design & Contracts  
**Date**: 2026-02-06  
**Status**: Complete

## Overview

This document defines the core entities, DTOs, and data relationships for the streaming RAG service. All types are JSON-serializable and compatible with Microsoft Agent Framework tool binding requirements.

## Core Entities

### Session

Represents an active generation session with context and lifecycle tracking.

**Properties**:
```csharp
public class Session
{
    public string SessionId { get; init; }         // Unique session identifier (GUID)
    public string? Owner { get; init; }            // Optional: org/repo identifier (deferred for MVP)
    public DateTime CreatedAt { get; init; }       // Session creation timestamp
    public DateTime LastActiveAt { get; set; }     // Last activity timestamp
    public SessionStatus Status { get; set; }      // Active, Completed, Cancelled, Error
}

public enum SessionStatus
{
    Active,
    Completed,
    Cancelled,
    Error
}
```

**Storage**: In-memory for MVP (concurrent dictionary keyed by `SessionId`)  
**Future**: Persist to database for session history and audit

**Validation Rules**:
- `SessionId` MUST be globally unique (use `Guid.NewGuid()`)
- `CreatedAt` MUST be UTC
- `LastActiveAt` MUST be updated on each prompt activity

### Prompt

Represents a single prompt submission within a session.

**Properties**:
```csharp
public class Prompt
{
    public string PromptId { get; init; }          // Unique prompt identifier (GUID)
    public string SessionId { get; init; }         // Parent session identifier
    public string Text { get; init; }              // User prompt text
    public string? IdempotencyKey { get; init; }   // Optional: retry-safe key
    public PromptStatus Status { get; set; }       // InFlight, Done, Cancelled, Error
    public DateTime CreatedAt { get; init; }       // Prompt creation timestamp
    public int TokenCount { get; set; }            // Total tokens generated (updated on done)
}

public enum PromptStatus
{
    InFlight,
    Done,
    Cancelled,
    Error
}
```

**Storage**: In-memory for MVP (nested under Session)  
**Future**: Persist for prompt history and replay

**Validation Rules**:
- `PromptId` MUST be globally unique
- `SessionId` MUST reference existing active session
- `Text` MUST NOT be empty
- `IdempotencyKey` uniqueness enforced within session (duplicate key → return cached result)

**State Transitions**:
```
InFlight → Done       (successful completion)
InFlight → Cancelled  (client cancel request)
InFlight → Error      (provider failure, timeout)
```

### GenerationDelta (Event)

Represents a single streaming token delta event emitted to clients.

**Properties**:
```csharp
public class GenerationDelta
{
    public string PromptId { get; init; }          // Parent prompt identifier
    public string Type { get; init; }              // "token" | "done" | "error"
    public int Seq { get; set; }                   // Monotonic sequence number (0-based)
    public string? Text { get; init; }             // Token delta text (nullable for done/error)
    public string Role { get; init; }              // "assistant" | "system" | "user"
    public object? Metadata { get; init; }         // Optional: provider-specific data, error details
}
```

**JSON Schema** (see `contracts/generation-delta.schema.json`):
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["promptId", "type", "seq", "role"],
  "properties": {
    "promptId": { "type": "string", "format": "uuid" },
    "type": { "type": "string", "enum": ["token", "done", "error"] },
    "seq": { "type": "integer", "minimum": 0 },
    "text": { "type": "string" },
    "role": { "type": "string", "enum": ["assistant", "system", "user"] },
    "metadata": { "type": "object" }
  }
}
```

**Event Types**:
- **token**: Incremental text delta from provider (primary event type)
- **done**: Final event indicating generation completion (may include summary metadata)
- **error**: Error event with structured code and message in `Metadata`

**Validation Rules**:
- `PromptId` MUST match an active prompt
- `Seq` MUST be monotonically increasing per prompt (no gaps)
- `Type="token"` MUST include non-null `Text`
- `Type="error"` MUST include `Metadata` with `{ "code": "...", "message": "..." }`
- `Role` defaults to "assistant" for token events

**Example Event Sequence**:
```json
{"promptId":"abc-123","type":"token","seq":0,"text":"This ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":1,"text":"is ","role":"assistant"}
{"promptId":"abc-123","type":"token","seq":2,"text":"a test.","role":"assistant"}
{"promptId":"abc-123","type":"done","seq":3,"role":"assistant","metadata":{"tokenCount":3}}
```

**Error Event Example**:
```json
{
  "promptId": "abc-123",
  "type": "error",
  "seq": 5,
  "role": "system",
  "metadata": {
    "code": "provider_timeout",
    "message": "Provider stalled for 30s with no tokens"
  }
}
```

### Document (Existing)

Reuses existing `DocumentEntity` from `DeepWiki.Data` for RAG retrieval context.

**Properties** (reference only, not modified in this feature):
```csharp
public class DocumentEntity
{
    public Guid Id { get; set; }
    public string RepoUrl { get; set; }
    public string FilePath { get; set; }
    public string Title { get; set; }
    public string Text { get; set; }
    public ReadOnlyMemory<float> Embedding { get; set; }
    public string MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Usage**: `IVectorStore.QueryAsync` retrieves top-k documents by embedding similarity; text included in generation context.

## DTOs (Request/Response)

### SessionRequest

Client request to create a new session.

**Properties**:
```csharp
public class SessionRequest
{
    public string? Owner { get; init; }            // Optional: org/repo identifier
    public Dictionary<string, string>? Context { get; init; }  // Optional: session metadata
}
```

**Response**: `SessionResponse` with `SessionId`

```csharp
public class SessionResponse
{
    public string SessionId { get; init; }
}
```

**Example**:
```json
// Request
POST /api/generation/session
{ "owner": "internal-dev", "context": { "environment": "dev" } }

// Response
{ "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479" }
```

### PromptRequest

Client request to submit a prompt within a session.

**Properties**:
```csharp
public class PromptRequest
{
    public string SessionId { get; init; }         // Target session ID
    public string Prompt { get; init; }            // User prompt text
    public string? IdempotencyKey { get; init; }   // Optional: retry-safe key
    public int TopK { get; init; } = 5;            // Number of documents to retrieve (default 5)
    public Dictionary<string, string>? Filters { get; init; }  // Optional: retrieval filters
}
```

**Validation Rules**:
- `SessionId` MUST reference active session (400 if not found)
- `Prompt` MUST NOT be empty (400 if invalid)
- `TopK` MUST be 1-20 (validation range)

**Example**:
```json
POST /api/generation/stream
{
  "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "prompt": "Explain vector embeddings in C#",
  "idempotencyKey": "client-req-001",
  "topK": 10,
  "filters": { "repoUrl": "https://github.com/example/repo" }
}
```

### CancelRequest

Client request to cancel an in-flight prompt.

**Properties**:
```csharp
public class CancelRequest
{
    public string SessionId { get; init; }
    public string PromptId { get; init; }
}
```

**Response**: `204 No Content` on success, `404` if prompt not found or already completed

**Example**:
```json
POST /api/generation/cancel
{
  "sessionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "promptId": "abc-123"
}
```

## Relationships

```
Session (1) ──< (N) Prompt
           └──< (N) GenerationDelta (via PromptId)

Prompt (1) ──< (N) GenerationDelta
       └── references Document (N) via RAG retrieval
```

**Lifecycle Flow**:
1. Client creates `Session` → receives `SessionId`
2. Client submits `PromptRequest` → receives `PromptId` (implicit in first delta)
3. Server retrieves documents via `IVectorStore.QueryAsync`
4. Server streams `GenerationDelta` events (type=token)
5. Server emits final delta (type=done or error)
6. Prompt status updated to `Done`/`Cancelled`/`Error`

## Agent Framework Compatibility

All DTOs are JSON-serializable and designed for Agent Framework tool binding:

**Tool Binding Example**:
```csharp
[AgentTool("queryKnowledge")]
public async Task<string> QueryKnowledgeAsync(string query, int topK = 5)
{
    var request = new PromptRequest
    {
        SessionId = agentContext.SessionId,
        Prompt = query,
        TopK = topK
    };
    
    var result = new StringBuilder();
    await foreach (var delta in generationService.GenerateAsync(request))
    {
        if (delta.Type == "token") result.Append(delta.Text);
    }
    return result.ToString();
}
```

**Error Handling Pattern** (agent-recoverable):
```csharp
try
{
    await foreach (var delta in generationService.GenerateAsync(request, ct))
    {
        if (delta.Type == "error")
        {
            var error = JsonSerializer.Deserialize<ErrorMetadata>(
                JsonSerializer.Serialize(delta.Metadata));
            throw new AgentRecoverableException(error.Message, error.Code);
        }
        yield return delta;
    }
}
catch (OperationCanceledException)
{
    // Agent can handle graceful cancellation
    yield return new GenerationDelta { Type = "done", ... };
}
```

## Storage Design (MVP)

### In-Memory Storage
```csharp
public class SessionManager
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Prompt> _prompts = new();
    
    public Session CreateSession(SessionRequest request)
    {
        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            Owner = request.Owner,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            Status = SessionStatus.Active
        };
        _sessions[session.SessionId] = session;
        return session;
    }
    
    public Prompt CreatePrompt(string sessionId, PromptRequest request)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException("Session not found");
        
        var prompt = new Prompt
        {
            PromptId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Text = request.Prompt,
            IdempotencyKey = request.IdempotencyKey,
            Status = PromptStatus.InFlight,
            CreatedAt = DateTime.UtcNow
        };
        _prompts[prompt.PromptId] = prompt;
        session.LastActiveAt = DateTime.UtcNow;
        return prompt;
    }
}
```

### Future Persistence (Phase 2)
- Store sessions in `Sessions` table (EF Core entity)
- Store prompts in `Prompts` table with FK to `SessionId`
- Store generation deltas in `GenerationDeltas` table for replay/audit (optional)
- Add indexes on `SessionId`, `PromptId`, `CreatedAt` for queries

## Validation Summary

| Entity | Key Validations |
|--------|----------------|
| **Session** | SessionId unique, CreatedAt UTC, Status transitions valid |
| **Prompt** | PromptId unique, SessionId exists, Text non-empty, IdempotencyKey unique in session |
| **GenerationDelta** | PromptId exists, Seq monotonic, Type enum valid, Error has metadata |
| **SessionRequest** | Owner optional, Context JSON-serializable |
| **PromptRequest** | SessionId exists, Prompt non-empty, TopK in range [1, 20] |

## Next Steps

- Define OpenAPI schema (see `contracts/generation-service.yaml`)
- Define JSON schema for `GenerationDelta` (see `contracts/generation-delta.schema.json`)
- Implement data validation in `GenerationController` and `SessionManager`
- Write unit tests for entity validation and state transitions

---

**Phase 1 Data Model Complete**: Proceed to contract definitions and quickstart documentation.
