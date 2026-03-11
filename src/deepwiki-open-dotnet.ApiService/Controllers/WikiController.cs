using DeepWiki.ApiService.Models;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using Microsoft.AspNetCore.Mvc;
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

    public WikiController(IWikiService wikiService, IWikiGenerationService wikiGenerationService)
    {
        _wikiService = wikiService;
        _wikiGenerationService = wikiGenerationService;
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
    /// Starts wiki generation for a collection and streams progress events via NDJSON.
    /// Each newline-delimited JSON object is a <c>WikiGenerationProgress</c> event.
    /// </summary>
    /// <response code="200">Generation stream started.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="409">A wiki with this name is already being generated for this collection.</response>
    [HttpPost("generate")]
    [Produces("application/x-ndjson")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task GenerateWiki([FromBody] GenerateWikiRequest request)
    {
        if (!ModelState.IsValid)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var coreRequest = new WikiGenerationRequest
        {
            CollectionId = request.CollectionId,
            Name = request.Name,
            Description = request.Description
        };

        IAsyncEnumerable<WikiGenerationProgress> stream;
        try
        {
            stream = _wikiGenerationService.GenerateAsync(coreRequest, HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await HttpContext.Response.WriteAsJsonAsync(new { detail = ex.Message });
            return;
        }

        HttpContext.Response.ContentType = "application/x-ndjson";
        HttpContext.Response.Headers.CacheControl = "no-cache";

        try
        {
            await foreach (var progress in stream.WithCancellation(HttpContext.RequestAborted))
            {
                var line = JsonSerializer.Serialize(progress) + "\n";
                await HttpContext.Response.WriteAsync(line, HttpContext.RequestAborted);
                await HttpContext.Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — graceful exit; orchestrator handles cancellation internally
        }
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
}
