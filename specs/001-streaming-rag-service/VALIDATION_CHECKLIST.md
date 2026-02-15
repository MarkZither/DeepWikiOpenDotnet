# Quickstart Scenario Validation Checklist

**Date**: 2026-02-13  
**Feature**: MCP Streaming RAG Service  
**Validation Type**: Manual E2E Testing

This checklist validates all scenarios documented in [quickstart.md](../quickstart.md) against acceptance criteria in [spec.md](spec.md).

---

## Prerequisites

- [ ] Ollama service running on http://localhost:11434 (or configured endpoint)
- [ ] Model downloaded: `ollama pull vicuna-13b` (or configured model)
- [ ] PostgreSQL or SQL Server running with vector store configured
- [ ] API service running: `dotnet run --project src/deepwiki-open-dotnet.AppHost`
- [ ] Sample documents ingested into vector store (at least 10 documents)
- [ ] curl, jq, and bash available for testing

---

## Scenario 1: Basic Session Creation

**Acceptance Criteria**: SC-004 (session lifecycle), HTTP 201 Created

### Steps

1. **Create session via curl**:
   ```bash
   SESSION_ID=$(curl -s -X POST http://localhost:5000/api/generation/session \
     -H "Content-Type: application/json" \
     -d '{"owner": "test-user"}' | jq -r '.sessionId')
   echo "Session ID: $SESSION_ID"
   ```

### Validation

- [ ] HTTP 201 Created response
- [ ] Response contains valid UUID `sessionId`
- [ ] Response contains `createdAt` timestamp
- [ ] Response contains `expiresAt` (1 hour from now)
- [ ] Session stored in server (check logs)

---

## Scenario 2: Streaming Generation (HTTP NDJSON)

**Acceptance Criteria**: SC-001 (TTF <500ms), SC-003 (sequence integrity), SC-005 (NDJSON format)

### Steps

1. **Submit prompt and stream tokens**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Explain vector embeddings in C#\", \"topK\": 5}" \
     --no-buffer | tee output.ndjson
   ```

2. **Parse NDJSON with jq**:
   ```bash
   cat output.ndjson | jq -c 'select(.type == "token") | .text' | tr -d '\n'
   ```

### Validation

- [ ] **TTF <500ms**: First token received within 500ms (check timestamp)
- [ ] Content-Type: `application/x-ndjson`
- [ ] Each line is valid JSON (validate with `jq`)
- [ ] Sequence numbers start at 0 and increment without gaps
- [ ] Token deltas contain `.type == "token"`, `.text`, `.seq`, `.role == "assistant"`
- [ ] Final delta has `.type == "done"` with `.seq` as last sequence number
- [ ] No duplicate text chunks in consecutive tokens
- [ ] RAG context included (verify retrieved documents influenced response)

---

## Scenario 3: Cancellation

**Acceptance Criteria**: SC-002 (cancellation <200ms)

### Steps

1. **Start long-running generation** (in background):
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Write a 1000 line implementation of...\", \"topK\": 5}" \
     --no-buffer > long_output.ndjson &
   CURL_PID=$!
   ```

2. **Extract promptId from first delta**:
   ```bash
   sleep 0.2
   PROMPT_ID=$(head -1 long_output.ndjson | jq -r '.promptId')
   ```

3. **Send cancel request**:
   ```bash
   START=$(date +%s%3N)
   curl -X POST http://localhost:5000/api/generation/cancel \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"promptId\": \"$PROMPT_ID\"}"
   END=$(date +%s%3N)
   LATENCY=$((END - START))
   echo "Cancellation latency: ${LATENCY}ms"
   ```

4. **Wait for curl to finish and check output**:
   ```bash
   wait $CURL_PID
   tail -5 long_output.ndjson | jq .
   ```

### Validation

- [ ] **Cancellation latency <200ms**: Time from cancel request to response
- [ ] HTTP 200 OK from cancel endpoint
- [ ] No further token deltas emitted after cancel (check output file)
- [ ] Final delta has `.type == "done"` or `.type == "error"`
- [ ] Final delta metadata indicates cancellation (if applicable)
- [ ] Prompt status updated to `Cancelled` (check health endpoint or logs)

---

## Scenario 4: Rate Limiting

**Acceptance Criteria**: SC-006 (100 req/min per IP)

### Steps

1. **Rapid-fire requests** (>100 in 60 seconds):
   ```bash
   for i in {1..105}; do
     curl -s -X POST http://localhost:5000/api/generation/session \
       -H "Content-Type: application/json" \
       -d '{"owner": "load-test"}' \
       -w "\\nStatus: %{http_code}\\n" &
   done
   wait
   ```

2. **Check for 429 responses**:
   ```bash
   grep "Status: 429" <output_file>
   ```

### Validation

- [ ] First ~100 requests succeed (HTTP 201)
- [ ] Subsequent requests return HTTP 429 Too Many Requests
- [ ] Response headers include:
   - `X-RateLimit-Limit: 100`
   - `X-RateLimit-Remaining: <count>`
   - `Retry-After: <seconds>`
- [ ] After waiting `Retry-After` seconds, requests succeed again
- [ ] Legitimate traffic not blocked (intermittent requests work)

---

## Scenario 5: Error Handling (Invalid Input)

**Acceptance Criteria**: Structured error responses, HTTP 400

### Steps

1. **Empty prompt**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"\"}"
   ```

2. **Invalid sessionId**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d '{"sessionId": "invalid-uuid", "prompt": "test"}'
   ```

