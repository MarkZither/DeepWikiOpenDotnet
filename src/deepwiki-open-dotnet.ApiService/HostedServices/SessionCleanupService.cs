using DeepWiki.Rag.Core.Services;

namespace deepwiki_open_dotnet.ApiService.HostedServices;

/// <summary>
/// Background service that periodically cleans up expired sessions.
/// Runs every 5 minutes by default.
/// </summary>
public class SessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    public SessionCleanupService(
        IServiceProvider serviceProvider,
        ILogger<SessionCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();

                var beforeCount = sessionManager.GetActiveSessions().Count;
                sessionManager.CleanupExpiredSessions();
                var afterCount = sessionManager.GetActiveSessions().Count;
                var removed = beforeCount - afterCount;

                if (removed > 0)
                {
                    _logger.LogInformation(
                        "Cleaned up {RemovedCount} expired sessions (active: {ActiveCount})",
                        removed, afterCount);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }

        _logger.LogInformation("Session cleanup service stopped");
    }
}
