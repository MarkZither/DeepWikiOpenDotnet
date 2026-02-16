# Research: Initial UI with Core Chat and Document Query

**Feature**: 004-initial-ui-core-chat  
**Date**: February 16, 2026  
**Purpose**: Research NDJSON streaming patterns, MudBlazor chat UX, and Blazor InteractiveServer state management

---

## Decision: HTTP NDJSON vs SignalR for Initial Implementation

**Choice**: HTTP NDJSON streaming via `/api/generation/stream`

### Rationale

1. **Simplicity**: Standard HTTP with `HttpClient.ReadAsStreamAsync()` - no additional infrastructure
2. **Existing Pattern**: Matches `WeatherApiClient` pattern already in codebase
3. **Proven API**: `/api/generation/stream` already tested and working
4. **Upgrade Path**: SignalR Hub exists and can be swapped in later without changing UI contracts

### Alternatives Considered

- **SignalR Hub (`GenerationHub`)**: More features (bidirectional, reconnection), but adds complexity for MVP. Deferred to enhancement phase.
- **Server-Sent Events (SSE)**: Not natively supported in Blazor, would require custom implementation

---

## NDJSON Streaming Pattern

### API Contract (Existing)

**Endpoint**: `POST /api/generation/stream`

**Request**:
```json
{
  "sessionId": "uuid",
  "prompt": "user question",
  "topK": 10,
  "filters": { "repoUrl": "optional" }
}
```

**Response**: NDJSON stream (Content-Type: `application/x-ndjson`)
```json
{"promptId":"abc","type":"token","seq":0,"text":"Hello ","role":"assistant"}
{"promptId":"abc","type":"token","seq":1,"text":"world","role":"assistant"}
{"promptId":"abc","type":"done","seq":2,"done":true}
```

### Implementation Pattern

**C# Streaming**:
```csharp
using var response = await _httpClient.PostAsync(url, content);
using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line)) continue;
    
    var delta = JsonSerializer.Deserialize<GenerationDelta>(line);
    await OnTokenReceived(delta); // Update UI
}
```

**Key Considerations**:
- Use `StreamReader.ReadLineAsync()` for line-by-line parsing
- Handle empty lines gracefully
- Parse each line as independent JSON object
- Update UI via `StateHasChanged()` on each token
- Cancel via `CancellationTokenSource` when user navigates away

---

## MudBlazor Chat UX Patterns

### Recommended Components

**MudTextField** (input):
- `Variant="Outlined"`
- `Adornment="Adornment.End"`
- `AdornmentIcon="@Icons.Material.Filled.Send"`
- `OnAdornmentClick` for submit
- `Disabled` during streaming (FR-018)

**MudPaper** (message bubbles):
- User messages: `Class="mud-theme-primary pa-3 mb-2"`
- AI messages: `Class="mud-theme-secondary pa-3 mb-2"`
- Auto-scroll to bottom on new message

**MudProgressLinear** (loading indicator):
- Indeterminate progress during streaming
- Hide when `type == "done"`

**MudAutocomplete** (document scope):
- Populated via `/api/documents` endpoint
- Multi-select for collections
- Optional (default to all documents per FR-016)

### Layout Structure

```razor
<MudContainer MaxWidth="MaxWidth.Large">
    <MudPaper Class="pa-4" Elevation="2">
        <DocumentScopeSelector @bind-SelectedCollections="@_selectedCollections" />
    </MudPaper>
    
    <MudPaper Class="chat-history pa-4 mt-4" Style="height: 60vh; overflow-y: auto;" @ref="_chatScroll">
        @foreach (var msg in _messages)
        {
            <ChatMessage Message="@msg" />
        }
        @if (_isStreaming)
        {
            <MudProgressLinear Indeterminate="true" />
        }
    </MudPaper>
    
    <ChatInput OnSubmit="HandleSubmit" Disabled="@_isStreaming" />
</MudContainer>
```

---

## Blazor InteractiveServer State Management

### Session Scope

**Services Registration**:
```csharp
// Program.cs
builder.Services.AddScoped<ChatStateService>();
builder.Services.AddScoped<ChatApiClient>();
```

**Lifetime**: Scoped to SignalR circuit (browser tab)
- State lives for duration of tab/browser session
- Disposed when circuit disconnects
- No persistence across page refreshes (per FR-005)

### State Service Pattern

```csharp
public class ChatStateService
{
    private readonly List<ChatMessageModel> _messages = new();
    public IReadOnlyList<ChatMessageModel> Messages => _messages;
    
    public event Action? OnMessagesChanged;
    
    public void AddMessage(ChatMessageModel message)
    {
        _messages.Add(message);
        OnMessagesChanged?.Invoke();
    }
    
    public void Clear()
    {
        _messages.Clear();
        OnMessagesChanged?.Invoke();
    }
}
```

