using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foreman.McpServer;

/// <summary>
/// All tools exposed to AI harnesses via MCP.
/// The state bag is injected at server startup and shared across requests.
/// </summary>
[McpServerToolType]
public static class ForemanMcpTools
{
    private static ForemanState? _state;

    internal static void SetState(ForemanState state) => _state = state;

    [McpServerTool, Description("Returns Foreman's current overall health summary.")]
    public static object ForemanStatus()
    {
        var state = _state ?? new ForemanState();
        return new
        {
            status = state.ActiveAlerts == 0 ? "green" : state.HasCritical ? "red" : "amber",
            activeAlerts = state.ActiveAlerts,
            monitoredProcesses = state.ProcessCount,
            uptimeSeconds = (int)(DateTimeOffset.UtcNow - state.StartTime).TotalSeconds,
            version = "0.1.0",
        };
    }

    [McpServerTool, Description("Lists all AI harness processes Foreman is monitoring.")]
    public static object ListMonitoredProcesses(
        [Description("Include child processes of harnesses")] bool includeChildren = true)
    {
        var state = _state ?? new ForemanState();
        var procs = state.GetProcesses(includeChildren);
        return new { processes = procs };
    }

    [McpServerTool, Description("Returns detailed information about a specific process by PID.")]
    public static object QueryProcessDetail([Description("Process ID to query")] int pid)
    {
        var state = _state ?? new ForemanState();
        var proc = state.GetProcess(pid);
        if (proc is null) return new { error = $"No process found with PID {pid}" };

        return new
        {
            pid = proc.Pid,
            name = proc.Name,
            commandLine = proc.CommandLine,
            state = proc.State.ToString(),
            uptimeMinutes = proc.UptimeMinutes,
            silentMinutes = proc.SilentMinutes,
            isHarness = proc.IsHarness,
            harnessType = proc.HarnessType,
            profileName = proc.ProfileName,
            ioCountersUnavailable = proc.IoCountersUnavailable,
        };
    }

    [McpServerTool, Description("Marks a Foreman alert as acknowledged, suppressing further notifications for it.")]
    public static object AcknowledgeAlert(
        [Description("The alert ID to acknowledge")] string alertId,
        [Description("Optional reason for acknowledgement")] string? reason = null)
    {
        var state = _state ?? new ForemanState();
        var acknowledged = state.AcknowledgeAlert(alertId);
        return new { acknowledged, reason };
    }

    [McpServerTool, Description("Returns recent Foreman events from the event log.")]
    public static object ListRecentEvents(
        [Description("Maximum number of events to return")] int limit = 50,
        [Description("Filter by minimum severity: Info, Low, Medium, High, Critical")] string? severity = null)
    {
        var state = _state ?? new ForemanState();
        ForemanSeverity? minSev = severity is not null
            ? Enum.TryParse<ForemanSeverity>(severity, true, out var s) ? s : null
            : null;

        var events = state.GetEvents(limit, minSev);
        return new { events };
    }

    [McpServerTool, Description("Pre-flight check a command line. Foreman heuristically evaluates it and returns allow/block/escalate.")]
    public static object ReportSuspiciousCommand(
        [Description("The command line to evaluate")] string commandLine,
        [Description("What the harness is trying to accomplish")] string context = "")
    {
        var match = Core.Heuristics.CommandAnalyzer.Instance.Analyze(commandLine);
        if (match is null)
            return new { decision = "allow", reason = "No heuristic rules matched", matchedRule = (string?)null };

        var decision = match.Severity switch
        {
            ForemanSeverity.Critical => "block",
            ForemanSeverity.High     => "escalate",
            _                        => "allow_once",
        };

        // log the check itself as an event
        _state?.AddEvent(new CommandAlertEvent(
            DateTimeOffset.UtcNow,
            match.Severity,
            "MCP.ReportSuspiciousCommand",
            $"Harness pre-checked command [{match.RuleId}]: {commandLine[..Math.Min(80, commandLine.Length)]}",
            commandLine,
            match.RuleId,
            match.RuleName,
            match.Description,
            match.Guidance,
            0
        ));

        return new
        {
            decision,
            reason = match.RuleName,
            matchedRule = match.RuleId,
            severity = match.Severity.ToString(),
        };
    }

