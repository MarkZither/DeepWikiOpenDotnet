using DeepWiki.ApiService.Models;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Text.Json;

namespace DeepWiki.ApiService.Controllers;

/// <summary>
/// REST API controller for wiki CRUD operations.
/// Routes: /api/wiki
/// </summary>
[ApiController]
[Route("api/wiki")]
[Produces("application/json")]
public class WikiController : ControllerBase
{
    private readonly IWikiService _wikiService;
    private readonly IWikiGenerationService _wikiGenerationService;
    private readonly IWikiExportService _wikiExportService;
    private readonly IServiceScopeFactory _scopeFactory;

    public WikiController(
        IWikiService wikiService,
        IWikiGenerationService wikiGenerationService,
        IWikiExportService wikiExportService,
        IServiceScopeFactory scopeFactory)
    {
        _wikiService = wikiService;
        _wikiGenerationService = wikiGenerationService;
        _wikiExportService = wikiExportService;
        _scopeFactory = scopeFactory;
    }

    // ── POST /api/wiki ────────────────────────────────────────────────────────

    /// <summary>Creates a new wiki with optional initial pages.</summary>
    /// <response code="201">Wiki created successfully.</response>
    /// <response code="400">Validation failed — name or collectionId missing or invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(WikiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WikiResponse>> CreateWiki([FromBody] CreateWikiRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var pages = request.Pages.Select(p => new Data.Abstractions.Entities.WikiPageEntity
            {
                Title = p.Title,
                Content = p.Content,
                SectionPath = p.SectionPath,
                SortOrder = p.SortOrder,
                ParentPageId = p.ParentPageId
            }).ToList();

            var wiki = await _wikiService.CreateWikiAsync(
                request.Name,
                request.CollectionId,
                request.Description,
                pages,
                HttpContext.RequestAborted);

            var response = WikiResponse.FromEntity(wiki);
            return CreatedAtAction(nameof(GetWiki), new { id = wiki.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { detail = ex.Message });
        }
    }

    // ── POST /api/wiki/generate ───────────────────────────────────────────────

    /// <summary>
    /// Starts wiki generation for a collection.
    /// Returns 202 Accepted with <c>{ wikiId }</c> as soon as the wiki entity is created;
    /// all subsequent progress events are delivered to SignalR clients subscribed to the
    /// <c>wiki:{wikiId}</c> group on <c>WikiProgressHub</c> (/hubs/wiki-progress).
    /// </summary>
    /// <response code="202">Generation started — body contains <c>{ wikiId }</c>.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="409">A wiki with this name is already being generated for this collection.</response>
    [HttpPost("generate")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateWiki([FromBody] GenerateWikiRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var coreRequest = new WikiGenerationRequest
        {
            CollectionId = request.CollectionId,
            Name = request.Name,
            Description = request.Description
        };

        // Create a long-lived DI scope for background generation.
        // The scope outlives the HTTP request and is disposed only after the entire
        // generation pipeline completes (success, cancellation, or error).
        var scope = _scopeFactory.CreateAsyncScope();
        var generationService = scope.ServiceProvider.GetRequiredService<IWikiGenerationService>();

        // GenerateAsync is synchronous up to the return — it validates duplicate guards and
        // returns the lazy IAsyncEnumerable without starting any async work yet.
        IAsyncEnumerable<WikiGenerationProgress> stream;
        try
        {
            stream = generationService.GenerateAsync(coreRequest, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            await scope.DisposeAsync();
            return Conflict(new { detail = ex.Message });
        }

        // The background task drives the entire generation pipeline.
        // It signals the TCS with the wiki ID once the wiki entity is created
        // (before any LLM call), enabling us to return 202 within seconds.
        // All progress events are also pushed to SignalR by IWikiProgressNotifier
        // inside WikiGenerationOrchestrator — no extra work is needed here.
        var tcs = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestAborted = HttpContext.RequestAborted;

        _ = Task.Run(async () =>
        {
            await using (scope)
            {
                try
                {
                    await foreach (var evt in stream)
                    {
                        if (evt.EventType == WikiGenerationProgress.EventWikiCreated)
                            tcs.TrySetResult(evt.WikiId);
                    }
                    // If wiki_created was never emitted the pipeline failed early.
                    tcs.TrySetException(new InvalidOperationException(
                        "Generation pipeline ended without a wiki_created event."));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        });

        Guid wikiId;
        try
        {
            // Wait up to 30 s for the wiki entity to be created (fast DB insert).
            wikiId = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), requestAborted);
        }
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { detail = "Wiki creation timed out. Check the API service logs." });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { detail = "Request aborted by client." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { detail = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { detail = $"Generation failed to start: {ex.Message}" });
        }

        return Accepted((string?)null, new { wikiId });
    }

    // ── GET /api/wiki/{id} ────────────────────────────────────────────────────

    /// <summary>Returns the full wiki with all its pages.</summary>
    /// <response code="200">Wiki found.</response>
    /// <response code="404">Wiki not found.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WikiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiResponse>> GetWiki(Guid id)
    {
        var wiki = await _wikiService.GetWikiByIdAsync(id, HttpContext.RequestAborted);
        if (wiki == null)
            return NotFound();

        return Ok(WikiResponse.FromEntity(wiki));
    }

    // ── DELETE /api/wiki/{id} ─────────────────────────────────────────────────

    /// <summary>Permanently deletes the wiki, all its pages, and page relations.</summary>
    /// <response code="204">Wiki deleted.</response>
    /// <response code="404">Wiki not found.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWiki(Guid id)
    {
        var deleted = await _wikiService.DeleteWikiAsync(id, HttpContext.RequestAborted);
        return deleted ? NoContent() : NotFound();
    }

    // ── GET /api/wiki/projects ────────────────────────────────────────────────

    /// <summary>Returns a paginated list of all wiki projects.</summary>
    /// <param name="page">1-based page number (default: 1).</param>
    /// <param name="pageSize">Items per page, max 100 (default: 20).</param>
    /// <response code="200">Paginated list of wikis.</response>
    [HttpGet("projects")]
    [ProducesResponseType(typeof(PagedResult<WikiSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WikiSummaryResponse>>> GetProjects(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await _wikiService.GetProjectsAsync(page, pageSize, HttpContext.RequestAborted);

        return Ok(new PagedResult<WikiSummaryResponse>
        {
            Items = items.Select(WikiSummaryResponse.FromEntity).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── PUT /api/wiki/{id} ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the wiki description. Wiki name is immutable after creation.
    /// Returns 400 if a "name" field is present in the request body.
    /// </summary>
    /// <response code="200">Description updated.</response>
    /// <response code="400">Request body contains a forbidden "name" field.</response>
    /// <response code="404">Wiki not found.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WikiSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiSummaryResponse>> UpdateWiki(Guid id, [FromBody] UpdateWikiRequest request)
    {
        // Detect forbidden "name" field (immutability guard)
        if (request.ExtensionData?.ContainsKey("name") == true ||
            request.ExtensionData?.ContainsKey("Name") == true)
        {
            return BadRequest(new { detail = "Wiki name is immutable after creation." });
        }

        var wiki = await _wikiService.UpdateWikiDescriptionAsync(id, request.Description, HttpContext.RequestAborted);
        if (wiki == null)
            return NotFound();

        return Ok(WikiSummaryResponse.FromEntity(wiki));
    }

    // ── GET /api/wiki/{id}/pages/{pageId} ─────────────────────────────────────

    /// <summary>Returns a single page including its related page links.</summary>
    /// <response code="200">Page found.</response>
    /// <response code="404">Wiki or page not found.</response>
    [HttpGet("{id:guid}/pages/{pageId:guid}")]
    [ProducesResponseType(typeof(WikiPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponse>> GetPage(Guid id, Guid pageId)
    {
        var page = await _wikiService.GetPageByIdAsync(id, pageId, HttpContext.RequestAborted);
        if (page == null)
            return NotFound();

        var relatedPages = await _wikiService.GetRelatedPagesAsync(pageId, HttpContext.RequestAborted);
        return Ok(WikiPageResponse.FromEntity(page, relatedPages));
    }

    // ── PUT /api/wiki/{id}/pages/{pageId} ─────────────────────────────────────

    /// <summary>Updates mutable fields of a wiki page. Only non-null fields are applied.</summary>
    /// <response code="200">Page updated.</response>
    /// <response code="404">Wiki or page not found.</response>
    [HttpPut("{id:guid}/pages/{pageId:guid}")]
    [ProducesResponseType(typeof(WikiPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponse>> UpdatePage(
        Guid id,
        Guid pageId,
        [FromBody] UpdateWikiPageRequest request)
    {
        var page = await _wikiService.UpdatePageAsync(
            id, pageId,
            request.Title, request.Content, request.SectionPath, request.SortOrder,
            request.RelatedPageIds,
            HttpContext.RequestAborted);

        if (page == null)
            return NotFound();

        var relatedPages = await _wikiService.GetRelatedPagesAsync(pageId, HttpContext.RequestAborted);
        return Ok(WikiPageResponse.FromEntity(page, relatedPages));
    }

    // ── POST /api/wiki/{id}/pages ─────────────────────────────────────────────

    /// <summary>Adds a new page to an existing wiki.</summary>
    /// <response code="201">Page created.</response>
    /// <response code="400">Validation failed — title or content missing.</response>
    /// <response code="404">Wiki not found.</response>
    [HttpPost("{id:guid}/pages")]
    [ProducesResponseType(typeof(WikiPageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WikiPageResponse>> AddPage(Guid id, [FromBody] CreateWikiPageRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Verify wiki exists
        var wiki = await _wikiService.GetWikiByIdAsync(id, HttpContext.RequestAborted);
        if (wiki == null)
            return NotFound();

        try
        {
            var page = await _wikiService.AddPageAsync(
                id,
                request.Title,
                request.Content,
                request.SectionPath,
                request.SortOrder,
                request.ParentPageId,
                request.RelatedPageIds,
                HttpContext.RequestAborted);

            var response = WikiPageResponse.FromEntity(page);
            return CreatedAtAction(nameof(GetPage), new { id, pageId = page.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { detail = ex.Message });
        }
    }

    // ── DELETE /api/wiki/{id}/pages/{pageId} ──────────────────────────────────

    /// <summary>Permanently removes a page from the wiki.</summary>
    /// <response code="204">Page deleted.</response>
    /// <response code="404">Wiki or page not found.</response>
    [HttpDelete("{id:guid}/pages/{pageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePage(Guid id, Guid pageId)
    {
        var deleted = await _wikiService.DeletePageAsync(id, pageId, HttpContext.RequestAborted);
        return deleted ? NoContent() : NotFound();
    }

    // ── POST /api/wiki/export ────────────────────────────────────────────────

    /// <summary>
    /// Exports a wiki as a single downloadable file in Markdown or JSON format.
    /// </summary>
    /// <response code="200">Export file produced — check Content-Disposition for filename.</response>
    /// <response code="400">Missing or invalid WikiId / Format.</response>
    /// <response code="404">Wiki not found.</response>
    [HttpPost("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportWiki([FromBody] WikiExportRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var wiki = await _wikiService.GetWikiByIdAsync(request.WikiId, HttpContext.RequestAborted);
        if (wiki is null)
            return NotFound();

        var pages = wiki.Pages
            .OrderBy(p => p.SortOrder)
            .ToList();

        // Build related-pages map: pageId → list of related WikiPageEntity
        var relatedPages = new Dictionary<Guid, IReadOnlyList<Data.Abstractions.Entities.WikiPageEntity>>();
        foreach (var page in pages)
        {
            var related = await _wikiService.GetRelatedPagesAsync(page.Id, HttpContext.RequestAborted);
            if (related.Count > 0)
                relatedPages[page.Id] = related;
        }

        var format = request.Format.ToLowerInvariant();
        var isMarkdown = format == "markdown";
        var safeName = string.Concat(wiki.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var fileName = isMarkdown ? $"{safeName}.md" : $"{safeName}.json";
        var contentType = isMarkdown ? "text/markdown" : "application/json";

        var outputStream = new MemoryStream();
        if (isMarkdown)
            await _wikiExportService.ExportAsMarkdownAsync(wiki, pages, relatedPages, outputStream, HttpContext.RequestAborted);
        else
            await _wikiExportService.ExportAsJsonAsync(wiki, pages, relatedPages, outputStream, HttpContext.RequestAborted);

        outputStream.Position = 0;

        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = fileName
        }.ToString();

        return File(outputStream, contentType, fileName);
    }
}
