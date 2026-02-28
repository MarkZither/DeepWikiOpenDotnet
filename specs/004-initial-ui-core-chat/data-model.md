# Data Model: Initial UI with Core Chat and Document Query

**Feature**: 004-initial-ui-core-chat  
**Date**: February 16, 2026  
**Purpose**: Define UI models, state structures, and API contracts for chat interface

---

## Overview

This feature uses **client-side only** data models. No database entities or backend models are created. Models represent in-memory state for the Blazor UI and DTOs for API communication.

---

## UI Models (Client-Side)

### ChatMessageModel

Represents a single message in the conversation (user or AI).

```csharp
namespace deepwiki_open_dotnet.Web.Models;

public class ChatMessageModel
{
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// Message sender role.
    /// </summary>
    public MessageRole Role { get; init; }
    
    /// <summary>
    /// Message text content (supports Markdown formatting).
    /// </summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp when message was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Indicates if message is still streaming (incomplete).
    /// </summary>
    public bool IsStreaming { get; set; }
    
    /// <summary>
    /// Source citations for AI responses (populated after streaming completes).
    /// </summary>
    public List<SourceCitation> Sources { get; init; } = new();
    
    /// <summary>
    /// Error message if generation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

public enum MessageRole
{
    User,
    Assistant,
    System
}
```

**Lifecycle**:
1. User message: Created immediately on submit with `Role=User`, `Text=userInput`
2. AI message: Created with `Role=Assistant`, `Text=""`, `IsStreaming=true`
3. During streaming: `Text` appended with each token
4. On completion: `IsStreaming=false`, `Sources` populated from metadata

---

### SourceCitation

Represents a document source referenced in an AI response.

```csharp
public class SourceCitation
{
    /// <summary>
    /// Document title or filename.
    /// </summary>
    public string Title { get; init; } = string.Empty;
    
    /// <summary>
    /// Repository URL (optional).
    /// </summary>
    public string? RepoUrl { get; init; }
    
    /// <summary>
    /// File path within repository.
    /// </summary>
    public string? FilePath { get; init; }
    
    /// <summary>
    /// Relevant text excerpt from the document.
    /// </summary>
    public string? Excerpt { get; init; }
    
    /// <summary>
    /// Link to full document (if available).
    /// </summary>
    public string? Url { get; init; }
    
    /// <summary>
    /// Similarity score (0-1).
    /// </summary>
    public float Score { get; init; }
}
```

---

### DocumentCollectionModel

Represents a selectable document collection/scope.

```csharp
public class DocumentCollectionModel
{
    /// <summary>
    /// Collection identifier (e.g., repo URL or collection name).
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// Display name for the collection.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Number of documents in collection.
    /// </summary>
    public int DocumentCount { get; init; }
    
    /// <summary>
    /// Whether this collection is currently selected.
    /// </summary>
    public bool IsSelected { get; set; }
}
```

---

### ChatSessionState

Represents the current chat session state (session-scoped service).

```csharp
public class ChatSessionState
{
    /// <summary>
    /// API session ID (obtained from /api/generation/session).
    /// </summary>
    public Guid SessionId { get; private set; }
    
    /// <summary>
    /// All messages in the conversation (chronological order).
    /// </summary>
    private readonly List<ChatMessageModel> _messages = new();
    public IReadOnlyList<ChatMessageModel> Messages => _messages;
    
    /// <summary>
    /// Currently selected document collections (empty = all documents).
    /// </summary>
    public HashSet<string> SelectedCollectionIds { get; } = new();
    
    /// <summary>
    /// Available document collections.
    /// </summary>
    public List<DocumentCollectionModel> AvailableCollections { get; } = new();
    
    /// <summary>
    /// Indicates if a generation is currently in progress.
    /// </summary>
    public bool IsGenerating { get; set; }
    
    /// <summary>
    /// Cancellation token source for current generation.
    /// </summary>
    public CancellationTokenSource? CurrentGenerationCts { get; set; }
    
    public event Action? OnStateChanged;
    
    public void AddMessage(ChatMessageModel message)
    {
        _messages.Add(message);
        OnStateChanged?.Invoke();
    }
    
    public void UpdateLastMessage(Action<ChatMessageModel> update)
    {
        if (_messages.Count > 0)
        {
            update(_messages[^1]);
            OnStateChanged?.Invoke();
        }
    }
    
    public void ClearMessages()
    {
        _messages.Clear();
        OnStateChanged?.Invoke();
    }
    
    public void SetSessionId(Guid sessionId)
    {
        SessionId = sessionId;
    }
}
```

