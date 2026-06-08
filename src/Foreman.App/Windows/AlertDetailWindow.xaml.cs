using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Models;
using System.Windows;
using System.Windows.Media;

namespace Foreman.App.Windows;

public partial class AlertDetailWindow : Window
{
    private readonly ForemanEvent _event;

    /// <summary>Wired up by TrayController so the "Open Log" button can open the log window.</summary>
    public static Action? OpenLogRequested { get; set; }

    /// <summary>
    /// Wired up by App.xaml.cs. Resolves a live ProcessRecord from a PID so the alert
    /// can display which process and harness type triggered the rule.
    /// </summary>
    public static Func<int, ProcessRecord?>? GetProcessByPid { get; set; }

    /// <summary>
    /// Wired up by App.xaml.cs. Resolves the BehaviorProfile for a harness key so the
    /// alert can display the current escalation level and session alert count.
    /// </summary>
    public static Func<string, BehaviorProfile?>? GetProfileByHarness { get; set; }

    /// <summary>
    /// Wired up by App.xaml.cs. Walks the process tree to find the harness a process belongs
    /// to, so a hook or spawned shell (which matches no harness rule itself) is still
    /// attributed to its harness rather than shown as "not a tracked harness".
    /// </summary>
    public static Func<int, ProcessRecord?>? GetHarnessAncestorByPid { get; set; }

    private AlertDetailWindow(ForemanEvent evt)
    {
        _event = evt;
        InitializeComponent();
        DataContext = new AlertDetailVm(evt);
    }

    /// <summary>Opens an AlertDetailWindow for the given event and forces it to the foreground.</summary>
    public static void ShowFor(ForemanEvent evt)
    {
        var w = new AlertDetailWindow(evt);
        w.Show();
        WindowActivation.Surface(w);
    }

    private void AcknowledgeClick(object sender, RoutedEventArgs e)
    {
        _event.Acknowledged = true;
        EventBus.Instance.Publish(new InfoEvent(
            DateTimeOffset.UtcNow,
            "Foreman",
            $"Alert acknowledged: [{_event.Id}] {_event.Message[..Math.Min(80, _event.Message.Length)]}"));
        Close();
    }

