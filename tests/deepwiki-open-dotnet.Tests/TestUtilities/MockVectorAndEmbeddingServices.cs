using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.ApiService.Tests.TestUtilities;

/// <summary>
/// In-memory vector store for API integration tests.
/// Stores upserted documents so tests can assert on written data.
/// Use services.AddScoped&lt;IVectorStore, MockVectorStore&gt;() in WebApplicationFactory.
/// </summary>
public class MockVectorStore : IVectorStore
{
    private readonly List<DocumentDto> _documents = [];

    public IReadOnlyList<DocumentDto> UpsertedDocuments => _documents.AsReadOnly();
    public int UpsertCallCount { get; private set; }

    public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
        float[] embedding, int k,
        Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var results = _documents
            .Take(k)
            .Select(d => new VectorQueryResult { Document = d, SimilarityScore = 1f })
            .ToList();
        return Task.FromResult<IReadOnlyList<VectorQueryResult>>(results);
    }

    public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
    {
        UpsertCallCount++;
        var existing = _documents.FindIndex(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath && d.ChunkIndex == document.ChunkIndex);
        if (existing >= 0)
            _documents[existing] = document;
        else
            _documents.Add(document);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _documents.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }

    public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
    {
        _documents.RemoveAll(d => d.RepoUrl == repoUrl && d.FilePath == filePath);
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Embedding service stub for API integration tests.
/// Returns deterministic fixed-dimension vectors so embedding calls don't require Ollama.
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    public string Provider    => "mock";
    public string ModelId     => "mock-embedding";
    public int EmbeddingDimension => 1536;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MakeVector(text));
    }

    public async IAsyncEnumerable<float[]> EmbedBatchAsync(
        IEnumerable<string> texts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var t in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Task.FromResult(MakeVector(t));
        }
    }

    public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
        IEnumerable<string> texts,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = texts
            .Select(t => EmbeddingResponse.Success(MakeVector(t), Provider, ModelId, latencyMs: 0))
            .ToList();
        return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(results);
    }

    private static float[] MakeVector(string text)
    {
        // Deterministic: first element is derived from text hash, rest are 0.
        var v = new float[1536];
        v[0] = (text.GetHashCode() & 0x7FFFFFFF) / (float)int.MaxValue;
        return v;
    }
}