---

## API DTOs (Communication with Backend)

### GenerationDeltaDto

Represents a single streaming token from `/api/generation/stream`.

```csharp
namespace deepwiki_open_dotnet.Web.Models;

using System.Text.Json.Serialization;

public class GenerationDeltaDto
{
    [JsonPropertyName("promptId")]
    public string? PromptId { get; init; }
    
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "token", "done", "error"
    
    [JsonPropertyName("seq")]
    public int Sequence { get; init; }
    
    [JsonPropertyName("text")]
    public string? Text { get; init; }
    
    [JsonPropertyName("role")]
    public string? Role { get; init; } // "assistant"
    
    [JsonPropertyName("done")]
    public bool? Done { get; init; }
    
    [JsonPropertyName("metadata")]
    public GenerationMetadataDto? Metadata { get; init; }
    
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public class GenerationMetadataDto
{
    [JsonPropertyName("sources")]
    public List<SourceDocumentDto>? Sources { get; init; }
    
    [JsonPropertyName("retrievalTimeMs")]
    public double? RetrievalTimeMs { get; init; }
    
    [JsonPropertyName("totalTokens")]
    public int? TotalTokens { get; init; }
}

public class SourceDocumentDto
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    
    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; init; }
    
    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }
    
    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; init; }
    
    [JsonPropertyName("score")]
    public float Score { get; init; }
}
```

---

### SessionRequestDto / SessionResponseDto

For creating a new chat session.

```csharp
public class SessionRequestDto
{
    [JsonPropertyName("owner")]
    public string? Owner { get; init; } // Optional identifier for session
}

public class SessionResponseDto
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }
    
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
    
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }
}
```

---

### GenerationRequestDto

Request body for `/api/generation/stream`.

```csharp
public class GenerationRequestDto
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }
    
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;
    
    [JsonPropertyName("topK")]
    public int TopK { get; init; } = 10;
    
    [JsonPropertyName("filters")]
    public Dictionary<string, string>? Filters { get; init; }
    
    [JsonPropertyName("context")]
    public List<ContextMessageDto>? Context { get; init; } // For multi-turn context
}

public class ContextMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty; // "user" or "assistant"
    
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}
```

---

### DocumentListResponseDto

Response from `/api/documents` for collection listing.

```csharp
public class DocumentListResponseDto
{
    [JsonPropertyName("items")]
    public List<DocumentSummaryDto> Items { get; init; } = new();
    
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }
    
    [JsonPropertyName("page")]
    public int Page { get; init; }
    
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }
}

public class DocumentSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
    
    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; init; }
    
    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }
    
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}
```

---

