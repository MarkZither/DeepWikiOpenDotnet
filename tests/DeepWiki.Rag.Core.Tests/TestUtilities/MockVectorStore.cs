using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Tests.TestUtilities;

/// <summary>
/// In-memory vector store for unit tests.
/// Stores upserted documents and returns them from QueryAsync so tests can assert on written data.
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
        var existing = _documents.FindIndex(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath);
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

    public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
