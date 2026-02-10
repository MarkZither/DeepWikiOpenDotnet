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

    [HttpPost("session")]
    public ActionResult<SessionResponse> CreateSession([FromBody] SessionRequest req)
    {
        var session = _sessionManager.CreateSession(req?.Owner);
        return Created(string.Empty, new SessionResponse { SessionId = session.SessionId });
    }

    [HttpPost("stream")]
    public async Task StreamGeneration([FromBody] PromptRequest req)
    {
        if (!ModelState.IsValid)
        {
            // Return a structured ErrorResponse for invalid requests
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            var detail = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            var err = new DeepWiki.ApiService.Models.ErrorResponse { Detail = detail };
            var json = JsonSerializer.Serialize(err);
            await Response.WriteAsync(json + "\n");
            await Response.Body.FlushAsync();
            return;
        }

        var session = _sessionManager.GetSession(req.SessionId);
        if (session == null)
        {
            Response.StatusCode = 404;
            Response.ContentType = "application/json";
            var err = new DeepWiki.ApiService.Models.ErrorResponse { Detail = "Session not found" };
            var json = JsonSerializer.Serialize(err);
            await Response.WriteAsync(json + "\n");
            await Response.Body.FlushAsync();
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
        catch (Exception ex)
        {
            // Convert unexpected exceptions into a structured error delta so streaming clients get a usable event
            var err = new DeepWiki.Data.Abstractions.Models.GenerationDelta
            {
                PromptId = req.SessionId,
                Type = "error",
                Role = "assistant",
                Seq = 0,
                Metadata = new { code = "internal_error", message = ex.Message }
            };

            var json = JsonSerializer.Serialize(err);
            await Response.WriteAsync(json + "\n");
            await Response.Body.FlushAsync();
        }
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelGeneration([FromBody] CancelRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _generationService.CancelAsync(req.SessionId, req.PromptId);
        return Ok();
    }
}