3. **Out-of-range topK**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"test\", \"topK\": 50}"
   ```

### Validation

- [ ] Empty prompt: HTTP 400, error message indicates "Prompt cannot be empty"
- [ ] Invalid sessionId: HTTP 404 or 400, error message indicates session not found
- [ ] Out-of-range topK: HTTP 400, error message indicates "TopK must be between 1 and 20"
- [ ] All error responses have consistent structure:
  ```json
  {
    "type": "error",
    "error": {
      "code": "validation_error",
      "message": "..."
    }
  }
  ```

---

## Scenario 6: Idempotency

**Acceptance Criteria**: Duplicate requests with same idempotency key return cached response

### Steps

1. **First request with idempotency key**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Hello\", \"idempotencyKey\": \"req-001\"}" \
     --no-buffer > first.ndjson
   ```

2. **Duplicate request (same key)**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Hello\", \"idempotencyKey\": \"req-001\"}" \
     --no-buffer > second.ndjson
   ```

3. **Compare responses**:
   ```bash
   diff first.ndjson second.ndjson
   ```

### Validation

- [ ] Both requests succeed (HTTP 200)
- [ ] Response content is identical (diff shows no differences)
- [ ] Second request completes faster (cached, no generation)
- [ ] Server logs indicate cache hit (check logs)

---

## Scenario 7: Provider Health Check

**Acceptance Criteria**: Health endpoint reports provider status

### Steps

1. **Check health endpoint**:
   ```bash
   curl -s http://localhost:5000/api/generation/health | jq .
   ```

### Validation

- [ ] HTTP 200 OK
- [ ] Response contains provider status array:
  ```json
  {
    "providers": [
      {
        "name": "Ollama",
        "available": true,
        "lastChecked": "2026-02-13T..."
      }
    ]
  }
  ```
- [ ] If Ollama is running: `available: true`
- [ ] If Ollama is stopped: `available: false`

---

## Scenario 8: Filters (Repository Scope)

**Acceptance Criteria**: Retrieval filters limit documents by metadata

### Steps

1. **Query with repository filter**:
   ```bash
   curl -X POST http://localhost:5000/api/generation/stream \
     -H "Content-Type: application/json" \
     -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Show code examples\", \"topK\": 5, \"filters\": {\"repoUrl\": \"https://github.com/example/repo\"}}" \
     --no-buffer | jq -c 'select(.type == "token") | .text' | tr -d '\n'
   ```

### Validation

- [ ] Generation succeeds
- [ ] Response content references documents from specified repository only
- [ ] Server logs indicate filter passed to IVectorStore (check logs)
- [ ] Returned context matches filter criteria

---

## Scenario 9: Session Expiration

**Acceptance Criteria**: Sessions expire after 1 hour of inactivity

### Steps

1. **Create session and note expiration time**:
   ```bash
   RESPONSE=$(curl -s -X POST http://localhost:5000/api/generation/session \
     -H "Content-Type: application/json" \
     -d '{"owner": "expiration-test"}')
   echo $RESPONSE | jq '.expiresAt'
   SESSION_ID=$(echo $RESPONSE | jq -r '.sessionId')
   ```

2. **Verify expiration is ~1 hour from now**:
   ```bash
   EXPIRES_AT=$(echo $RESPONSE | jq -r '.expiresAt')
   CREATED_AT=$(echo $RESPONSE | jq -r '.createdAt')
   # Calculate difference (should be ~3600 seconds)
   ```

3. **Simulate expiration** (wait for cleanup service or manually test after 1 hour):
   - Wait for SessionCleanupService to run (every 5 minutes)
   - OR manually advance server time (if testing infrastructure supports it)

### Validation

- [ ] `expiresAt` is approximately 1 hour after `createdAt`
- [ ] After expiration, session is no longer accessible (404 on subsequent requests)
- [ ] SessionCleanupService logs indicate expired session removal

---

## Scenario 10: Metrics Export

**Acceptance Criteria**: OpenTelemetry metrics exported to Prometheus/Grafana

### Steps

1. **Run generation requests** (generate metrics):
   ```bash
   for i in {1..10}; do
     curl -X POST http://localhost:5000/api/generation/stream \
       -H "Content-Type: application/json" \
       -d "{\"sessionId\": \"$SESSION_ID\", \"prompt\": \"Test $i\", \"topK\": 5}" \
       --no-buffer > /dev/null
   done
   ```

2. **Check Prometheus metrics endpoint** (if configured):
   ```bash
   curl -s http://localhost:9090/metrics | grep generation
   ```

3. **Check Grafana dashboard** (manual):
   - Open http://localhost:3000
   - Navigate to DeepWiki RAG Generation Metrics dashboard
   - Verify panels show data for TTF, tokens/sec, error rates

### Validation

- [ ] Prometheus scrapes metrics successfully
- [ ] Grafana dashboard shows:
  - TTF histogram with p50, p95, p99
  - Token generation rate by provider
  - Total token count
  - Error rate by type
  - Active sessions count
  - Provider health status
- [ ] Metrics update in real-time as requests are made

---

## Performance Validation

### Time To First Token (TTF)

- [ ] Local Ollama: **<500ms** (measured from request to first delta)
- [ ] Remote OpenAI: **<1s** (measured from request to first delta)

### Token Throughput

- [ ] **>50 tokens/sec** sustained rate (measure with long generation)

### Cancellation Latency

- [ ] **<200ms** from cancel request to final delta emission

---

## Summary

**Total Scenarios**: 10  
**Scenarios Passed**: ____ / 10  
**Scenarios Failed**: ____  
**Blockers**: (list any critical failures)  

**Overall Status**: ⬜ PASS | ⬜ FAIL  

**Notes**:
- (Add any observations, performance numbers, or issues discovered)

---

## Sign-Off

**Tester**: _________________________  
**Date**: _________________________  
**Environment**: Dev | Staging | Production  
**Ollama Version**: _________________________  
**API Version**: _________________________
