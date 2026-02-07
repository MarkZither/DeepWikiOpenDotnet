using System.Text.Json;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;

namespace DeepWiki.ApiService.Controllers;

[ApiController]
[Route("api/generation")]
public class GenerationController : ControllerBase
{
    private readonly IGenerationService _generationService;
    private readonly DeepWiki.Rag.Core.Services.SessionManager _sessionManager;

    public GenerationController(IGenerationService generationService, DeepWiki.Rag.Core.Services.SessionManager sessionManager)
    {
        _generationService = generationService;
        _sessionManager = sessionManager;
    }

    [HttpPost("/session")]
    public ActionResult<SessionResponse> CreateSession([FromBody] SessionRequest req)
    {
        var session = _sessionManager.CreateSession(req?.Owner);
        return Created(string.Empty, new SessionResponse { SessionId = session.SessionId });
    }

    [HttpPost("/stream")]
    public async Task StreamGeneration([FromBody] PromptRequest req)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid request");
            return;
        }

        var session = _sessionManager.GetSession(req.SessionId);
        if (session == null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsync("Session not found");
            return;
        }

        Response.ContentType = "application/x-ndjson";

        try
        {
            await foreach (var d in _generationService.GenerateAsync(req.SessionId, req.Prompt, req.TopK, req.Filters, req.IdempotencyKey, HttpContext.RequestAborted))
            {
                var json = JsonSerializer.Serialize(d);
                await Response.WriteAsync(json + "\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Client cancelled - just return
        }
    }

    [HttpPost("/cancel")]
    public async Task<IActionResult> CancelGeneration([FromBody] CancelRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _generationService.CancelAsync(req.SessionId, req.PromptId);
        return Ok();
    }
}
