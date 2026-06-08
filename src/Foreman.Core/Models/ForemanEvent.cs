using Foreman.Core.Behavior;

namespace Foreman.Core.Models;

public abstract record ForemanEvent(
    DateTimeOffset Timestamp,
    ForemanSeverity Severity,
    string Source,
    string Message
)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public bool Acknowledged { get; set; }
}

public sealed record CommandAlertEvent(
    DateTimeOffset Timestamp,
    ForemanSeverity Severity,
    string Source,
    string Message,
    string CommandLine,
    string RuleId,
    string RuleName,
    string RuleDescription,
    string RuleGuidance,
    int ProcessId
) : ForemanEvent(Timestamp, Severity, Source, Message);

public sealed record HangDetectedEvent(
    DateTimeOffset Timestamp,
    string Source,
    string Message,
    int ProcessId,
    string ProcessName,
    int UptimeMinutes,
    int SilentMinutes,
    int? ParentHarnessPid
) : ForemanEvent(Timestamp, ForemanSeverity.High, Source, Message);

public sealed record OrphanDetectedEvent(
    DateTimeOffset Timestamp,
    string Source,
    string Message,
    int ProcessId,
    string ProcessName,
    int DeadParentPid,
    string DeadParentName,
    int UptimeMinutes
) : ForemanEvent(Timestamp, ForemanSeverity.Medium, Source, Message);

public sealed record PermissionViolationEvent(
    DateTimeOffset Timestamp,
    string Source,
    string Message,
    int ProcessId,
    string ProfileName,
    string ViolationType,
    string Detail
) : ForemanEvent(Timestamp, ForemanSeverity.High, Source, Message);

public sealed record NonzeroExitEvent(
    DateTimeOffset Timestamp,
    string Source,
    string Message,
    int ProcessId,
    string ProcessName,
    int ExitCode,
    int? ParentHarnessPid
) : ForemanEvent(Timestamp, ForemanSeverity.Low, Source, Message);

/// <summary>Informational / lifecycle event — startup, scan complete, status changes.</summary>
public sealed record InfoEvent(
    DateTimeOffset Timestamp,
    string Source,
    string Message
) : ForemanEvent(Timestamp, ForemanSeverity.Info, Source, Message);

/// <summary>
/// Published by BehaviorTracker whenever a harness crosses an escalation threshold.
/// The level only increases within a session (reset clears it).
/// </summary>
public sealed record EscalationEvent(
    DateTimeOffset Timestamp,
    EscalationLevel NewLevel,
    EscalationLevel OldLevel,
    string HarnessId,
    string HarnessDisplayName,
    string Reason,
    int TotalAlerts,
    int UniqueRules,
    int CategoryCount,
    string[] CategoryList,
    string TriggerRuleId,
    string TriggerRuleName
) : ForemanEvent(
    Timestamp,
    NewLevel >= EscalationLevel.Emergency ? ForemanSeverity.Critical :
    NewLevel >= EscalationLevel.Alarm     ? ForemanSeverity.High     :
                                            ForemanSeverity.Medium,
    "Foreman.Behavior",
    $"[{NewLevel.ToString().ToUpperInvariant()}] {HarnessDisplayName} — {TotalAlerts} alert(s), " +
    $"{UniqueRules} rule(s), trigger: {TriggerRuleName}");
