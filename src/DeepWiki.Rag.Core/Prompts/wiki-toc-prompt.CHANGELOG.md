# wiki-toc-prompt CHANGELOG

## v1.0.0 — 2026-03-01

### Initial version

**Feature**: wiki-toc-generation  
**Prompt file**: `wiki-toc-prompt.txt`

### Rationale

The TOC prompt asks the LLM to structure all ingested document summaries into a coherent wiki outline before any page content is generated. This two-phase approach (TOC → pages) is necessary because:

1. **Token limits** — generating an entire wiki in one call would exceed the context window for most models.
2. **Structural coherence** — having a fixed TOC lets each page generation call see sibling page titles, enabling meaningful "Related Pages" connections.
3. **Retry isolation** — if the TOC fails to parse, only the TOC call is retried; page generation can proceed with a valid structure once the TOC succeeds.

### Placeholders

| Placeholder | Description |
|---|---|
| `{document_summaries}` | Newline-delimited list of document titles and first-200-char summaries retrieved via vector search on the collection name. |
| `{max_pages}` | Upper bound on total pages (from `WikiGenerationOptions.PageTokenLimit`-derived heuristic). |

### Expected output format

Valid JSON only (no markdown fences):

```json
{
  "sections": [
    {
      "sectionPath": "SectionName",
      "pages": [
        { "title": "PageTitle", "keywords": ["kw1", "kw2"] }
      ]
    }
  ]
}
```

### Parse failure handling

If the LLM response is not valid JSON or does not contain a `sections` array, `WikiGenerationOrchestrator` retries up to `WikiGenerationOptions.MaxTocRetries` times before marking the wiki as `Error` and aborting generation.
