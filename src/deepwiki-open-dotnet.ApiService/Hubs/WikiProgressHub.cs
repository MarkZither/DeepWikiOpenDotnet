using Microsoft.AspNetCore.SignalR;

namespace DeepWiki.ApiService.Hubs;

/// <summary>
/// SignalR hub that delivers live wiki generation progress events to browser clients.
///
/// Flow:
///   1. Client connects and calls Subscribe(wikiId).
///   2. WikiGenerationOrchestrator calls IWikiProgressNotifier.NotifyAsync on every progress event.
///   3. SignalRWikiProgressNotifier broadcasts to the group "wiki:{wikiId}".
///   4. Client receives "WikiProgress" messages and updates the UI incrementally.
///   5. Client calls Unsubscribe(wikiId) on completion or navigation away.
/// </summary>
public class WikiProgressHub : Hub
{
    private readonly ILogger<WikiProgressHub> _logger;

    public WikiProgressHub(ILogger<WikiProgressHub> logger)
    {
        _logger = logger;
    }

    /// <summary>Subscribes the current connection to progress events for the given wiki.</summary>
    public async Task Subscribe(Guid wikiId)
    {
        var group = GroupName(wikiId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _logger.LogDebug("WikiProgressHub: {ConnectionId} subscribed to {Group}", Context.ConnectionId, group);
    }

    /// <summary>Unsubscribes the current connection from progress events for the given wiki.</summary>
    public async Task Unsubscribe(Guid wikiId)
    {
        var group = GroupName(wikiId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _logger.LogDebug("WikiProgressHub: {ConnectionId} unsubscribed from {Group}", Context.ConnectionId, group);
    }

    /// <summary>Stable group name key for a wiki ID.</summary>
    public static string GroupName(Guid wikiId) => $"wiki:{wikiId}";
}
