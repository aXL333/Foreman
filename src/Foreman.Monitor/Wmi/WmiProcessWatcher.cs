using Foreman.Core.Events;
using Foreman.Core.Heuristics;
using Foreman.Core.Models;
using Foreman.Core.Settings;
using System.Management;
using System.Collections.Generic;

namespace Foreman.Monitor.Wmi;

/// <summary>
/// Listens for process creation and termination via WMI event subscriptions.
/// Fires at medium IL (no admin required). ~1s latency vs. ETW, acceptable for Phase 1.
/// </summary>
public sealed class WmiProcessWatcher : IDisposable
{
    private readonly ProcessTreeTracker _tree;
    private readonly EventBus _bus;
    private readonly ForemanSettings _settings;
    private ManagementEventWatcher? _createWatcher;
    private ManagementEventWatcher? _deleteWatcher;
    private bool _started;

    public WmiProcessWatcher(ProcessTreeTracker tree, EventBus bus, ForemanSettings settings)
    {
        _tree = tree;
        _bus = bus;
        _settings = settings;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        // initial snapshot — detect already-running harnesses before WMI watchers start
        Task.Run(RunInitialScan);

        // process creation — WITHIN 1 means polling every 1 second
        _createWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'"));
        _createWatcher.EventArrived += OnProcessCreated;
        _createWatcher.Start();

        // process termination — 2s is fine since we track final state
        _deleteWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'"));
        _deleteWatcher.EventArrived += OnProcessDeleted;
        _deleteWatcher.Start();
    }

    private void RunInitialScan()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate " +
                "FROM Win32_Process");

            var harnessesFound = new List<string>();

            foreach (ManagementObject proc in searcher.Get())
            {
                try
                {
                    using var _ = proc;
                    var record = BuildRecord(proc);
                    _tree.OnProcessCreated(record);

                    if (record.HarnessType is not null)
                        harnessesFound.Add($"{record.HarnessType} (pid {record.Pid})");
                }
                catch { /* process may have exited mid-scan */ }
            }

            var msg = harnessesFound.Count > 0
                ? $"Initial scan — {harnessesFound.Count} harness process(es) already running: {string.Join(", ", harnessesFound)}"
                : "Initial scan complete — no harnesses currently running";

            _bus.Publish(new InfoEvent(DateTimeOffset.UtcNow, "Foreman.Monitor", msg));
        }
        catch (Exception ex)
        {
            _bus.Publish(new InfoEvent(DateTimeOffset.UtcNow, "Foreman.Monitor",
                $"Initial process scan failed: {ex.Message}"));
        }
    }

    private void OnProcessCreated(object sender, EventArrivedEventArgs e)
    {
        try
        {
            using var proc = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var record = BuildRecord(proc);
            _tree.OnProcessCreated(record);

            // heuristic analysis on the thread pool — don't block the WMI callback
            if (!string.IsNullOrWhiteSpace(record.CommandLine))
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var match = CommandAnalyzer.Instance.Analyze(record.CommandLine, record.Name);
                    if (match is null) return;

                    _bus.Publish(new CommandAlertEvent(
                        DateTimeOffset.UtcNow,
                        match.Severity,
                        $"{record.Name} (pid {record.Pid})",
                        $"[{match.RuleId}] {match.RuleName}: {TruncateCmdLine(record.CommandLine)}",
                        record.CommandLine,
                        match.RuleId,
                        match.RuleName,
                        match.Description,
                        match.Guidance,
                        record.Pid
                    ));
                });
            }
        }
        catch (ManagementException) { /* process died before we could read it */ }
        catch (Exception) { /* never crash the watcher thread */ }
    }

    private void OnProcessDeleted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            using var proc = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var pid = Convert.ToInt32(proc["ProcessId"]);
            var name = proc["Name"]?.ToString() ?? string.Empty;
            var exitCode = proc["ExitCode"] is uint code ? (int)code : 0;

            var orphans = _tree.OnProcessDeleted(pid);

            // publish orphan events for any children that survived
            foreach (var orphan in orphans)
            {
                _bus.Publish(new OrphanDetectedEvent(
                    DateTimeOffset.UtcNow,
                    "Foreman.Monitor",
                    $"{orphan.Name} (pid {orphan.Pid}) is orphaned — parent {name} (pid {pid}) exited",
                    orphan.Pid,
                    orphan.Name,
                    pid,
                    name,
                    orphan.UptimeMinutes
                ));
            }

            // flag nonzero exits from harness-classified processes
            if (exitCode != 0 && _tree.IsTrackedHarness(pid))
            {
                _bus.Publish(new NonzeroExitEvent(
                    DateTimeOffset.UtcNow,
                    "Foreman.Monitor",
                    $"{name} (pid {pid}) exited with code {exitCode}",
                    pid, name, exitCode, null
                ));
            }
        }
        catch { }
    }

    private ProcessRecord BuildRecord(ManagementBaseObject proc)
    {
        var pid = Convert.ToInt32(proc["ProcessId"]);
        var parentPid = Convert.ToInt32(proc["ParentProcessId"]);
        var name = proc["Name"]?.ToString() ?? string.Empty;
        var cmdLine = proc["CommandLine"]?.ToString() ?? string.Empty;
        var exePath = proc["ExecutablePath"]?.ToString() ?? string.Empty;

        // WMI CreationDate is a DMTF datetime string
        var startTime = ParseDmtfDate(proc["CreationDate"]?.ToString());

        var record = new ProcessRecord
        {
            Pid = pid,
            ParentPid = parentPid,
            Name = name,
            CommandLine = cmdLine,
            ExecutablePath = exePath,
            StartTime = startTime,
            LastIoChangeTime = DateTimeOffset.UtcNow,
        };

        HarnessClassifier.Classify(record, _settings.DisabledHarnesses, _settings.CustomHarnessExes);
        return record;
    }

    private static DateTimeOffset ParseDmtfDate(string? dmtf)
    {
        // Format: yyyymmddHHmmss.ffffff+UTC offset  e.g. 20250608143000.000000+000
        if (string.IsNullOrEmpty(dmtf) || dmtf.Length < 14)
            return DateTimeOffset.UtcNow;

        try
        {
            return DateTimeOffset.ParseExact(dmtf[..14], "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal);
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static string TruncateCmdLine(string cmd, int max = 120) =>
        cmd.Length <= max ? cmd : cmd[..max] + "…";

    public void Dispose()
    {
        _createWatcher?.Stop();
        _createWatcher?.Dispose();
        _deleteWatcher?.Stop();
        _deleteWatcher?.Dispose();
    }
}