    [McpServerTool, Description(
        "Returns behavioral escalation metrics for every monitored harness. " +
        "Levels: Watch (0) → Alert (1) → Alarm (2) → Emergency (3). " +
        "Use this to check whether Foreman has raised an alarm about you or a sibling harness.")]
    public static object GetBehaviorMetrics()
    {
        var state    = _state ?? new ForemanState();
        var profiles = state.GetBehaviorProfiles?.Invoke() ?? [];

        return new
        {
            harnesses = profiles.Select(p => new
            {
                harnessId             = p.HarnessId,
                displayName           = p.DisplayName,
                escalationLevel       = p.CurrentLevel.ToString().ToLowerInvariant(),
                escalationLevelNum    = (int)p.CurrentLevel,
                totalAlerts           = p.TotalAlerts,
                uniqueRules           = p.UniqueRulesCount,
                categories            = p.Categories.ToArray(),
                sessionDurationSeconds = (int)p.SessionDuration.TotalSeconds,
            }).OrderByDescending(h => h.escalationLevelNum).ThenByDescending(h => h.totalAlerts).ToArray(),
        };
    }

    [McpServerTool, Description(
        "Resets behavioral escalation metrics for a specific harness back to the Watch level. " +
        "Call this when starting a new, unrelated task to prevent prior session activity from " +
        "contributing to escalation thresholds. Pass your own harnessId (e.g. 'claude-code').")]
    public static object ResetBehaviorMetrics(
        [Description("The harness ID to reset, e.g. 'claude-code', 'codex', or 'proc:node'")] string harnessId)
    {
        var state = _state ?? new ForemanState();
        state.ResetBehaviorProfile?.Invoke(harnessId);

        // log the reset as an info event so it's visible in the log window
        EventBus.Instance.Publish(new InfoEvent(
            DateTimeOffset.UtcNow,
            "MCP.ResetBehaviorMetrics",
            $"Behavior metrics reset for '{harnessId}' via MCP tool call."));

        return new { reset = true, harnessId };
    }

    [McpServerTool, Description(
        "Announces that the harness is starting a new task. Foreman logs the announcement " +
        "in the event log so operators can correlate task boundaries with alert patterns. " +
        "Optionally resets behavioral metrics if this is a fresh, unrelated task.")]
    public static object ReportTaskStart(
        [Description("Human-readable description of the new task")] string taskDescription,
        [Description("Reset behavioral escalation metrics for a fresh start")] bool resetMetrics = false,
        [Description("Harness ID for metric reset (only used when resetMetrics=true), e.g. 'claude-code'")] string? harnessId = null)
    {
        EventBus.Instance.Publish(new InfoEvent(
            DateTimeOffset.UtcNow,
            "MCP.TaskStart",
            $"New task announced: {taskDescription[..Math.Min(120, taskDescription.Length)]}"));

        if (resetMetrics && harnessId is not null)
        {
            var state = _state ?? new ForemanState();
            state.ResetBehaviorProfile?.Invoke(harnessId);

            EventBus.Instance.Publish(new InfoEvent(
                DateTimeOffset.UtcNow,
                "MCP.TaskStart",
                $"Behavior metrics reset for '{harnessId}' at task boundary."));
        }

        return new
        {
            acknowledged = true,
            taskDescription,
            metricsReset = resetMetrics && harnessId is not null,
        };
    }

    [McpServerTool, Description("Returns the permission profile that applies to the calling harness.")]
    public static object GetMyPermissions()
    {
        // In a full implementation this would identify the caller's process via connection metadata.
        // For now, return the claude-code-default profile summary.
        return new
        {
            profileName = "claude-code-default",
            note = "Full per-caller identification requires MCP session metadata — coming in Phase 5",
        };
    }
}