    private void OpenLogClick(object sender, RoutedEventArgs e)
    {
        try { OpenLogRequested?.Invoke(); }
        catch (Exception ex)
        {
            // Show type + message + partial stack so future failures are diagnosable
            var stackSnippet = ex.StackTrace is { Length: > 0 } st
                ? (st.Length > 400 ? st[..400] + "\n…" : st)
                : "(no stack trace)";
            MessageBox.Show(
                $"Could not open the event log.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n{stackSnippet}",
                "Foreman", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed class AlertDetailVm
{
    public string  SeverityLabel      { get; }
    public Brush   SeverityBackground { get; }
    public string  EventTypeLabel     { get; }
    public string  TimestampLabel     { get; }
    public string  Source             { get; }

    public Visibility RuleInfoVisibility    { get; }
    public string     RuleId               { get; }
    public string     RuleName             { get; }
    public Visibility CommandLineVisibility { get; }
    public string     CommandLine          { get; }

    // ── Process identification (CommandAlertEvent) ────────────────────────
    public Visibility ProcessInfoVisibility { get; }
    public string     ProcessLine          { get; }   // "cmd.exe  ·  pid 1234"
    public string     HarnessLine          { get; }   // "Harness type: claude-code"
    public Visibility HarnessLineVisibility { get; }

    // ── Escalation / behavior profile summary ────────────────────────────
    public Visibility EscalationLineVisibility { get; }
    public string     EscalationLevelLabel      { get; }   // "ALERT", "ALARM", etc.
    public Brush      EscalationBadgeBg        { get; }
    public Brush      EscalationBadgeFg        { get; }
    public string     EscalationSummary        { get; }   // "7 alerts  ·  3 rules"

    public string WhyDangerous     { get; }
    public string RecommendedAction { get; }

    public AlertDetailVm(ForemanEvent evt)
    {
        SeverityLabel      = evt.Severity.ToString().ToUpperInvariant();
        SeverityBackground = SeverityToBrush(evt.Severity);
        TimestampLabel     = evt.Timestamp.LocalDateTime.ToString("yyyy-MM-dd  HH:mm:ss.fff");
        Source             = evt.Source;

        // defaults — overridden per event type below
        RuleInfoVisibility    = Visibility.Collapsed;
        RuleId                = string.Empty;
        RuleName              = string.Empty;
        CommandLineVisibility = Visibility.Collapsed;
        CommandLine           = string.Empty;
        ProcessInfoVisibility    = Visibility.Collapsed;
        ProcessLine              = string.Empty;
        HarnessLine              = string.Empty;
        HarnessLineVisibility    = Visibility.Collapsed;
        EscalationLineVisibility = Visibility.Collapsed;
        EscalationLevelLabel     = string.Empty;
        EscalationBadgeBg        = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24));
        EscalationBadgeFg        = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90));
        EscalationSummary        = string.Empty;
        WhyDangerous             = string.Empty;
        RecommendedAction     = string.Empty;

        switch (evt)
        {
            case CommandAlertEvent cmd:
                EventTypeLabel        = "Command Alert";
                RuleInfoVisibility    = Visibility.Visible;
                RuleId                = cmd.RuleId;
                RuleName              = cmd.RuleName;
                CommandLineVisibility = Visibility.Visible;
                CommandLine           = cmd.CommandLine;

                // Resolve the originating process from the live process tree
                if (cmd.ProcessId > 0)
                {
                    var rec = AlertDetailWindow.GetProcessByPid?.Invoke(cmd.ProcessId);
                    ProcessInfoVisibility = Visibility.Visible;

                    string? harnessKey;
                    if (rec is not null)
                    {
                        ProcessLine = $"{rec.Name}  ·  pid {cmd.ProcessId}";
                        string? ancestorHt = null;
                        if (rec.IsHarness && rec.HarnessType is not null)
                        {
                            HarnessLine           = $"Harness type: {rec.HarnessType}";
                            HarnessLineVisibility = Visibility.Visible;
                        }
                        else if (rec.HarnessType is not null)
                        {
                            HarnessLine           = $"Child of {rec.HarnessType} harness  ·  parent pid {rec.ParentPid}";
                            HarnessLineVisibility = Visibility.Visible;
                        }
                        else
                        {
                            // The process matched no harness rule itself — but it may be a child
                            // of one (e.g. a PowerShell hook or shell spawned by claude-code).
                            // Walk the process tree to attribute it.
                            ancestorHt = AlertDetailWindow.GetHarnessAncestorByPid?.Invoke(cmd.ProcessId)?.HarnessType;
                            HarnessLine = ancestorHt is not null
                                ? $"Child of {ancestorHt} harness  ·  parent pid {rec.ParentPid}"
                                : $"Unclassified (not a tracked harness)  ·  parent pid {rec.ParentPid}";
                            HarnessLineVisibility = Visibility.Visible;
                        }
                        // Harness key matches how BehaviorTracker keys profiles
                        harnessKey = rec.HarnessType ?? ancestorHt ?? $"proc:{rec.Name}";
                    }
                    else
                    {
                        // Process has already exited by the time the user opened the detail;
                        // derive the key the same way BehaviorTracker.GetHarnessKey does.
                        ProcessLine           = $"pid {cmd.ProcessId}  ·  process has since exited";
                        HarnessLineVisibility = Visibility.Collapsed;
                        var idx  = cmd.Source.IndexOf(" (pid", StringComparison.Ordinal);
                        var name = idx > 0 ? cmd.Source[..idx] : cmd.Source;
                        harnessKey = $"proc:{name}";
                    }

                    // Show the harness's current escalation level and session alert totals
                    var profile = AlertDetailWindow.GetProfileByHarness?.Invoke(harnessKey);
                    if (profile is not null)
                    {
                        EscalationLineVisibility = Visibility.Visible;
                        EscalationLevelLabel     = profile.CurrentLevel.ToString().ToUpperInvariant();
                        (EscalationBadgeBg, EscalationBadgeFg) = EscalationColors(profile.CurrentLevel);

                        var parts = new List<string>
                        {
                            $"{profile.TotalAlerts} alert{(profile.TotalAlerts == 1 ? "" : "s")} this session"
                        };
                        if (profile.UniqueRulesCount > 1)
                            parts.Add($"{profile.UniqueRulesCount} unique rules");
                        if (profile.CategoryCount > 1)
                            parts.Add($"{profile.CategoryCount} categories");
                        EscalationSummary = string.Join("  ·  ", parts);
                    }
                }

                WhyDangerous = string.IsNullOrWhiteSpace(cmd.RuleDescription)
                    ? "This command matched a pattern associated with malicious or unsafe behaviour."
                    : cmd.RuleDescription;

                RecommendedAction = string.IsNullOrWhiteSpace(cmd.RuleGuidance)
                    ? GetDefaultGuidance(cmd.RuleId)
                    : cmd.RuleGuidance;
                break;

            case HangDetectedEvent hang:
                EventTypeLabel = "Process Hang Detected";
                WhyDangerous =
                    $"\"{hang.ProcessName}\" (pid {hang.ProcessId}) has been running for {hang.UptimeMinutes} minutes " +
                    $"with no I/O activity for {hang.SilentMinutes} minutes.{ResolveHarnessOwner(hang.ParentHarnessPid)}\n\n" +
                    $"This means the process is stuck — waiting for input that will never come, " +
                    $"caught in an infinite loop, or blocked on a locked resource. Hung harness " +
                    $"children can accumulate silently and consume memory or file handles.";
                RecommendedAction =
                    "1. Check whether the harness is prompting for input in its terminal.\n" +
                    "2. If the hang is unintentional, send Ctrl+C to the harness terminal or kill " +
                    $"pid {hang.ProcessId} in Task Manager.\n" +
                    "3. Review the harness's most recent output to understand what it was doing before stalling.";
                break;

            case OrphanDetectedEvent orphan:
                EventTypeLabel = "Orphaned Process";
                WhyDangerous =
                    $"\"{orphan.ProcessName}\" (pid {orphan.ProcessId}) is still running even though " +
                    $"its parent \"{orphan.DeadParentName}\" (pid {orphan.DeadParentPid}) has exited.\n\n" +
                    $"The orphan has been running for {orphan.UptimeMinutes} minutes unsupervised. " +
                    $"Orphaned harness children can continue executing tasks, mutating files, or making " +
                    $"network requests without the parent harness to oversee them.";
                RecommendedAction =
                    "1. If this process is expected (e.g. a deliberate background daemon), you can safely ignore this alert.\n" +
                    $"2. Otherwise, terminate pid {orphan.ProcessId} from Task Manager.\n" +
                    "3. Review what task the harness was running when it exited to understand what the orphan may be doing.";
                break;

            case PermissionViolationEvent perm:
                EventTypeLabel = "Permission Violation";
                WhyDangerous =
                    $"Profile \"{perm.ProfileName}\" was violated.\n\n" +
                    $"Violation type: {perm.ViolationType}\n" +
                    $"Detail: {perm.Detail}\n\n" +
                    $"The harness attempted an action outside the boundaries configured for it in Foreman's permission profiles.";
                RecommendedAction =
                    "1. Review the harness's current task to determine if this action was intentional.\n" +
                    "2. If the action is legitimate, update the permission profile in Foreman's Profiles editor.\n" +
                    "3. If unexpected, terminate the harness and audit its recent command history in the event log.";
                break;

            case NonzeroExitEvent exit:
                EventTypeLabel = "Non-Zero Exit";
                WhyDangerous =
                    $"\"{exit.ProcessName}\" (pid {exit.ProcessId}) exited with code {exit.ExitCode}.\n\n" +
                    $"Non-zero exit codes indicate a failure. In a harness context this may mean " +
                    $"a tool call failed, a script encountered an unhandled exception, or a system " +
                    $"command was rejected (e.g. access denied). Depending on the harness, it may " +
                    $"retry the operation with different parameters or escalate privileges.";
                RecommendedAction =
                    "1. Check the harness's terminal output for error messages near this timestamp.\n" +
                    "2. Review what command or tool the harness was executing when the process exited.\n" +
                    "3. Watch for follow-up actions — an AI harness may attempt alternative approaches after a failure.";
                break;

            case EscalationEvent esc:
                EventTypeLabel = $"Escalation — {esc.NewLevel.ToString().ToUpperInvariant()}";
                WhyDangerous =
                    $"\"{esc.HarnessDisplayName}\" crossed the {esc.NewLevel} threshold " +
                    $"(was {esc.OldLevel}) based on accumulated behavior this session.\n\n" +
                    $"Trigger rule: {esc.TriggerRuleId}  {esc.TriggerRuleName}\n" +
                    $"Session totals: {esc.TotalAlerts} alert(s) · {esc.UniqueRules} unique rule(s) · " +
                    $"{esc.CategoryCount} threat categor{(esc.CategoryCount == 1 ? "y" : "ies")}" +
                    (esc.CategoryList.Length > 0 ? $" ({string.Join(", ", esc.CategoryList).ToUpperInvariant()})" : "") +
                    $"\n\nReason: {esc.Reason}";

                RecommendedAction = esc.NewLevel switch
                {
                    EscalationLevel.Emergency =>
                        "IMMEDIATE ACTION RECOMMENDED:\n" +
                        "1. Open the Behavior Metrics window and review the full session history.\n" +
                        "2. If the activity was not authorised, use 'Kill Harness' to terminate the harness.\n" +
                        "3. Review the event log for the specific commands that triggered escalation.\n" +
                        "4. Consider disabling the harness in Foreman until you can audit its behaviour.",
                    EscalationLevel.Alarm =>
                        "1. Open the Behavior Metrics window and review the alert pattern.\n" +
                        "2. Check the event log for the specific commands that crossed the alarm threshold.\n" +
                        "3. If the alerts are from a legitimate task, acknowledge this event — Foreman will continue monitoring.\n" +
                        "4. If unexpected, terminate the harness or disable it in the Harnesses window.",
                    _ =>
                        "1. Review the event log for the alerts that contributed to this escalation.\n" +
                        "2. If all alerts were from legitimate tasks, acknowledge this event — Foreman will continue monitoring.\n" +
                        "3. Open Behavior Metrics to see the full picture across this session.",
                };
                break;

            default:
                EventTypeLabel = "System Event";
                WhyDangerous = evt.Message;
                RecommendedAction = "No action required for informational events.";
                break;
        }
    }

