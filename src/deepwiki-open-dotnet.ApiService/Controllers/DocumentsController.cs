using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Diagnostics;

namespace DeepWiki.ApiService.Controllers;

/// <summary>
/// Handles document CRUD operations and ingestion.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly DeepWiki.Data.Interfaces.IDocumentRepository _repository;
    private readonly ILogger<DocumentsController> _logger;
    private readonly ResiliencePipeline _ingestionResiliencePipeline;

    public DocumentsController(
        IDocumentIngestionService ingestionService,
        DeepWiki.Data.Interfaces.IDocumentRepository repository,
        ILogger<DocumentsController> logger)
    {
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure resilience pipeline for ingestion (embedding calls)
        _ingestionResiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Ingestion retry attempt {AttemptNumber} after {Delay}ms. Exception: {Exception}",
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
                        "Circuit breaker opened for ingestion service. Will retry after {BreakDuration}s",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Ingests a batch of documents into the vector store.
    /// </summary>
    /// <param name="request">The ingestion request containing documents to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ingestion results with success/failure counts and document IDs.</returns>
    /// <response code="200">Returns ingestion results.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="503">Embedding service unavailable.</response>
    [HttpPost("ingest")]
    [ProducesResponseType(typeof(IngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest(
        [FromBody] IngestRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return BadRequest(new ErrorResponse { Detail = errors });
        }

        // Additional validation
        if (request.Documents.Count == 0)
        {
            return BadRequest(new ErrorResponse { Detail = "Documents array cannot be empty." });
        }

        if (request.Documents.Count > 1000)
        {
            return BadRequest(new ErrorResponse { Detail = "Cannot ingest more than 1000 documents in a single request." });
        }

        // Validate individual documents
        for (int i = 0; i < request.Documents.Count; i++)
        {
            var doc = request.Documents[i];
            if (string.IsNullOrWhiteSpace(doc.RepoUrl))
            {
                return BadRequest(new ErrorResponse { Detail = $"Document at index {i}: RepoUrl is required." });
            }
            if (string.IsNullOrWhiteSpace(doc.FilePath))
            {
                return BadRequest(new ErrorResponse { Detail = $"Document at index {i}: FilePath is required." });
            }
            if (string.IsNullOrWhiteSpace(doc.Title))
            {
                return BadRequest(new ErrorResponse { Detail = $"Document at index {i}: Title is required." });
            }
            if (string.IsNullOrWhiteSpace(doc.Text))
            {
                return BadRequest(new ErrorResponse { Detail = $"Document at index {i}: Text is required." });
            }
            if (doc.Text.Length > 5_000_000)
            {
                return BadRequest(new ErrorResponse { Detail = $"Document at index {i}: Text exceeds maximum length of 5,000,000 characters." });
            }
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // Map API request to service request
            var ingestionRequest = MapToIngestionRequest(request);

            // Execute ingestion with resilience policy
            IngestionResult result;
            try
            {
                result = await _ingestionResiliencePipeline.ExecuteAsync(
                    async ct => await _ingestionService.IngestAsync(ingestionRequest, ct),
                    cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                _logger.LogError("Ingestion service circuit breaker is open");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Embedding service is temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest documents after retries");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ErrorResponse { Detail = "Failed to process ingestion request. Please try again." });
            }

            sw.Stop();

            // Map service result to API response
            var response = MapToIngestResponse(result, sw.ElapsedMilliseconds);

            _logger.LogInformation(
                "Ingestion completed: {SuccessCount} successful, {FailureCount} failed, {TotalChunks} chunks created in {DurationMs}ms",
                response.SuccessCount,
                response.FailureCount,
                response.TotalChunks,
                response.DurationMs);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during document ingestion");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while processing your request." });
        }
    }

    /// <summary>
    /// Retrieves a single document by its ID.
    /// </summary>
    /// <param name="id">Document ID (GUID)</param>
    /// <returns>Document details or 404 with ErrorResponse when not found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DeepWiki.Data.Abstractions.Models.DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                return NotFound(new ErrorResponse { Detail = "Document not found." });
            }

            var dto = MapToDocumentDto(entity);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while retrieving the document." });
        }
    }

    /// <summary>
    /// Deletes a document by its ID.
    /// </summary>
    /// <param name="id">Document ID (GUID)</param>
    /// <returns>204 No Content on success, 404 when not found.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var exists = await _repository.ExistsAsync(id);
            if (!exists)
            {
                return NotFound(new ErrorResponse { Detail = "Document not found." });
            }

            await _repository.DeleteAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while deleting the document." });
        }
    }

    /// <summary>
    /// Lists documents with pagination and optional repoUrl filter.
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Items per page</param>
    /// <param name="repoUrl">Optional repository URL filter</param>
    [HttpGet]
    [ProducesResponseType(typeof(DocumentListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? repoUrl = null)
    {
        if (page < 1)
        {
            return BadRequest(new ErrorResponse { Detail = "Page must be >= 1." });
        }

        if (pageSize < 1 || pageSize > 1000)
        {
            return BadRequest(new ErrorResponse { Detail = "PageSize must be between 1 and 1000." });
        }

        try
        {
            var skip = (page - 1) * pageSize;
            var (items, total) = await _repository.ListAsync(repoUrl, skip, pageSize);

            var response = new DocumentListResponse
            {
                Items = items.Select(MapToDocumentSummary).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Detail = "An unexpected error occurred while listing documents." });
        }
    }

    private static DeepWiki.Data.Abstractions.Models.DocumentDto MapToDocumentDto(DeepWiki.Data.Entities.DocumentEntity e)
    {
        return new DeepWiki.Data.Abstractions.Models.DocumentDto
        {
            Id = e.Id,
            RepoUrl = e.RepoUrl,
            FilePath = e.FilePath,
            Title = e.Title ?? string.Empty,
            Text = e.Text ?? string.Empty,
            Embedding = e.Embedding?.ToArray() ?? Array.Empty<float>(),
            MetadataJson = e.MetadataJson ?? "{}",
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            TokenCount = e.TokenCount,
            FileType = e.FileType ?? string.Empty,
            IsCode = e.IsCode,
            IsImplementation = e.IsImplementation
        };
    }

    private static DocumentSummary MapToDocumentSummary(DeepWiki.Data.Entities.DocumentEntity e)
    {
        return new DocumentSummary
        {
            Id = e.Id,
            RepoUrl = e.RepoUrl,
            FilePath = e.FilePath,
            Title = e.Title ?? string.Empty,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            TokenCount = e.TokenCount,
            FileType = e.FileType ?? string.Empty,
            IsCode = e.IsCode
        };
    }

    /// <summary>
    /// Maps API IngestRequest to service IngestionRequest.
    /// </summary>
    private IngestionRequest MapToIngestionRequest(IngestRequest apiRequest)
    {
        var documents = apiRequest.Documents.Select(doc => new IngestionDocument
        {
            RepoUrl = doc.RepoUrl,
            FilePath = doc.FilePath,
            Title = doc.Title,
            Text = doc.Text,
            MetadataJson = doc.Metadata.HasValue
                ? doc.Metadata.Value.GetRawText()
                : "{}"
        }).ToList();

        return new IngestionRequest
        {
            Documents = documents,
            ContinueOnError = apiRequest.ContinueOnError,
            BatchSize = apiRequest.BatchSize,
            MaxRetries = 3,
            MaxTokensPerChunk = 8192,
            SkipEmbedding = false
        };
    }

    /// <summary>
    /// Maps service IngestionResult to API IngestResponse.
    /// </summary>
    private IngestResponse MapToIngestResponse(IngestionResult serviceResult, long durationMs)
    {
        return new IngestResponse
        {
            SuccessCount = serviceResult.SuccessCount,
            FailureCount = serviceResult.FailureCount,
            TotalChunks = serviceResult.TotalChunks,
            DurationMs = durationMs,
            IngestedDocumentIds = serviceResult.IngestedDocumentIds,
            Errors = serviceResult.Errors.Select(e => new IngestError
            {
                DocumentIdentifier = e.DocumentIdentifier,
                Message = e.ErrorMessage,
                Stage = e.Stage.ToString()
            }).ToList()
        };
    }
}
