using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Text.Json;

namespace DeepWiki.ApiService.Controllers;

/// <summary>
/// Handles semantic document search queries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class QueryController : ControllerBase
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QueryController> _logger;
    private readonly ResiliencePipeline _embeddingResiliencePipeline;

    public QueryController(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<QueryController> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure resilience pipeline for embedding service calls (Polly v8 API)
        _embeddingResiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Embedding service retry attempt {AttemptNumber} after {Delay}ms. Exception: {Exception}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Circuit breaker opened for embedding service. Will retry after {BreakDuration}s",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
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

        try
        {
            // Embed the query text using resilient embedding service
            float[] queryEmbedding;
            try
            {
                queryEmbedding = await _embeddingResiliencePipeline.ExecuteAsync(
                    async ct => await _embeddingService.EmbedAsync(request.Query, ct),
                    cancellationToken);
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                _logger.LogError("Embedding service circuit breaker is open");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Embedding service is temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to embed query after retries: {Query}", request.Query);
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
                request.Query.Length > 50 ? request.Query[..50] + "..." : request.Query,
                request.K);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during query: {Query}", request.Query);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while processing your query." });
        }
    }
}
