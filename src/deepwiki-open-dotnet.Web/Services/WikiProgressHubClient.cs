using deepwiki_open_dotnet.Web.Models;
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace deepwiki_open_dotnet.Web.Services;

/// <summary>
/// Scoped service that manages a SignalR <see cref="HubConnection"/> to the API service's
/// <c>WikiProgressHub</c> (<c>/hubs/wiki-progress</c>).
///
/// Usage pattern:
/// <code>
///   await hubClient.EnsureConnectedAsync();
///   await hubClient.SubscribeAsync(wikiId, onEvent);
///   // ... component renders ...
///   await hubClient.UnsubscribeAsync(wikiId);
/// </code>
/// </summary>
public sealed class WikiProgressHubClient : IAsyncDisposable
{
    private readonly IHttpMessageHandlerFactory _handlerFactory;
    private readonly ILogger<WikiProgressHubClient> _logger;

    // Named handler registered for Aspire service discovery of "apiservice"
    private const string HandlerName = "apiservice-signalr";

    private HubConnection? _connection;
    private bool _disposed;

    // Active subscriptions: wikiId → (handler disposable, callback)
    private readonly Dictionary<Guid, (IDisposable Handler, Func<WikiGenerationProgressDto, Task> Callback)> _subscriptions = new();

    public WikiProgressHubClient(
        IHttpMessageHandlerFactory handlerFactory,
        ILogger<WikiProgressHubClient> logger)
    {
        _handlerFactory = handlerFactory;
        _logger = logger;
    }

    // ── Connection lifecycle ─────────────────────────────────────────────────

    private HubConnection BuildConnection()
    {
        var conn = new HubConnectionBuilder()
            .WithUrl("https+http://apiservice/hubs/wiki-progress", options =>
            {
                // Route through Aspire's service-discovery handler so that the
                // "https+http://apiservice" scheme is resolved at connection time.
                options.HttpMessageHandlerFactory = _ =>
                    _handlerFactory.CreateHandler(HandlerName);
            })
            .WithAutomaticReconnect()
            .Build();

        // After any automatic reconnect, the server creates a new connection ID and the
        // previous group memberships are gone.  Re-subscribe every currently registered
        // wiki so events are not silently dropped.
        conn.Reconnected += async connectionId =>
        {
            _logger.LogInformation(
                "WikiProgressHub reconnected (ConnectionId={Id}); re-subscribing {Count} group(s).",
                connectionId, _subscriptions.Count);

            foreach (var (wikiId, _) in _subscriptions)
            {
                try
                {
                    await conn.InvokeAsync("Subscribe", (object)wikiId);
                    _logger.LogDebug("Re-subscribed to wiki group after reconnect: {WikiId}", wikiId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Re-subscribe after reconnect failed for wiki {WikiId}.", wikiId);
                }
            }
        };

        return conn;
    }

    /// <summary>
    /// Ensures the hub connection is established. Idempotent — safe to call multiple times.
    /// </summary>
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _connection ??= BuildConnection();

        if (_connection.State == HubConnectionState.Disconnected)
        {
            try
            {
                await _connection.StartAsync(ct);
                _logger.LogInformation("WikiProgressHub connected (ConnectionId={Id})", _connection.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to WikiProgressHub.");
                throw;
            }
        }
    }

    // ── Subscribe / unsubscribe ───────────────────────────────────────────────

    /// <summary>
    /// Subscribes to all progress events for <paramref name="wikiId"/> and invokes
    /// <paramref name="onEvent"/> on each received <see cref="WikiGenerationProgressDto"/>.
    /// Calls the server-side <c>Subscribe</c> hub method to join the SignalR group.
    /// </summary>
    public async Task SubscribeAsync(Guid wikiId, Func<WikiGenerationProgressDto, Task> onEvent, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        // Remove any existing subscription for this wikiId before re-registering
        await UnsubscribeAsync(wikiId);

        // Register the client-side handler for "WikiProgress" messages from this connection.
        // Using async handler so any exception inside onEvent propagates and is logged rather
        // than silently swallowed (which would happen with an async-void Action wrapper).
        var handler = _connection!.On<JsonElement>("WikiProgress", async payload =>
        {
            WikiGenerationProgressDto? dto = null;
            try
            {
                dto = MapToDto(payload);
                if (dto is null)
                {
                    _logger.LogWarning("WikiProgress: MapToDto returned null — raw payload: {Raw}",
                        payload.GetRawText()[..Math.Min(200, payload.GetRawText().Length)]);
                    return;
                }
                _logger.LogDebug("WikiProgress received: eventType={EventType} wikiId={WikiId}",
                    dto.EventType, dto.WikiId);
                await onEvent(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WikiProgress handler faulted for event {EventType} on wiki {WikiId}.",
                    dto?.EventType ?? "?", wikiId);
            }
        });

        _subscriptions[wikiId] = (handler, onEvent);

        // Join the server-side group "wiki:{wikiId}"
        await _connection!.InvokeAsync("Subscribe", (object)wikiId, ct);
        _logger.LogInformation("Subscribed to wiki progress for {WikiId} (ConnectionId={Id})",
            wikiId, _connection!.ConnectionId);
    }

    /// <summary>
    /// Leaves the server-side SignalR group and removes the local handler for <paramref name="wikiId"/>.
    /// </summary>
    public async Task UnsubscribeAsync(Guid wikiId)
    {
        if (_subscriptions.TryGetValue(wikiId, out var sub))
        {
            sub.Handler.Dispose();
            _subscriptions.Remove(wikiId);
        }

        if (_connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("Unsubscribe", (object)wikiId);
                _logger.LogDebug("Unsubscribed from wiki progress for {WikiId}", wikiId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unsubscribe call failed for wiki {WikiId} — ignoring.", wikiId);
            }
        }
    }

    // ── JSON → DTO mapping ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static WikiGenerationProgressDto? MapToDto(JsonElement element)
    {
        var raw = element.GetRawText();
        return JsonSerializer.Deserialize<WikiGenerationProgressDto>(raw, _jsonOptions);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (handler, _) in _subscriptions.Values)
            handler.Dispose();
        _subscriptions.Clear();

        if (_connection is not null)
        {
            try { await _connection.StopAsync(); }
            catch { /* best effort */ }
            await _connection.DisposeAsync();
        }
    }
}