    // ── Guidance fallback by rule category ───────────────────────────────────

    private static string GetDefaultGuidance(string ruleId)
    {
        var category = ruleId.Split('-')[0].ToLowerInvariant();
        return category switch
        {
            "cred" =>
                "1. Immediately review what the harness was attempting to do.\n" +
                "2. If you did not explicitly ask it to access credentials, terminate it now.\n" +
                "3. Change any browser passwords or secrets that may have been exposed.\n" +
                "4. Review the harness's full command history in the event log.",

            "priv" =>
                "1. Do not grant elevated permissions unless you explicitly requested a privileged task.\n" +
                "2. Terminate the harness if privilege escalation was not expected.\n" +
                "3. Review what files or resources the harness was targeting.",

            "win" =>
                "1. Verify whether the detected Windows-specific operation was part of your requested task.\n" +
                "2. If unexpected, terminate the harness and audit its recent activity.\n" +
                "3. Re-enable any security features (Defender, firewall) that may have been altered.",

            "net" =>
                "1. Verify that outbound network activity was expected for the current task.\n" +
                "2. Be especially wary of curl/wget piped to execution — this is a common code-injection vector.\n" +
                "3. If unexpected, terminate the harness and check what was downloaded.",

            "del" or "cmd" =>
                "1. Review whether the harness was asked to perform destructive disk or system operations.\n" +
                "2. Terminate immediately if a destructive command like format, diskpart, or rm -rf was run unexpectedly.\n" +
                "3. Check the harness's output for signs of what it was attempting.",

            _ =>
                "1. Review the harness's current task to determine if this command was expected.\n" +
                "2. If unexpected, terminate the harness and audit its recent activity in the event log.\n" +
                "3. Acknowledge this alert in Foreman once you have reviewed and understood what happened.",
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Names the harness a hung/orphaned child belongs to, for the alert text.</summary>
    private static string ResolveHarnessOwner(int? harnessPid)
    {
        if (harnessPid is not int hp) return "";
        var rec = AlertDetailWindow.GetProcessByPid?.Invoke(hp);
        return rec?.HarnessType is { } ht
            ? $" Spawned by the {ht} harness ({rec.Name}, pid {hp})."
            : $" Spawned by harness process pid {hp}.";
    }

    private static (Brush bg, Brush fg) EscalationColors(EscalationLevel level) => level switch
    {
        EscalationLevel.Emergency => (new SolidColorBrush(Color.FromRgb(0x44, 0x0A, 0x0A)),
                                      new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66))),
        EscalationLevel.Alarm     => (new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x08)),
                                      new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44))),
        EscalationLevel.Alert     => (new SolidColorBrush(Color.FromRgb(0x30, 0x28, 0x08)),
                                      new SolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x3C))),
        _                         => (new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)),
                                      new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90))),
    };

    private static Brush SeverityToBrush(ForemanSeverity s) => s switch
    {
        ForemanSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22)),
        ForemanSeverity.High     => new SolidColorBrush(Color.FromRgb(0xCC, 0x55, 0x22)),
        ForemanSeverity.Medium   => new SolidColorBrush(Color.FromRgb(0xAA, 0x77, 0x11)),
        ForemanSeverity.Low      => new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0x33)),
        _                        => new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99)),
    };
}
