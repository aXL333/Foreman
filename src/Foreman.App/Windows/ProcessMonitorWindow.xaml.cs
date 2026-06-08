using Foreman.Core.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Foreman.App.Windows;

public partial class ProcessMonitorWindow : Window
{
    private readonly Func<IEnumerable<ProcessRecord>> _getSnapshot;
    private readonly DispatcherTimer _timer;
    private readonly ResourceSampler _sampler = new();
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
        // Guard: the IsChecked="True" filter checkboxes raise Checked during InitializeComponent(),
        // before sibling elements (HideTerminatedCheck / ProcessList / CountLabel) are created. Don't
        // run until the window is actually loaded. (Same XAML-init hazard as LogWindow's filters.)
        if (!IsLoaded) return;

        var records = _getSnapshot().ToList();

        bool harnessOnly    = HarnessOnlyCheck.IsChecked   == true;
        bool hideTerminated = HideTerminatedCheck.IsChecked == true;

        var filteredRecords = records
            .Where(p => !harnessOnly    || p.IsHarness)
            .Where(p => !hideTerminated || p.State != ProcessState.Terminated)
            .OrderByDescending(p => (int)p.State)        // hanging/orphaned first
            .ThenBy(p => p.HarnessType ?? "zzz")
            .ThenBy(p => p.Pid)
            .ToList();

        // Live resource metrics (CPU/mem/I/O/GPU) for just the rows we're about to show.
        var metrics = _sampler.Sample(filteredRecords.Select(p => p.Pid).ToList());

        var filtered = filteredRecords
            .Select(p => new ProcessMonitorVm(p, metrics.TryGetValue(p.Pid, out var m) ? m : null))
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
        _sampler.Dispose();
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

    // ── Live resource metrics ────────────────────────────────────────────────
    public string CpuLabel { get; }
    public string MemLabel { get; }
    public string IoLabel  { get; }
    public string GpuLabel { get; }
    public string NetLabel { get; } = "n/a";   // per-process net needs Run Elevated (ETW)

    public ProcessMonitorVm(ProcessRecord p, ResourceSampler.Metrics? metrics)
    {
        Pid             = p.Pid;
        Name            = p.Name;
        CommandLineFull = p.CommandLine;
        CommandLine     = Truncate(p.CommandLine, 80);
        ParentPid       = p.ParentPid;
        HarnessType     = p.HarnessType ?? (p.IsHarness ? "harness" : "—");

        // Terminated rows have no live process to sample.
        if (p.State == ProcessState.Terminated || metrics is not { } m)
        {
            CpuLabel = MemLabel = IoLabel = GpuLabel = "—";
        }
        else
        {
            CpuLabel = m.CpuPercent < 0.5 ? "0%" : $"{m.CpuPercent:0}%";
            MemLabel = m.MemoryBytes > 0 ? FormatBytes(m.MemoryBytes) : "—";
            IoLabel  = FormatRate(m.IoBytesPerSec);
            GpuLabel = m.GpuPercent is { } g ? (g < 0.5 ? "0%" : $"{g:0}%") : "n/a";
        }

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

    private static string FormatBytes(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1 << 30):0.0} GB" :
        b >= 1L << 20 ? $"{b / (double)(1 << 20):0} MB" :
        b >= 1L << 10 ? $"{b / (double)(1 << 10):0} KB" : $"{b} B";

    private static string FormatRate(double bytesPerSec) =>
        bytesPerSec < 1            ? "—" :
        bytesPerSec < 1024         ? $"{bytesPerSec:0} B/s" :
        bytesPerSec < 1024 * 1024  ? $"{bytesPerSec / 1024:0} KB/s" :
                                     $"{bytesPerSec / (1024 * 1024):0.0} MB/s";
}
