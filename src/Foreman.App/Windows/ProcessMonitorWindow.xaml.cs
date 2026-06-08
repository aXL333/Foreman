using Foreman.Core.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Foreman.App.Windows;

public partial class ProcessMonitorWindow : Window
{
    private readonly Func<IEnumerable<ProcessRecord>> _getSnapshot;
    private readonly DispatcherTimer _timer;
    private ProcessMonitorVm? _selected;

    public ProcessMonitorWindow(Func<IEnumerable<ProcessRecord>> getSnapshot)
    {
        _getSnapshot = getSnapshot;
        InitializeComponent();

        Loaded += (_, _) => Refresh();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        var records = _getSnapshot().ToList();

        bool harnessOnly    = HarnessOnlyCheck.IsChecked   == true;
        bool hideTerminated = HideTerminatedCheck.IsChecked == true;

        var filtered = records
            .Where(p => !harnessOnly    || p.IsHarness)
            .Where(p => !hideTerminated || p.State != ProcessState.Terminated)
            .OrderByDescending(p => (int)p.State)        // hanging/orphaned first
            .ThenBy(p => p.HarnessType ?? "zzz")
            .ThenBy(p => p.Pid)
            .Select(p => new ProcessMonitorVm(p))
            .ToList();

        ProcessList.ItemsSource = filtered;
        CountLabel.Text = $"{filtered.Count} process{(filtered.Count == 1 ? "" : "es")}";

        // re-select previously selected row if it's still present
        if (_selected is not null)
        {
            var match = filtered.FirstOrDefault(v => v.Pid == _selected.Pid);
            if (match is not null)
                ProcessList.SelectedItem = match;
        }
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => Refresh();

    private void ProcessList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = ProcessList.SelectedItem as ProcessMonitorVm;

        if (_selected is null)
        {
            CmdLineDetail.Text = "— select a process —";
            CmdLineDetail.Foreground = (Brush)FindResource("TextMutedBrush");
            CopyPidBtn.IsEnabled = false;
            CopyCmdBtn.IsEnabled = false;
        }
        else
        {
            CmdLineDetail.Text = string.IsNullOrEmpty(_selected.CommandLineFull)
                ? "(no command line)"
                : _selected.CommandLineFull;
            CmdLineDetail.Foreground = (Brush)FindResource("TextPrimaryBrush");
            CopyPidBtn.IsEnabled = true;
            CopyCmdBtn.IsEnabled = !string.IsNullOrEmpty(_selected.CommandLineFull);
        }
    }

    private void CopyPidClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
            Clipboard.SetText(_selected.Pid.ToString());
    }

    private void CopyCmdClick(object sender, RoutedEventArgs e)
    {
        if (_selected?.CommandLineFull is { Length: > 0 } cmd)
            Clipboard.SetText(cmd);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}

// ─── ViewModel ─────────────────────────────────────────────────────────────────

public sealed class ProcessMonitorVm
{
    // raw for selection continuity
    public int    Pid            { get; }
    public string Name           { get; }
    public string CommandLine    { get; }        // truncated for column display
    public string CommandLineFull { get; }       // full, for detail strip
    public int    ParentPid      { get; }
    public string HarnessType    { get; }
    public string StateLabel     { get; }
    public string UptimeLabel    { get; }
    public string SilentLabel    { get; }
    public Brush  RowForeground  { get; }

    public ProcessMonitorVm(ProcessRecord p)
    {
        Pid             = p.Pid;
        Name            = p.Name;
        CommandLineFull = p.CommandLine;
        CommandLine     = Truncate(p.CommandLine, 80);
        ParentPid       = p.ParentPid;
        HarnessType     = p.HarnessType ?? (p.IsHarness ? "harness" : "—");

        StateLabel = p.State switch
        {
            ProcessState.Hanging    => "⚠ Hanging",
            ProcessState.Orphaned   => "⚠ Orphaned",
            ProcessState.Terminated => "✕ Gone",
            _                       => p.IsHarness ? "● Active" : "○ Active",
        };

        UptimeLabel = FormatMinutes(p.UptimeMinutes);
        SilentLabel = p.IoCountersUnavailable ? "n/a" : FormatMinutes(p.SilentMinutes);

        RowForeground = p.State switch
        {
            ProcessState.Hanging  or
            ProcessState.Orphaned => new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44)),
            ProcessState.Terminated => new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x65)),
            _ => p.IsHarness
                ? new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0))
                : new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x90)),
        };
    }

    private static string FormatMinutes(int mins)
    {
        if (mins < 1)  return "<1m";
        if (mins < 60) return $"{mins}m";
        var h = mins / 60;
        var m = mins % 60;
        return $"{h}h {m:D2}m";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