## State Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Page Load (Chat.razor)                   │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────┐
            │   ChatStateService (Scoped)   │
            │   - Initialize session        │
            │   - Load collections          │
            └───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────┐
            │   ChatApiClient.CreateSession │
            │   POST /api/generation/session│
            └───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────┐
            │   Store SessionId in state    │
            └───────────────────────────────┘
                            │
                ┌───────────┴───────────┐
                │                       │
                ▼                       ▼
    ┌────────────────────┐  ┌──────────────────────┐
    │  User types message│  │Load collections:     │
    │  Clicks submit     │  │GET /api/documents    │
    └────────────────────┘  └──────────────────────┘
                │                       │
                ▼                       ▼
    ┌────────────────────────────────────────────┐
    │   Add user message to state.Messages       │
    │   Create AI message (IsStreaming=true)     │
    └────────────────────────────────────────────┘
                            │
                            ▼
    ┌────────────────────────────────────────────┐
    │   Build GenerationRequestDto               │
    │   - sessionId, prompt, topK                │
    │   - context (previous messages)            │
    │   - filters (selected collections)         │
    └────────────────────────────────────────────┘
                            │
                            ▼
    ┌────────────────────────────────────────────┐
    │   ChatApiClient.StreamGenerationAsync      │
    │   POST /api/generation/stream              │
    └────────────────────────────────────────────┘
                            │
                ┌───────────┴───────────┐
                │   NDJSON Stream       │
                └───────────────────────┘
                            │
            ┌───────────────┼───────────────┐
            │               │               │
            ▼               ▼               ▼
    ┌─────────────┐ ┌────────────┐ ┌─────────────┐
    │type="token" │ │type="token"│ │type="done"  │
    │seq=0        │ │seq=1       │ │seq=2        │
    │text="Hi"    │ │text=" ..."  │ │metadata=... │
    └─────────────┘ └────────────┘ └─────────────┘
            │               │               │
            └───────────────┼───────────────┘
                            ▼
            ┌───────────────────────────────┐
            │   Append text to AI message   │
            │   state.UpdateLastMessage()   │
            │   StateHasChanged()           │
            └───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────┐
            │   On done: Extract sources    │
            │   Set IsStreaming=false       │
            │   Populate message.Sources    │
            └───────────────────────────────┘
```

---

## Relationships

```
ChatSessionState (1)
    ├── Messages (N) ChatMessageModel
    │       └── Sources (N) SourceCitation
    ├── AvailableCollections (N) DocumentCollectionModel
    └── SelectedCollectionIds (N) string

ChatApiClient
    ├── Uses → GenerationRequestDto
    ├── Returns → IAsyncEnumerable<GenerationDeltaDto>
    └── Uses → SessionRequestDto/ResponseDto
```

---

## Mapping Logic

### DTO → UI Model (Sources)

```csharp
private List<SourceCitation> MapSources(List<SourceDocumentDto>? dtoSources)
{
    if (dtoSources == null) return new();
    
    return dtoSources.Select(dto => new SourceCitation
    {
        Title = dto.Title ?? "Unknown",
        RepoUrl = dto.RepoUrl,
        FilePath = dto.FilePath,
        Excerpt = dto.Excerpt,
        Score = dto.Score,
        Url = dto.RepoUrl != null && dto.FilePath != null 
            ? $"/documents?repo={Uri.EscapeDataString(dto.RepoUrl)}&path={Uri.EscapeDataString(dto.FilePath)}"
            : null
    }).ToList();
}
```

### UI Messages → API Context

```csharp
private List<ContextMessageDto> BuildContext(IEnumerable<ChatMessageModel> messages)
{
    return messages
        .Where(m => m.Role != MessageRole.System && !m.IsStreaming)
        .Select(m => new ContextMessageDto
        {
            Role = m.Role == MessageRole.User ? "user" : "assistant",
            Content = m.Text
        })
        .ToList();
}
```

---

## Validation Rules

### ChatMessageModel
- `Text` must not be empty for user messages
- `Role` must be valid enum value
- `Text` maximum length: 10,000 characters (UI validation)

### GenerationRequestDto
- `Prompt` must not be empty or whitespace
- `TopK` range: 1-100
- `SessionId` must be valid GUID

### DocumentCollectionModel
- `Id` must be unique within `AvailableCollections`
- `Name` must not be empty

---

## Summary

All data models are **client-side only** with no database persistence. State is scoped to the Blazor circuit lifetime (browser tab session). Models follow DTO pattern for API communication with clear separation between UI models and API contracts.
