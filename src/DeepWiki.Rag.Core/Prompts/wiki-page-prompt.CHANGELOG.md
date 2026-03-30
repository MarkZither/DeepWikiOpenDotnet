# wiki-page-prompt CHANGELOG

## v1.0.0 — 2026-03-01

### Initial version

**Feature**: wiki-page-generation  
**Prompt file**: `wiki-page-prompt.txt`

### Rationale

The page content prompt is called once per TOC entry in Phase 2 of the wiki generation pipeline. It receives RAG-retrieved document chunks scoped to the page's title + section path + keywords, ensuring the generated content is grounded in the actual ingested documents rather than the model's parametric knowledge.

### Placeholders

| Placeholder | Description |
|---|---|
| `{wiki_name}` | Display name of the wiki (provides high-level context). |
| `{section_path}` | Section hierarchy path (e.g., `Architecture/Data Model`). |
| `{page_title}` | Title of the page being generated. |
| `{toc_json}` | Full TOC JSON so the model knows sibling pages and can suggest related links. |
| `{document_chunks}` | Newline-delimited chunks from IVectorStore retrieved via embedding of `pageTitle + sectionPath + keywords`. |

### RELATED_PAGES parsing contract

The orchestrator splits the LLM response on the **last occurrence** of the `RELATED_PAGES:` marker:

- Everything **before** the marker becomes the page `Content` (Markdown).
- Everything **after** the marker is parsed as a JSON string array of page titles.
- If `RELATED_PAGES:` is absent, the page content is used as-is with an empty related-pages list.
- Page titles in the array are matched case-insensitively against existing wiki page titles. Unmatched titles are silently ignored (no orphaned relations).

### Edge cases

| Case | Behaviour |
|---|---|
| No `RELATED_PAGES:` line | Content = full response; related pages = empty |
| Empty array `[]` | Content = text before marker; related pages = empty |
| Unrecognised title | Silently skipped; relation not created |
| Special chars in title | Matched after Unicode normalisation (NFC) |
