using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace DeepWiki.ApiService.Hubs;

/// <summary>
/// SignalR hub for streaming RAG generation with bidirectional communication.
/// Provides contract parity with HTTP NDJSON baseline transport.
/// </summary>
public class GenerationHub : Hub
{
    private readonly IGenerationService _generationService;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<GenerationHub> _logger;

    public GenerationHub(
        IGenerationService generationService,
        SessionManager sessionManager,
        ILogger<GenerationHub> logger)
    {
        _generationService = generationService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new generation session.
    /// Task T066: Implement GenerationHub.StartSession method.
    /// </summary>
    /// <param name="request">Session creation request with optional owner</param>
    /// <returns>Session response with sessionId</returns>
    public SessionResponse StartSession(SessionRequest request)
    {
        _logger.LogInformation("SignalR: Creating session for owner {Owner}", request?.Owner ?? "anonymous");
        
        var session = _sessionManager.CreateSession(request?.Owner);
        
        return new SessionResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Streams generation deltas for a prompt using RAG retrieval and LLM generation.
    /// Task T067: Implement GenerationHub.SendPrompt streaming method.
    /// Returns IAsyncEnumerable for server-to-client streaming.
    /// </summary>
    /// <param name="request">Prompt request with sessionId, prompt text, retrieval parameters</param>
    /// <param name="cancellationToken">Cancellation token from client or hub lifetime</param>
    /// <returns>Async stream of GenerationDelta events</returns>
    public async IAsyncEnumerable<GenerationDelta> SendPrompt(
        PromptRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.SessionId))
        {
            _logger.LogWarning("SignalR: SendPrompt called with missing sessionId");
            yield return new GenerationDelta
            {
                PromptId = Guid.NewGuid().ToString(),
                Type = "error",
                Seq = 0,
                Role = "assistant",
                Metadata = new Dictionary<string, object>
                {
                    ["error"] = "SessionId is required"
                }
            };
            yield break;
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            _logger.LogWarning("SignalR: SendPrompt called with empty prompt for session {SessionId}", request.SessionId);
            yield return new GenerationDelta
            {
                PromptId = Guid.NewGuid().ToString(),
                Type = "error",
                Seq = 0,
                Role = "assistant",
                Metadata = new Dictionary<string, object>
                {
                    ["error"] = "Prompt text is required"
                }
            };
            yield break;
        }

        var session = _sessionManager.GetSession(request.SessionId);
        if (session == null)
        {
            _logger.LogWarning("SignalR: Session {SessionId} not found", request.SessionId);
            yield return new GenerationDelta
            {
                PromptId = Guid.NewGuid().ToString(),
                Type = "error",
                Seq = 0,
                Role = "assistant",
                Metadata = new Dictionary<string, object>
                {
                    ["error"] = "Session not found"
                }
            };
            yield break;
        }

        _logger.LogInformation("SignalR: Streaming prompt for session {SessionId}", request.SessionId);

        await foreach (var delta in _generationService.GenerateAsync(
            request.SessionId,
            request.Prompt,
            request.TopK,
            request.Filters,
            request.IdempotencyKey,
            cancellationToken))
        {
            yield return delta;
        }
    }

    /// <summary>
    /// Cancels an in-flight prompt generation.
    /// Task T068: Implement GenerationHub.Cancel method.
    /// </summary>
    /// <param name="request">Cancel request with sessionId and promptId</param>
    /// <returns>Task representing the async cancellation operation</returns>
    public async Task Cancel(CancelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.SessionId))
        {
            _logger.LogWarning("SignalR: Cancel called with missing sessionId");
            throw new HubException("SessionId is required");
        }

        if (string.IsNullOrWhiteSpace(request.PromptId))
        {
            _logger.LogWarning("SignalR: Cancel called with missing promptId");
            throw new HubException("PromptId is required");
        }

        _logger.LogInformation("SignalR: Cancelling prompt {PromptId} in session {SessionId}", 
            request.PromptId, request.SessionId);

        await _generationService.CancelAsync(request.SessionId, request.PromptId);
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR: Client connected - ConnectionId: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "SignalR: Client disconnected with error - ConnectionId: {ConnectionId}", 
                Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("SignalR: Client disconnected - ConnectionId: {ConnectionId}", 
                Context.ConnectionId);
        }
        
        return base.OnDisconnectedAsync(exception);
    }
}
