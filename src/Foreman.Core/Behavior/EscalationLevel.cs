namespace Foreman.Core.Behavior;

/// <summary>
/// Four-level escalation model. Levels only increase within a session; reset manually
/// via BehaviorMetricsWindow or when the harness process exits.
/// </summary>
public enum EscalationLevel
{
    /// <summary>Default — event logged, tray colour unchanged.</summary>
    Watch = 0,

    /// <summary>
    /// Triggered by: 1+ High alert, OR 3+ Medium alerts, OR 2+ threat categories.
    /// Actions: tray → Red, balloon notification.
    /// </summary>
    Alert = 1,

    /// <summary>
    /// Triggered by: any Critical, OR 5+ unique rules, OR 3+ categories, OR 2+ High alerts.
    /// Actions: balloon + MCP proactive push to connected harness.
    /// </summary>
    Alarm = 2,

    /// <summary>
    /// Triggered by: emergency-tier rule (mimikatz, shadow-copy, lateral movement),
    /// OR 10+ total alerts, OR all 3 major categories (cred + priv + net) simultaneously.
    /// Actions: alarm window auto-shown, MCP push, kill-harness button offered.
    /// </summary>
    Emergency = 3,
}
