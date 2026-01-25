using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.VectorData;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Data.Abstractions.VectorData;

/// <summary>
/// Extension of Microsoft.Extensions.VectorData.VectorStore that provides
/// typed access to document collections for the DeepWiki application.
/// </summary>
public interface IDocumentVectorStore
{
    /// <summary>
    /// Gets a document collection by name.
    /// </summary>
    /// <param name="name">The name of the collection (e.g., "documents").</param>
    /// <returns>A vector store collection for DocumentRecord entities.</returns>
    IDocumentVectorCollection GetDocumentCollection(string name);

    /// <summary>
    /// Gets the default document collection ("documents").
    /// </summary>
    /// <returns>A vector store collection for DocumentRecord entities.</returns>
    IDocumentVectorCollection GetDocumentCollection();

    /// <summary>
    /// Lists all collection names in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of collection names.</returns>
    IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default);
}
