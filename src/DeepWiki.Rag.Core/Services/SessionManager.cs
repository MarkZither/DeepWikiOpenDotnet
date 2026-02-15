using System.Collections.Concurrent;
using DeepWiki.Rag.Core.Models;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Manages session and prompt state in-memory using thread-safe concurrent collections.
/// For MVP, sessions are not persisted. Future: migrate to database for session history.
/// </summary>
public class SessionManager
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Prompt>> _promptsBySession = new();
    private readonly ConcurrentDictionary<string, string> _idempotencyKeys = new(); // idempotencyKey -> promptId

    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(1);

    /// <summary>
    /// Creates a new session with the specified owner.
    /// </summary>
    /// <param name="owner">Optional owner identifier.</param>
    /// <returns>Newly created session.</returns>
    public Session CreateSession(string? owner = null)
    {
        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            Owner = owner,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout),
            Status = SessionStatus.Active
        };

        _sessions[session.SessionId] = session;
        _promptsBySession[session.SessionId] = new ConcurrentDictionary<string, Prompt>();

        return session;
    }

    /// <summary>
    /// Retrieves a session by ID.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Session if found, null otherwise.</returns>
    public Session? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Updates session last activity timestamp.
    /// </summary>
    public void UpdateSessionActivity(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastActiveAt = DateTime.UtcNow;
            session.ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout);
        }
    }

    /// <summary>
    /// Creates a new prompt within a session.
    /// </summary>
    /// <param name="sessionId">Parent session ID.</param>
    /// <param name="text">Prompt text.</param>
    /// <param name="idempotencyKey">Optional idempotency key.</param>
    /// <returns>Newly created prompt, or existing prompt if idempotency key matches.</returns>
    /// <exception cref="ArgumentException">Thrown if session not found or inactive.</exception>
    public Prompt CreatePrompt(string sessionId, string text, string? idempotencyKey = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Status != SessionStatus.Active)
        {
            throw new ArgumentException($"Session {sessionId} not found or inactive", nameof(sessionId));
        }

        // Check idempotency key
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var cacheKey = $"{sessionId}:{idempotencyKey}";
            if (_idempotencyKeys.TryGetValue(cacheKey, out var existingPromptId))
            {
                // Return cached prompt
                if (_promptsBySession[sessionId].TryGetValue(existingPromptId, out var existingPrompt))
                {
                    return existingPrompt;
                }
            }
        }

        var prompt = new Prompt
        {
            PromptId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Text = text,
            IdempotencyKey = idempotencyKey,
            Status = PromptStatus.InFlight,
            CreatedAt = DateTime.UtcNow,
            TokenCount = 0
        };

        _promptsBySession[sessionId][prompt.PromptId] = prompt;

        // Cache idempotency key
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var cacheKey = $"{sessionId}:{idempotencyKey}";
            _idempotencyKeys[cacheKey] = prompt.PromptId;
        }

        UpdateSessionActivity(sessionId);

        return prompt;
    }

    /// <summary>
    /// Retrieves a prompt by ID within a session.
    /// </summary>
    public Prompt? GetPrompt(string sessionId, string promptId)
    {
        if (_promptsBySession.TryGetValue(sessionId, out var prompts))
        {
            prompts.TryGetValue(promptId, out var prompt);
            return prompt;
        }
        return null;
    }

    /// <summary>
    /// Updates prompt status.
    /// </summary>
    public void UpdatePromptStatus(string sessionId, string promptId, PromptStatus status, int tokenCount = 0)
    {
        var prompt = GetPrompt(sessionId, promptId);
        if (prompt != null)
        {
            prompt.Status = status;
            prompt.TokenCount = tokenCount;
        }
    }

    /// <summary>
    /// Gets all active sessions (for monitoring/cleanup).
    /// </summary>
    public IReadOnlyDictionary<string, Session> GetActiveSessions()
    {
        return _sessions;
    }

    /// <summary>
    /// Cleans up expired sessions (should be called periodically by background task).
    /// </summary>
    public void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var expiredSessions = _sessions.Where(kvp => kvp.Value.ExpiresAt < now).ToList();

        foreach (var (sessionId, _) in expiredSessions)
        {
            _sessions.TryRemove(sessionId, out _);
            _promptsBySession.TryRemove(sessionId, out _);
            
            // Remove idempotency keys for this session
            var keysToRemove = _idempotencyKeys.Where(kvp => kvp.Key.StartsWith($"{sessionId}:")).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                _idempotencyKeys.TryRemove(key, out _);
            }
        }
    }
}