**Component Subscription**:
```csharp
@code {
    [Inject] ChatStateService ChatState { get; set; }
    
    protected override void OnInitialized()
    {
        ChatState.OnMessagesChanged += StateHasChanged;
    }
    
    public void Dispose()
    {
        ChatState.OnMessagesChanged -= StateHasChanged;
    }
}
```

---

## Auto-Scroll Implementation

### Approach

**ElementReference + JSInterop**:
```csharp
private ElementReference _chatScroll;

private async Task ScrollToBottom()
{
    await JS.InvokeVoidAsync("scrollToBottom", _chatScroll);
}
```

**JavaScript** (wwwroot/js/chat.js):
```javascript
window.scrollToBottom = (element) => {
    element.scrollTop = element.scrollHeight;
};
```

**Trigger**: Call after each token appended during streaming

**Alternative**: MudBlazor `<MudScrollToBottom>` component (if available in v7.x)

---

## Markdown Rendering for AI Responses

### Decision: Use Markdig

**Rationale**:
- Industry standard for .NET Markdown parsing
- Supports GitHub-flavored markdown
- Handles code blocks with syntax highlighting
- Extensible for citations

**Implementation**:
```csharp
@inject MarkdownPipeline MarkdownPipeline

<div class="markdown-content">
    @((MarkupString)Markdown.ToHtml(message.Text, MarkdownPipeline))
</div>
```

**Configuration**:
```csharp
// Program.cs
builder.Services.AddSingleton(new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build());
```

---

## Citation Display Pattern

### Inline Citations

**API Response** includes citations in metadata:
```json
{
  "type": "done",
  "metadata": {
    "sources": [
      {"title": "doc.md", "excerpt": "...", "url": "/documents/123"}
    ]
  }
}
```

**UI Pattern**:
- Superscript numbers inline: `[1]`, `[2]`
- Expandable section at message bottom with full citations
- Click citation to expand document excerpt

**MudBlazor**:
```razor
<MudExpansionPanels>
    <MudExpansionPanel Text="Sources (@sources.Count)">
        @foreach (var src in sources)
        {
            <MudText Typo="Typo.body2">@src.Title</MudText>
            <MudText Typo="Typo.caption">@src.Excerpt</MudText>
        }
    </MudExpansionPanel>
</MudExpansionPanels>
```

---

## Error Handling Strategy

### API Errors

**HTTP Status Codes**:
- 503: Embedding service unavailable → "AI service temporarily unavailable"
- 429: Rate limited → "Too many requests, please wait"
- 400: Invalid request → Display validation message
- 500: Server error → "Something went wrong, please try again"

**Implementation**:
```csharp
try
{
    await StreamGenerationAsync(prompt);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
{
    await ShowError("AI service is temporarily unavailable. Please try again later.");
}
catch (Exception ex)
{
    _logger.LogError(ex, "Generation failed");
    await ShowError("An error occurred. Please try again.");
}
```

### Streaming Interruption

**Scenarios**:
- Network disconnect during stream
- User navigates away mid-stream
- Circuit disconnect

**Handling**:
- Use `CancellationToken` tied to component lifetime
- Display partial response with "(Interrupted)" indicator
- Allow retry with "Continue" button

---

## Performance Optimizations

### Token Batching

**Problem**: Rendering every token causes excessive DOM updates

**Solution**: Batch tokens into 50ms windows
```csharp
private readonly StringBuilder _tokenBuffer = new();
private Timer? _flushTimer;

private void OnTokenReceived(string text)
{
    _tokenBuffer.Append(text);
    _flushTimer?.Dispose();
    _flushTimer = new Timer(_ =>
    {
        InvokeAsync(() =>
        {
            _currentMessage.Text += _tokenBuffer.ToString();
            _tokenBuffer.Clear();
            StateHasChanged();
        });
    }, null, 50, Timeout.Infinite);
}
```

### Virtual Scrolling

**Deferred to Phase 2**: Only needed if >50 messages causes performance issues (SC-002 says 50 messages should work fine)

---

## Summary

### Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Streaming Transport | HTTP NDJSON | Simpler than SignalR, matches existing pattern |
| UI Library | MudBlazor 7.x | Already chosen by user, comprehensive components |
| Render Mode | InteractiveServer | Server-side state, good for streaming |
| State Management | Scoped service | Aligns with Blazor circuit lifetime |
| Markdown | Markdig | Industry standard, extensible |
| Auto-scroll | JSInterop | Reliable cross-browser |
| Token Batching | 50ms windows | Reduces DOM thrash |

### Open Questions

- ✅ Resolved: No persistence across sessions (clarification confirmed)
- ✅ Resolved: Block input during streaming (clarification confirmed)
- ✅ Resolved: Stream with auto-scroll (clarification confirmed)
