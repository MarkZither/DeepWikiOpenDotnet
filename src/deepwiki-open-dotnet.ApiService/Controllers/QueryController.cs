using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Polly;
using System.Text.Json;

namespace DeepWiki.ApiService.Controllers;

/// <summary>
/// Handles semantic document search queries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "v1")]
public class QueryController : ControllerBase
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QueryController> _logger;
    private readonly ResiliencePipeline _embeddingResiliencePipeline;
    private readonly IConfiguration _configuration;

    public QueryController(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<QueryController> logger,
        ResiliencePipeline embeddingResiliencePipeline,
        IConfiguration configuration)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingResiliencePipeline = embeddingResiliencePipeline ?? throw new ArgumentNullException(nameof(embeddingResiliencePipeline));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Performs semantic search for documents based on a natural language query.
    /// </summary>
    /// <param name="request">The query request containing search parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of documents ordered by similarity score.</returns>
    /// <response code="200">Returns semantically similar documents.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="503">Embedding service unavailable.</response>
    [HttpPost]
    [ProducesResponseType(typeof(QueryResultItem[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Query(
        [FromBody] QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return BadRequest(new ErrorResponse { Detail = errors });
        }

        var queryText = request.Query!;

        try
        {
            // Log entry for tracing
            var preview = queryText.Length > 100 ? queryText[..100] + "..." : queryText;
            _logger.LogInformation("Query endpoint invoked. Query preview: '{QueryPreview}' K={K}", preview, request.K);

            // Embed the query text using resilient embedding service with a local timeout to avoid hanging
            float[] queryEmbedding;
            var swEmbed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("About to call embedding provider '{Provider}'", _embeddingService.Provider);

                // Apply an embedding timeout in addition to the request cancellation token.
                // Default 120s — configurable via Embedding:TimeoutSeconds for slow local models.
                var embedTimeoutSec = _configuration.GetValue<int?>("Embedding:TimeoutSeconds") ?? 120;
                using var ctsEmbed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsEmbed.CancelAfter(TimeSpan.FromSeconds(embedTimeoutSec));

                queryEmbedding = await _embeddingResiliencePipeline.ExecuteAsync(
                    async ct => await _embeddingService.EmbedAsync(queryText, ct),
                    ctsEmbed.Token);

                swEmbed.Stop();
                _logger.LogInformation("Embedding completed in {ElapsedMs}ms with dimension {Dim}", swEmbed.ElapsedMilliseconds, queryEmbedding?.Length ?? 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Query request was cancelled by the client");
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Embedding provider call timed out after 15s");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Embedding service timed out. Please try again later." });
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                _logger.LogError("Embedding service circuit breaker is open");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Embedding service is temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to embed query after retries: {Query}", queryText);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Failed to generate query embedding. Please try again." });
            }

            // Validate embedding dimension
            if (queryEmbedding == null || queryEmbedding.Length != 1536)
            {
                _logger.LogError("Invalid embedding dimension: {Dimension}", queryEmbedding?.Length ?? 0);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Embedding service returned invalid result." });
            }

            // Build filters dictionary for vector store
            Dictionary<string, string>? filters = null;
            if (request.Filters != null)
            {
                filters = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(request.Filters.RepoUrl))
                {
                    filters["repoUrl"] = request.Filters.RepoUrl;
                }
                if (!string.IsNullOrEmpty(request.Filters.FilePath))
                {
                    filters["filePath"] = request.Filters.FilePath;
                }
            }

            // Query vector store for similar documents
            var results = await _vectorStore.QueryAsync(
                queryEmbedding,
                request.K,
                filters,
                cancellationToken);

            // Map to API response format
            var response = results.Select(r => new QueryResultItem
            {
                Id = r.Document.Id,
                RepoUrl = r.Document.RepoUrl,
                FilePath = r.Document.FilePath,
                Title = r.Document.Title,
                Text = request.IncludeFullText ? r.Document.Text : null,
                SimilarityScore = r.SimilarityScore,
                Metadata = string.IsNullOrEmpty(r.Document.MetadataJson)
                    ? null
                    : JsonSerializer.Deserialize<JsonElement>(r.Document.MetadataJson)
            }).ToArray();

            _logger.LogInformation(
                "Query completed: found {ResultCount} documents for query '{Query}' with k={K}",
                response.Length,
                queryText.Length > 50 ? queryText[..50] + "..." : queryText,
                request.K);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during query: {Query}", queryText);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while processing your query." });
        }
    }
}
