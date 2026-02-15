#!/usr/bin/env bash
# Simple curl demo for streaming RAG service (NDJSON parsing with jq)
# Usage: ./curl-demo.sh
set -euo pipefail

API_URL=${API_URL:-http://localhost:5000}

# Create a session
SESSION_ID=$(curl -s -X POST "$API_URL/api/generation/session" \
  -H "Content-Type: application/json" \
  -d '{"owner":"curl-demo"}' | jq -r '.sessionId')

if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
  echo "Failed to create session"
  exit 1
fi

echo "Using session: $SESSION_ID"

echo "Streaming prompt..."

curl -s -X POST "$API_URL/api/generation/stream" \
  -H "Content-Type: application/json" \
  -N \
  -d "{\"sessionId\":\"$SESSION_ID\",\"prompt\":\"Explain streaming NDJSON with jq\"}" \
  | while read -r line; do
      TYPE=$(echo "$line" | jq -r '.type')
      case "$TYPE" in
        token)
          echo -n "$(echo "$line" | jq -r '.text')"
          ;;
        done)
          echo ""
          echo "-- Generation complete --"
          ;;
        error)
          echo "Error: $(echo "$line" | jq -r '.metadata.message // .metadata')"
          exit 1
          ;;
        *)
          # ignore
          ;;
      esac
    done
