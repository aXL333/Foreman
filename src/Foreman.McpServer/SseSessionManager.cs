// Alias avoids the Foreman.McpServer namespace shadowing ModelContextProtocol.Server.McpServer
using McpServerType = global::ModelContextProtocol.Server.McpServer;
using System.Collections.Concurrent;

namespace Foreman.McpServer;

/// <summary>
/// Tracks live MCP SSE sessions and broadcasts server-initiated notifications
/// (notifications/message) to all connected clients.
/// </summary>
public sealed class SseSessionManager
{
    private readonly ConcurrentDictionary<string, McpServerType> _sessions = new();

    public int Count => _sessions.Count;

    /// <summary>Registers a connected session. Returns the session key for later unregistration.</summary>
    public string Register(McpServerType server)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _sessions[id] = server;
        return id;
    }

    public void Unregister(string id) => _sessions.TryRemove(id, out _);

    /// <summary>
    /// Pushes notifications/message to every currently-connected MCP client.
    /// Failures on individual sessions are swallowed so a dead client can't block others.
    /// </summary>
    public async Task BroadcastNotificationAsync(string level, string logger, object data)
    {
        if (_sessions.IsEmpty) return;

        var tasks = _sessions.Values.Select(server =>
            server.SendNotificationAsync("notifications/message", new { level, logger, data })
                  .ContinueWith(t => { /* swallow per-client errors */ }, TaskScheduler.Default));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
