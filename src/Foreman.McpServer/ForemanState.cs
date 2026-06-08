using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Models;
using System.Collections.Concurrent;

namespace Foreman.McpServer;

/// <summary>
/// Shared in-process state bag threadsafely accessed by MCP tools and the alert dispatcher.
/// </summary>
public sealed class ForemanState : IEventSink
{
    private readonly ConcurrentQueue<ForemanEvent> _eventLog = new();
    private readonly ConcurrentDictionary<string, ForemanEvent> _alertById = new();
    private const int MaxEvents = 1000;

    public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

    // injected from MonitorService / BehaviorTracker after construction
    public Func<IEnumerable<ProcessRecord>>?       GetProcessSnapshot  { get; set; }
    public Func<IEnumerable<BehaviorProfile>>?     GetBehaviorProfiles { get; set; }

    /// <summary>Resets behavioral metrics for a specific harness ID. Returns false if not found.</summary>
    public Action<string>? ResetBehaviorProfile { get; set; }

    public int ActiveAlerts => _alertById.Values.Count(e => !e.Acknowledged);
    public bool HasCritical => _alertById.Values.Any(e => !e.Acknowledged && e.Severity >= ForemanSeverity.High);
    public int ProcessCount => GetProcessSnapshot?.Invoke().Count() ?? 0;

    void IEventSink.OnEvent(ForemanEvent evt)
    {
        _alertById[evt.Id] = evt;
        _eventLog.Enqueue(evt);

        // prune old events
        while (_eventLog.Count > MaxEvents)
            _eventLog.TryDequeue(out _);
    }

    public void AddEvent(ForemanEvent evt) => ((IEventSink)this).OnEvent(evt);

    public bool AcknowledgeAlert(string alertId)
    {
        if (!_alertById.TryGetValue(alertId, out var evt)) return false;
        evt.Acknowledged = true;
        return true;
    }

    public IEnumerable<ProcessRecord> GetProcesses(bool includeChildren) =>
        GetProcessSnapshot?.Invoke()
            .Where(p => includeChildren || p.IsHarness) ?? [];

    public ProcessRecord? GetProcess(int pid) =>
        GetProcessSnapshot?.Invoke().FirstOrDefault(p => p.Pid == pid);

    public IEnumerable<object> GetEvents(int limit, ForemanSeverity? minSeverity)
    {
        return _eventLog
            .Where(e => minSeverity is null || e.Severity >= minSeverity)
            .TakeLast(limit)
            .Select(e => new
            {
                id        = e.Id,
                timestamp = e.Timestamp,
                severity  = e.Severity.ToString(),
                source    = e.Source,
                message   = e.Message,
                acked     = e.Acknowledged,
            });
    }
}
