using Microsoft.AspNetCore.Components.Server.Circuits;

namespace deepwiki_open_dotnet.Web.Services;

/// <summary>
/// Logs Blazor Server circuit lifecycle events so that silent failures
/// (e.g. SignalR message-size exceeded, unhandled component exceptions)
/// are always visible in the Aspire dashboard / application logs.
/// </summary>
public sealed class CircuitErrorLogger : CircuitHandler
{
    private readonly ILogger<CircuitErrorLogger> _logger;

    public CircuitErrorLogger(ILogger<CircuitErrorLogger> logger)
        => _logger = logger;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        _logger.LogDebug("Circuit opened: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken ct)
    {
        _logger.LogDebug("Circuit connection up: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken ct)
    {
        // Connection-down fires when the WebSocket drops — including when
        // SignalR rejects an oversized frame (MaximumReceiveMessageSize exceeded).
        // The Aspire dashboard will show this even if the browser shows nothing.
        _logger.LogWarning(
            "Circuit connection lost: {CircuitId}. " +
            "If this follows a large file selection, check SignalR MaximumReceiveMessageSize. " +
            "Current limit is configured in Program.cs AddSignalR().",
            circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        _logger.LogDebug("Circuit closed: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }
}
