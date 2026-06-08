using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Foreman.App.Windows;

/// <summary>
/// Main at-a-glance dashboard window — opened by left-clicking the tray icon.
/// Shows per-harness escalation status chips, then a scrollable feed of the 50 most
/// recent security-relevant events (command alerts, escalations, hangs, orphans).
/// Clicking any row opens the AlertDetailWindow for that event.
/// Refreshes live (via IEventSink) and every 30 s for relative-time ("X ago") text.
/// </summary>
public partial class DashboardWindow : Window, IEventSink
{
    private readonly Func<IEnumerable<BehaviorProfile>> _getProfiles;
    private readonly ObservableCollection<DashboardAlertVm> _alerts = [];
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>Wired by TrayController so "Open Full Log" can open the log window.</summary>
    public Action? OpenLogRequested { get; set; }

    public DashboardWindow(Func<IEnumerable<BehaviorProfile>> getProfiles)
    {
        _getProfiles = getProfiles;
        InitializeComponent();

        AlertList.ItemsSource = _alerts;

        Refresh();
        EventBus.Instance.Subscribe(this);

        // Periodic refresh so relative time labels stay accurate
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    // ── IEventSink ────────────────────────────────────────────────────────────

    void IEventSink.OnEvent(ForemanEvent evt)
    {
        // Info events (startup messages etc.) don't appear in the dashboard
        if (evt is InfoEvent) return;
        Dispatcher.BeginInvoke(Refresh);
    }

    // ── Data refresh ──────────────────────────────────────────────────────────

    private void Refresh()
    {
        // ── Alert feed ────────────────────────────────────────────────────────
        var history = EventBus.Instance.GetHistory();
        var relevant = history
            .Where(static e => e is CommandAlertEvent
                                  or EscalationEvent
                                  or HangDetectedEvent
                                  or OrphanDetectedEvent)
            .OrderByDescending(e => e.Timestamp)
            .Take(50)
            .ToList();

        _alerts.Clear();
        foreach (var evt in relevant)
            _alerts.Add(DashboardAlertVm.From(evt));

        // ── Harness chips ─────────────────────────────────────────────────────
        var profiles = _getProfiles()
            .Where(p => p.TotalAlerts > 0)
            .OrderByDescending(p => (int)p.CurrentLevel)
            .ThenByDescending(p => p.TotalAlerts)
            .ToList();

        HarnessPanel.ItemsSource = profiles
            .Select(p => new HarnessChipVm(p))
            .ToList();

        // ── Header summary ────────────────────────────────────────────────────
        var total = profiles.Sum(p => p.TotalAlerts);
        SummaryLabel.Text = total == 0
            ? "all clear"
            : $"{total} alert{(total == 1 ? "" : "s")} total";

        // ── Footer ────────────────────────────────────────────────────────────
        FooterText.Text = relevant.Count == 0
            ? ""
            : $"{relevant.Count} recent event{(relevant.Count == 1 ? "" : "s")}  ·  click any row for detail";

        // ── Show / hide empty state ───────────────────────────────────────────
        var hasItems = relevant.Count > 0;
        EmptyPanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        AlertList.Visibility  = hasItems ? Visibility.Visible   : Visibility.Collapsed;
    }

    // ── UI event handlers ─────────────────────────────────────────────────────

    private void AlertList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AlertList.SelectedItem is DashboardAlertVm vm)
        {
            AlertList.SelectedItem = null;  // clear immediately so the row can be re-clicked
            AlertDetailWindow.ShowFor(vm.OriginalEvent);
        }
    }

    private void OpenLogClick(object sender, RoutedEventArgs e) =>
        OpenLogRequested?.Invoke();

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DashboardAlertVm — one row in the alert feed
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DashboardAlertVm
{
    public string     SeverityLabel     { get; }
    public Brush      SeverityBrush     { get; }
    public string     TagBadge          { get; }  // "NET", "CRED", "ESC", "HANG" …
    public Brush      TagBg             { get; }
    public Brush      TagFg             { get; }
    public string     Headline          { get; }  // primary display text
    public string     SubLine           { get; }  // command / categories / detail
    public Visibility SubLineVisibility { get; }
    public string     TimeAgo           { get; }
    public ForemanEvent OriginalEvent   { get; }

    private DashboardAlertVm(
        string severityLabel, Brush severityBrush,
        string tagBadge, Brush tagBg, Brush tagFg,
        string headline, string subLine, string timeAgo,
        ForemanEvent originalEvent)
    {
        SeverityLabel     = severityLabel;
        SeverityBrush     = severityBrush;
        TagBadge          = tagBadge;
        TagBg             = tagBg;
        TagFg             = tagFg;
        Headline          = headline;
        SubLine           = subLine;
        SubLineVisibility = string.IsNullOrEmpty(subLine) ? Visibility.Collapsed : Visibility.Visible;
        TimeAgo           = timeAgo;
        OriginalEvent     = originalEvent;
    }

    public static DashboardAlertVm From(ForemanEvent evt)
    {
        var (tag, tagBg, tagFg) = GetTag(evt);
        var (headline, subLine) = GetContent(evt);

        return new DashboardAlertVm(
            evt.Severity.ToString().ToUpperInvariant(),
            SeverityToBrush(evt.Severity),
            tag, tagBg, tagFg,
            headline, subLine,
            RelativeTime(evt.Timestamp),
            evt);
    }

    // ── Tag badge ─────────────────────────────────────────────────────────────

    private static (string tag, Brush bg, Brush fg) GetTag(ForemanEvent evt) => evt switch
    {
        CommandAlertEvent cmd => CommandTag(cmd.RuleId),

        EscalationEvent esc => esc.NewLevel switch
        {
            EscalationLevel.Emergency => ("ESC",
                new SolidColorBrush(Color.FromRgb(0x44, 0x0A, 0x0A)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x77))),
            EscalationLevel.Alarm => ("ESC",
                new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x04)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55))),
            _ => ("ESC",
                new SolidColorBrush(Color.FromRgb(0x2C, 0x22, 0x06)),
                new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x55))),
        },

        HangDetectedEvent => ("HANG",
            new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x06)),
            new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x44))),

        OrphanDetectedEvent => ("ORPHAN",
            new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x04)),
            new SolidColorBrush(Color.FromRgb(0xAA, 0x88, 0x33))),

        _ => ("EVENT",
            new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)),
            new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90))),
    };

    private static (string tag, Brush bg, Brush fg) CommandTag(string ruleId)
    {
        var cat = ruleId.Split('-')[0].ToUpperInvariant();
        return cat switch
        {
            "CRED" => (cat,
                new SolidColorBrush(Color.FromRgb(0x44, 0x0A, 0x0A)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88))),
            "NET" => (cat,
                new SolidColorBrush(Color.FromRgb(0x0A, 0x1C, 0x38)),
                new SolidColorBrush(Color.FromRgb(0x66, 0xAA, 0xFF))),
            "PRIV" => (cat,
                new SolidColorBrush(Color.FromRgb(0x28, 0x0A, 0x2C)),
                new SolidColorBrush(Color.FromRgb(0xCC, 0x77, 0xFF))),
            "WIN" => (cat,
                new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x34)),
                new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xFF))),
            "DEL" => (cat,
                new SolidColorBrush(Color.FromRgb(0x2E, 0x1E, 0x04)),
                new SolidColorBrush(Color.FromRgb(0xEE, 0xCC, 0x44))),
            _ => (cat,
                new SolidColorBrush(Color.FromRgb(0x18, 0x1A, 0x22)),
                new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90))),
        };
    }

    // ── Headline + subLine ────────────────────────────────────────────────────

    private static (string headline, string subLine) GetContent(ForemanEvent evt) => evt switch
    {
        CommandAlertEvent cmd =>
            (
                $"{cmd.RuleName}  ·  {ProcessName(cmd.Source)}",
                cmd.CommandLine.Length > 120
                    ? cmd.CommandLine[..120] + "…"
                    : cmd.CommandLine
            ),

        EscalationEvent esc =>
            (
                $"{esc.HarnessDisplayName}  →  {esc.NewLevel.ToString().ToUpperInvariant()}",
                BuildEscSubLine(esc)
            ),

        HangDetectedEvent hang =>
            (
                $"{hang.ProcessName}  (pid {hang.ProcessId})  —  no I/O for {hang.SilentMinutes}min",
                $"running {hang.UptimeMinutes}min total"
            ),

        OrphanDetectedEvent orphan =>
            (
                $"{orphan.ProcessName}  —  orphaned after parent exited",
                $"was child of {orphan.DeadParentName} (pid {orphan.DeadParentPid})" +
                $"  ·  running {orphan.UptimeMinutes}min"
            ),

        _ => (
            evt.Message.Length > 100 ? evt.Message[..100] + "…" : evt.Message,
            string.Empty
        ),
    };

    private static string BuildEscSubLine(EscalationEvent esc)
    {
        var parts = new List<string>
        {
            $"{esc.TotalAlerts} alert{(esc.TotalAlerts == 1 ? "" : "s")}",
        };
        if (esc.UniqueRules > 0)
            parts.Add($"{esc.UniqueRules} rule{(esc.UniqueRules == 1 ? "" : "s")}");
        if (esc.CategoryList.Length > 0)
            parts.Add(string.Join(", ", esc.CategoryList).ToUpperInvariant());
        return string.Join("  ·  ", parts);
    }

    private static string ProcessName(string source)
    {
        // Source format: "processName (pid 12345)"
        var idx = source.IndexOf(" (pid", StringComparison.Ordinal);
        return idx > 0 ? source[..idx] : source;
    }

    // ── Severity brush ────────────────────────────────────────────────────────

    private static Brush SeverityToBrush(ForemanSeverity s) => s switch
    {
        ForemanSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xCC, 0x22, 0x22)),
        ForemanSeverity.High     => new SolidColorBrush(Color.FromRgb(0xCC, 0x55, 0x22)),
        ForemanSeverity.Medium   => new SolidColorBrush(Color.FromRgb(0xAA, 0x77, 0x11)),
        ForemanSeverity.Low      => new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0x33)),
        _                        => new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99)),
    };

    // ── Relative time ─────────────────────────────────────────────────────────

    private static string RelativeTime(DateTimeOffset ts)
    {
        var ago = DateTimeOffset.UtcNow - ts;
        if (ago.TotalSeconds < 30) return "just now";
        if (ago.TotalMinutes <  2) return $"{(int)ago.TotalSeconds}s ago";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours   < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// HarnessChipVm — one status chip in the header strip
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HarnessChipVm
{
    public string DisplayName { get; }
    public string LevelLabel  { get; }   // "WATCH", "ALERT", "ALARM", "EMERGENCY"
    public Brush  LevelBg     { get; }
    public Brush  LevelFg     { get; }
    public string CountLabel  { get; }   // "5 alerts"

    public HarnessChipVm(BehaviorProfile profile)
    {
        DisplayName = profile.DisplayName;
        LevelLabel  = profile.CurrentLevel.ToString().ToUpperInvariant();
        CountLabel  = $"{profile.TotalAlerts} alert{(profile.TotalAlerts == 1 ? "" : "s")}";
        (LevelBg, LevelFg) = profile.CurrentLevel switch
        {
            EscalationLevel.Emergency => (
                new SolidColorBrush(Color.FromRgb(0x44, 0x0A, 0x0A)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66))),
            EscalationLevel.Alarm => (
                new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x08)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44))),
            EscalationLevel.Alert => (
                new SolidColorBrush(Color.FromRgb(0x30, 0x28, 0x08)),
                new SolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x3C))),
            _ => (
                new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x28)),
                new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90))),
        };
    }
}
