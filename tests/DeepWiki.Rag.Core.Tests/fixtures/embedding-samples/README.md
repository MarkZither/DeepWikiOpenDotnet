# Embedding fixtures for tests

This directory contains deterministic fixtures for use in unit and integration tests.

Contents
- `sample-documents.json` — small set of documents used for ingestion and query tests.
- `sample-embeddings.json` — precomputed 1536-dim embeddings keyed by document id (deterministic synthetic values).
- `similarity-ground-truth.json` — sample queries with expected top-k document ids.
- `python-tiktoken-samples.json` — small set of texts with expected token counts (reference values for parity tests).
- `generate_embedding_fixtures.py` — (deprecated) Python helper to regenerate `sample-embeddings.json`.
- `EmbeddingFixtureGenerator` — a .NET console tool that calls a local Foundry/Ollama endpoint to produce real embeddings and writes `sample-embeddings.json`.

Regenerating fixtures

Option A (recommended - .NET tool):

1. Build and run the generator (default: Foundry-compatible endpoint at http://localhost:5273):

   dotnet run --project tests/DeepWiki.Rag.Core.Tests/tools/EmbeddingFixtureGenerator -- --model mxbai-embed-large --input tests/DeepWiki.Rag.Core.Tests/fixtures/embedding-samples/sample-documents.json --output tests/DeepWiki.Rag.Core.Tests/fixtures/embedding-samples/sample-embeddings.json

2. For Ollama (default port 11434):

   dotnet run --project tests/DeepWiki.Rag.Core.Tests/tools/EmbeddingFixtureGenerator -- --ollama --model nomic-embed-text --host localhost --port 11434

Option B (legacy - Python):

1. Install Python 3.11+ and run the script (not recommended for CI):

   python tests/DeepWiki.Rag.Core.Tests/fixtures/embedding-samples/generate_embedding_fixtures.py

2. The script reads `sample-documents.json` and writes `sample-embeddings.json`.

Why these fixtures
- Keep tests deterministic and fast (no external provider calls needed).
- Embeddings are synthetic but repeatable and sized correctly (1536 floats).
- `similarity-ground-truth.json` provides small query → expected doc sets to validate k-NN behavior in integration tests.

Note: For production-level evaluation, regenerate embeddings from a real provider and replace `sample-embeddings.json` if needed.