using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Mcp;
using Foreman.Core.Models;
using Foreman.Core.Profiles;
using Foreman.Core.Settings;
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
    public int McpPort { get; set; } = 54321;
    public LlmTriageSettings LlmTriage { get; set; } = new();

    // injected from MonitorService / BehaviorTracker after construction
    public Func<IEnumerable<ProcessRecord>>?       GetProcessSnapshot  { get; set; }
    public Func<IEnumerable<BehaviorProfile>>?     GetBehaviorProfiles { get; set; }
    public Func<string, HarnessProfile?>?          GetProfileByName    { get; set; }
    public Func<string, string?>?                  GetDefaultProfileNameByHarnessId { get; set; }
    public Func<int, ProcessRecord?>?              FindHarnessAncestorByPid { get; set; }
    public Func<int>?                              GetMcpSessionCount { get; set; }
    public Func<IEnumerable<McpServerEntry>>?      GetMcpInventory    { get; set; }

    /// <summary>Resets behavioral metrics for a specific harness ID. Returns false if not found.</summary>
    public Action<string>? ResetBehaviorProfile { get; set; }

    public int ActiveAlerts => _alertById.Values.Count(e => !e.Acknowledged);
    public bool HasCritical => _alertById.Values.Any(e => !e.Acknowledged && e.Severity >= ForemanSeverity.High);
    public int ProcessCount => GetProcessSnapshot?.Invoke().Count() ?? 0;
    public int McpSessionCount => GetMcpSessionCount?.Invoke() ?? 0;

    void IEventSink.OnEvent(ForemanEvent evt)
    {
        if (evt.Severity > ForemanSeverity.Info)
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

    /// <summary>Returns the tracked alert with the given id, or null. Used to gate ack by severity.</summary>
    public ForemanEvent? GetAlert(string alertId) =>
        _alertById.TryGetValue(alertId, out var evt) ? evt : null;

    /// <summary>Current escalation level for a harness, or null if it has no profile yet.</summary>
    public EscalationLevel? GetEscalationLevel(string harnessId) =>
        GetBehaviorProfiles?.Invoke()
            .FirstOrDefault(p => string.Equals(p.HarnessId, harnessId, StringComparison.OrdinalIgnoreCase))
            ?.CurrentLevel;

    public IEnumerable<ProcessRecord> GetProcesses(bool includeChildren) =>
        GetProcessSnapshot?.Invoke()
            .Where(p => includeChildren || p.IsHarness) ?? [];

    public ProcessRecord? GetProcess(int pid) =>
        GetProcessSnapshot?.Invoke().FirstOrDefault(p => p.Pid == pid);

    public IEnumerable<ProcessRecord> GetProcessesForHarness(string harnessId, bool includeChildren)
    {
        var snapshot = GetProcessSnapshot?.Invoke().ToList() ?? [];
        if (!includeChildren)
        {
            return snapshot.Where(p =>
                string.Equals(p.HarnessType, harnessId, StringComparison.OrdinalIgnoreCase));
        }

        return snapshot.Where(p =>
            string.Equals(p.HarnessType, harnessId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(FindHarnessAncestorByPid?.Invoke(p.Pid)?.HarnessType, harnessId, StringComparison.OrdinalIgnoreCase));
    }

    public HarnessProfile? ResolveProfile(string? profileName, string? harnessId, int? processId)
    {
        if (!string.IsNullOrWhiteSpace(profileName) && GetProfileByName?.Invoke(profileName) is { } explicitProfile)
            return explicitProfile;

        if (processId is int pid)
        {
            var proc = GetProcess(pid);
            if (proc?.ProfileName is not null && GetProfileByName?.Invoke(proc.ProfileName) is { } processProfile)
                return processProfile;

            var ancestor = FindHarnessAncestorByPid?.Invoke(pid);
            if (ancestor?.ProfileName is not null && GetProfileByName?.Invoke(ancestor.ProfileName) is { } ancestorProfile)
                return ancestorProfile;
        }

        if (!string.IsNullOrWhiteSpace(harnessId) &&
            GetDefaultProfileNameByHarnessId?.Invoke(harnessId) is { } defaultProfileName)
        {
            return GetProfileByName?.Invoke(defaultProfileName);
        }

        return null;
    }

    public string? ResolveHarnessId(string? harnessId, int? processId)
    {
        if (!string.IsNullOrWhiteSpace(harnessId)) return harnessId;

        if (processId is int pid)
        {
            var proc = GetProcess(pid);
            if (proc?.HarnessType is not null) return proc.HarnessType;
            var ancestor = FindHarnessAncestorByPid?.Invoke(pid);
            if (ancestor?.HarnessType is not null) return ancestor.HarnessType;
        }

        return null;
    }

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
