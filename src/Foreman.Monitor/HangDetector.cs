using Foreman.Core.Events;
using Foreman.Core.Models;
using Foreman.Core.Settings;
using System.Collections.Concurrent;

namespace Foreman.Monitor;

/// <summary>
/// Emits HangDetected events when a harness child process has had zero I/O for longer
/// than the threshold.
///
/// Scope: by default (MonitorAllProcesses == false) only processes that live inside a
/// harness process tree are candidates — and the harness process itself is excluded,
/// because an agent idling while it waits for user input is normal, not a hang. This is
/// what stops idle system processes (RuntimeBroker, WinStore.App, svchost, explorer…)
/// and the harness itself from producing noise.
///
/// De-duplication: alerts fire once per *hang episode*. A permanently-idle process is
/// reported a single time; it only re-alerts if its I/O resumes and it then hangs again.
/// </summary>
public sealed class HangDetector
{
    private readonly EventBus _bus;
    private readonly ForemanSettings _settings;
    private readonly ProcessTreeTracker _tree;

    // key: pid → the LastIoChangeTime snapshot at the moment we last alerted.
    // We only re-alert if this advances (i.e. I/O resumed and the process hung anew).
    private readonly ConcurrentDictionary<int, DateTimeOffset> _alertedEpoch = new();

    // Infrastructure helpers a harness spawns that legitimately sit idle: a console host
    // has no I/O when its console isn't being written to. They are real harness descendants,
    // so the scope check would otherwise flag them — but a hung conhost is never actionable.
    private static readonly HashSet<string> _infraProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "conhost.exe",
    };

    public HangDetector(EventBus bus, ForemanSettings settings, ProcessTreeTracker tree)
    {
        _bus = bus;
        _settings = settings;
        _tree = tree;
    }

    public void Check(ProcessRecord record)
    {
        if (record.State == ProcessState.Terminated) return;
        if (record.IoCountersUnavailable) return;
        if (_infraProcessNames.Contains(record.Name)) return;   // conhost.exe etc. — idle by nature

        // ── Scope: only harness *children* are hang candidates ───────────────────
        // FindHarnessAncestor returns the nearest IsHarness ancestor (possibly the
        // record itself). When MonitorAllProcesses is off we require a harness ancestor
        // AND that the record is not the harness itself.
        var harness = _tree.FindHarnessAncestor(record.Pid);
        if (!_settings.MonitorAllProcesses)
        {
            if (harness is null) return;            // not inside any harness tree
            if (harness.Pid == record.Pid) return;  // this IS the harness, not a child
        }

        var threshold = _settings.HangThresholdMinutes;
        var silent    = record.SilentMinutes;
        var uptime    = record.UptimeMinutes;

        if (uptime < threshold || silent < threshold) return;

        // ── De-dupe per hang episode ─────────────────────────────────────────────
        // If LastIoChangeTime hasn't moved since our last alert, this is the same
        // silent stretch we already reported — stay quiet. When the process resumes
        // I/O, ProcessTreeTracker.UpdateIoCounters advances LastIoChangeTime, so a
        // subsequent hang produces a fresh alert.
        if (_alertedEpoch.TryGetValue(record.Pid, out var epoch) && epoch == record.LastIoChangeTime)
            return;

        _alertedEpoch[record.Pid] = record.LastIoChangeTime;
        record.State = ProcessState.Hanging;

        // If we have an owning harness, name it in the message and event so the user
        // can see *which* harness the hung child belongs to.
        var isChild   = harness is not null && harness.Pid != record.Pid;
        var ownerNote = isChild ? $", a child of {harness!.HarnessType} (pid {harness.Pid})," : "";
        int? ownerPid = isChild ? harness!.Pid : null;

        _bus.Publish(new HangDetectedEvent(
            DateTimeOffset.UtcNow,
            "Foreman.Monitor",
            $"{record.Name} (pid {record.Pid}){ownerNote} has had no I/O for {silent} min (running {uptime} min)",
            record.Pid,
            record.Name,
            uptime,
            silent,
            ownerPid
        ));
    }

    /// <summary>Drops the per-process alert state when a process exits (called by the poller/watcher).</summary>
    public void Forget(int pid) => _alertedEpoch.TryRemove(pid, out _);
}
