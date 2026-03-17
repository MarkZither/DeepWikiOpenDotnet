using DeepWiki.ApiService.Hubs;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace DeepWiki.ApiService.Services;

/// <summary>
/// <see cref="IWikiProgressNotifier"/> implementation that broadcasts wiki generation
/// progress events through <see cref="WikiProgressHub"/> to all subscribed SignalR clients.
///
/// Clients join the group "wiki:{wikiId}" by calling Hub.Subscribe(wikiId) and receive
/// the client-side method "WikiProgress" with a <see cref="WikiGenerationProgress"/> payload.
/// </summary>
public class SignalRWikiProgressNotifier : IWikiProgressNotifier
{
    private readonly IHubContext<WikiProgressHub> _hubContext;
    private readonly ILogger<SignalRWikiProgressNotifier> _logger;

    public SignalRWikiProgressNotifier(
        IHubContext<WikiProgressHub> hubContext,
        ILogger<SignalRWikiProgressNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task NotifyAsync(WikiGenerationProgress progress, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients
                .Group(WikiProgressHub.GroupName(progress.WikiId))
                .SendAsync("WikiProgress", progress, ct);
        }
        catch (Exception ex)
        {
            // Never let a notification failure surface to the caller.
            _logger.LogWarning(ex,
                "SignalR notification failed for event {EventType} on wiki {WikiId}.",
                progress.EventType, progress.WikiId);
        }
    }
}
