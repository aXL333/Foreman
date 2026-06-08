using Foreman.Core.Models;

namespace Foreman.Core.Behavior;

/// <summary>
/// Accumulated behavioral metrics for a single harness (or process) within one session.
/// Thread-safe for concurrent reads/writes from EventBus and UI threads.
/// </summary>
public sealed class BehaviorProfile
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _bySeverity = new();
    private readonly HashSet<string> _categories = new();   // "cred", "priv", "net", "win", "del" …
    private readonly HashSet<string> _rules = new();        // "cred-007", "win-009" …

    public string HarnessId    { get; }
    public string DisplayName  { get; }

    /// <summary>When this session's first alert was recorded.</summary>
    public DateTimeOffset SessionStart  { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAlertTime { get; private set; }

    public EscalationLevel CurrentLevel { get; internal set; } = EscalationLevel.Watch;
    public int TotalAlerts { get; private set; }

    // ── Thread-safe accessors ────────────────────────────────────────────────

    public int UniqueRulesCount  { get { lock (_lock) return _rules.Count; } }
    public int CategoryCount     { get { lock (_lock) return _categories.Count; } }
    public int GetSeverityCount(string sev)  { lock (_lock) return _bySeverity.GetValueOrDefault(sev); }
    public bool HasCategory(string cat)      { lock (_lock) return _categories.Contains(cat); }
    public bool HasRule(string ruleId)       { lock (_lock) return _rules.Contains(ruleId); }

    /// <summary>Snapshot copy of the category set — safe to iterate.</summary>
    public IReadOnlyList<string> Categories  { get { lock (_lock) return [.. _categories]; } }

    /// <summary>Snapshot copy of fired rule IDs — safe to iterate.</summary>
    public IReadOnlyList<string> UniqueRules { get { lock (_lock) return [.. _rules]; } }

    public TimeSpan SessionDuration => DateTimeOffset.UtcNow - SessionStart;

    // ── Construction ─────────────────────────────────────────────────────────

    public BehaviorProfile(string harnessId)
    {
        HarnessId   = harnessId;
        // strip the "proc:" prefix that is prepended for unlcassified processes
        DisplayName = harnessId.StartsWith("proc:", StringComparison.Ordinal)
            ? harnessId[5..]
            : harnessId;
    }

    // ── Mutation (called on EventBus thread) ─────────────────────────────────

    public void RecordAlert(CommandAlertEvent cmd)
    {
        lock (_lock)
        {
            TotalAlerts++;
            LastAlertTime = DateTimeOffset.UtcNow;

            var sev = cmd.Severity.ToString();
            _bySeverity[sev] = _bySeverity.GetValueOrDefault(sev) + 1;

            if (!string.IsNullOrEmpty(cmd.RuleId))
            {
                _rules.Add(cmd.RuleId);
                var cat = cmd.RuleId.Split('-')[0].ToLowerInvariant();
                _categories.Add(cat);
            }
        }
    }

    /// <summary>Resets all metrics and drops the escalation level back to Watch.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            TotalAlerts   = 0;
            LastAlertTime = default;
            CurrentLevel  = EscalationLevel.Watch;
            _bySeverity.Clear();
            _categories.Clear();
            _rules.Clear();
        }
    }
}
