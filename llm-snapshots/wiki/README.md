# LLM Snapshot Directory: wiki

**Purpose**: Baseline fixtures for snapshot-based tests of the wiki generation pipeline.

## Snapshot Format

Each snapshot is a JSON file conforming to the canonical schema:

```json
{
  "id": "<uuid>",
  "created_at": "<iso8601>",
  "model": "<model-name>",
  "feature": "<feature-tag>",
  "request": {
    "prompt": "<prompt text sent to the LLM>"
  },
  "stream": [
    "<token1>", "<token2>", "..."
  ],
  "response_hash": "<sha256-hex of the joined stream tokens>",
  "redacted": false,
  "retention_policy": "indefinite"
}
```

## Redaction Policy

- Snapshots MUST NOT contain PII, API keys, or secrets.
- If a real LLM call was used to seed a snapshot, review and redact any sensitive content before committing.
- Set `"redacted": true` when any field has been manually scrubbed.

## Playback Instructions

Tests in `WikiTocParserTests` and `WikiPageParserTests` load these fixtures from disk and replay the `stream` array as if it were a live LLM stream. This ensures parser behaviour is deterministic and reproducible without live LLM calls.

To update a fixture:
1. Obtain the desired LLM output (via a real call or manual authoring).
2. Format it as a JSON array of token strings in the `stream` field.
3. Compute `response_hash = SHA-256(string.Join("", stream))` and set it.
4. Commit the updated fixture.

## Files

| File | Feature | Description |
|---|---|---|
| `toc-v1.0.0.json` | wiki-toc-generation | Baseline TOC response with two sections and four pages |
| `page-v1.0.0.json` | wiki-page-generation | Baseline page content response with RELATED_PAGES footer |
