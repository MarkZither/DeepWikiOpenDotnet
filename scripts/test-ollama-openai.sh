#!/usr/bin/env bash
# Test OpenAI provider directed at an Ollama instance by temporarily starting the API
# Usage: ./scripts/test-ollama-openai.sh [OLLAMA_URL] [API_URL]
# Example: ./scripts/test-ollama-openai.sh http://localhost:11434 http://localhost:5000
set -euo pipefail

NO_START=0
for arg in "$@"; do
  if [ "$arg" = "-n" ] || [ "$arg" = "--no-start" ]; then
    NO_START=1
  fi
done

OLLAMA_URL=${1:-${OLLAMA_URL:-http://localhost:11434}}
API_URL=${2:-${API_URL:-http://localhost:5000}}

# Helper: probe a health endpoint and return 0 on success
probe_health() {
  local url="$1"
  if curl -s --connect-timeout 2 "$url/api/generation/health" | jq -e . >/dev/null 2>&1; then
    echo "$url"
    return 0
  fi
  return 1
}

# If --no-start is requested, try to detect a running API on common ports (5000, 5484) and use it.
if [ "$NO_START" -eq 1 ]; then
  echo "--no-start specified: searching for existing API on $API_URL, http://localhost:5000, http://localhost:5484"
  # Try provided API_URL first
  if probe_health "$API_URL" >/dev/null 2>&1; then
    echo "Found API at $API_URL"
  else
    # Try default ports explicitly
    if probe_health "http://localhost:5000" >/dev/null 2>&1; then
      API_URL="http://localhost:5000"
      echo "Found API at $API_URL"
    elif probe_health "http://localhost:5484" >/dev/null 2>&1; then
      API_URL="http://localhost:5484"
      echo "Found API at $API_URL"
    else
      echo "No running API detected on ports 5000 or 5484. Exiting."
      exit 1
    fi
  fi
else
  echo "OLLAMA_URL=$OLLAMA_URL"
  echo "API_URL=$API_URL"
fi

LOGFILE=$(mktemp /tmp/deepwiki-api-log.XXXXXX)

# If an API is already running on the provided URL or on common ports, use it and don't start a new one.
DETECTED_URL=""
if probe_health "$API_URL" >/dev/null 2>&1; then
  DETECTED_URL="$API_URL"
elif probe_health "http://localhost:5000" >/dev/null 2>&1; then
  DETECTED_URL="http://localhost:5000"
elif probe_health "http://localhost:5484" >/dev/null 2>&1; then
  DETECTED_URL="http://localhost:5484"
fi

if [ -n "$DETECTED_URL" ]; then
  API_URL="$DETECTED_URL"
  echo "Found running API at $API_URL; skipping start."
else
  if [ "$NO_START" -eq 1 ]; then
    echo "No running API detected on ports 5000 or 5484 and --no-start specified; exiting."
    exit 1
  fi

  echo "No existing API detected; starting API with OpenAI provider pointing at Ollama... (logs -> $LOGFILE)"
  (
    export OPENAI__BaseUrl="$OLLAMA_URL"
    export OPENAI__Provider="ollama"
    # Make Generation service try OpenAI first, then Ollama if fallback needed
    export GENERATION__PROVIDERS__0="OpenAI"
    export GENERATION__PROVIDERS__1="Ollama"
    cd src/deepwiki-open-dotnet.ApiService
    # Run API in development mode (bind to HTTP URL passed) and redirect logs
    dotnet run --urls "http://localhost:5000" > "$LOGFILE" 2>&1
  ) &
  API_PID=$!

  cleanup() {
    echo "Stopping API (pid $API_PID)"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
    echo "Logs (last 200 lines):"
    tail -n 200 "$LOGFILE" || true
    rm -f "$LOGFILE"
  }
  trap cleanup EXIT

  # Wait for API to be healthy (poll health endpoint)
  MAX_WAIT=30
  SLEPT=0
  while [ $SLEPT -lt $MAX_WAIT ]; do
    if probe_health "$API_URL" >/dev/null 2>&1; then
      echo "API healthy"
      break
    fi
    sleep 1
    SLEPT=$((SLEPT+1))
  done

  if [ $SLEPT -ge $MAX_WAIT ]; then
    echo "Timed out waiting for API to become healthy. Check logs: $LOGFILE"
    exit 1
  fi
fi

# Create a session
SESSION_ID=$(curl -s -X POST "$API_URL/api/generation/session" \
  -H "Content-Type: application/json" \
  -d '{"owner":"test-script"}' | jq -r '.sessionId')

if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
  echo "Failed to create session. Dumping API logs (tail 200):"
  tail -n 200 "$LOGFILE" || true
  exit 1
fi

echo "Using session: $SESSION_ID"

echo "Running streaming prompt (OpenAI -> Ollama)..."

curl -s -X POST "$API_URL/api/generation/stream" \
  -H "Content-Type: application/json" \
  -N \
  -d "{\"sessionId\":\"$SESSION_ID\",\"prompt\":\"Test OpenAI->Ollama streaming: say hello\"}" \
  | while read -r line; do
    # Some lines may be empty; ignore
    if [ -z "$line" ]; then continue; fi

    TYPE=$(echo "$line" | jq -r '.type // empty')
    case "$TYPE" in
      token)
        echo -n "$(echo "$line" | jq -r '.text // empty')"
        ;;
      done)
        echo ""
        echo "-- Generation complete --"
        ;;
      error)
        echo "Error: $(echo "$line" | jq -r '.metadata.message // .metadata // tostring')"
        exit 1
        ;;
      *)
        # ignore other messages
        ;;
    esac
  done

# All done; cleanup will be executed by trap
exit 0
