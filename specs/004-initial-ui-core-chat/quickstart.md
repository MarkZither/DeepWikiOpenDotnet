# Quickstart: Initial UI with Core Chat and Document Query

**Feature**: 004-initial-ui-core-chat  
**Date**: February 16, 2026  
**Purpose**: Quick guide to using the chat interface

---

## Overview

The DeepWiki chat interface provides interactive AI conversations grounded in document knowledge. Ask questions and receive intelligent responses with source citations.

---

## Prerequisites

- DeepWiki Web application running (default: `https://localhost:5001`)
- API service running with `/api/generation/stream` endpoint available
- Documents indexed in vector store

**Quick Check**:
```bash
# Verify services are running
curl https://localhost:5000/health  # API service
curl https://localhost:5001/        # Web UI
```

---

## Using the Chat Interface

### Step 1: Navigate to Chat

1. Open browser to `https://localhost:5001`
2. Click **Chat** in the navigation menu
3. Chat page loads with empty conversation history

### Step 2: Ask a Question

1. Type your question in the input field at the bottom
2. Click the send icon or press **Enter**
3. Your message appears in the chat history
4. Loading indicator shows while AI processes your query

**Example**:
```
User: What is Entity Framework Core?
```

### Step 3: View AI Response

1. AI response streams in real-time (token by token)
2. Response maintains formatting (paragraphs, lists, code blocks)
3. Source citations appear below the response (if applicable)

**Example Response**:
```
Assistant: Entity Framework Core is an object-relational mapper (ORM) 
that enables .NET developers to work with databases using .NET objects...

Sources:
[1] ef-core-documentation.md - "Entity Framework Core is a lightweight..."
[2] microsoft-docs.md - "EF Core supports multiple database providers..."
```

### Step 4: Continue Conversation

1. Ask follow-up questions
2. AI maintains context from previous messages
3. Scroll up to view conversation history

**Example Follow-up**:
```
User: How do I configure it for PostgreSQL?
Assistant: To configure EF Core for PostgreSQL, install the Npgsql...

---

## Additional Features

### Selecting Document Scope

1. Click the **Document Scope** dropdown at the top
2. Select specific repositories or document collections
3. AI will only search within selected scope
4. Default: All available documents (shown as "All Documents")

### Clearing Chat History

1. Click the **Clear Chat** button in the top right
2. Conversation history is removed
3. Start a fresh conversation

---

## Keyboard Shortcuts

- **Enter**: Send message
- **Shift + Enter**: New line in message input
- **Esc**: Clear current input

---

## Troubleshooting

### No Response from AI

**Symptoms**: Message sent but no AI response appears

**Causes**:
- API service unavailable
- Vector store empty (no documents indexed)
- LLM provider (Ollama) not running

**Resolution**:
```bash
# Check API health
curl https://localhost:5000/health

# Check Ollama
curl http://localhost:11434/api/tags

# View API logs
docker logs deepwiki-apiservice
```

### Streaming Stops Mid-Response

**Symptoms**: AI response starts but stops before completion

**Causes**:
- Network interruption
- Browser tab backgrounded (circuit suspended)
- API timeout

**Resolution**:
- Ask the question again
- Check browser console for errors  
- Verify stable network connection

### Citations Not Showing

**Symptoms**: AI responds but no source citations appear

**Causes**:
- Query did not use document retrieval
- No relevant documents found
- Documents lack proper metadata

**Verification**:
- Check if document scope is selected
- Verify documents are indexed: `GET /api/documents`
- Review API response for metadata

---

## Development Notes

### Adding the Chat Link to Navigation

In `NavMenu.razor`, add:

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="chat">
        <span class="bi bi-chat-dots-fill" aria-hidden="true"></span> Chat
    </NavLink>
</div>
```

### Testing the Interface

```bash
# Run web application
cd src/deepwiki-open-dotnet.Web
dotnet run

# In browser
open https://localhost:5001/chat
```

---

## Next Steps

- Phase 2: Add SignalR Hub support for enhanced streaming
- Phase 2: WCAG 2.1 AA accessibility improvements
- Phase 2: Localization/i18n support

