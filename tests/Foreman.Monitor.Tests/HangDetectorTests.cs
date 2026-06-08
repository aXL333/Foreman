using Foreman.Core.Events;
using Foreman.Core.Models;
using Foreman.Core.Settings;
using Foreman.Monitor;

namespace Foreman.Monitor.Tests;

/// <summary>
/// Regression coverage for the hang-detection scoping + de-duplication fixes.
///
/// Background: idle Windows system processes (RuntimeBroker.exe, WinStore.App.exe, …)
/// were being reported as hangs, and a permanently-idle process re-alerted on every
/// suppress-window tick. Hang detection must be scoped to harness *children* and must
/// alert only once per hang episode.
///
/// EventBus is a process-wide singleton, so each test uses a unique PID range and filters
/// captured events by those PIDs to stay isolated from other tests.
/// </summary>
public sealed class HangDetectorTests
{
    private static readonly TimeSpan Old = TimeSpan.FromMinutes(20);  // > default 10-min threshold

    private static ProcessRecord Idle(int pid, int parentPid, string name, bool isHarness = false, string? harnessType = null)
    {
        var past = DateTimeOffset.UtcNow - Old;
        return new ProcessRecord
        {
            Pid              = pid,
            ParentPid        = parentPid,
            Name             = name,
            StartTime        = past,
            LastIoChangeTime = past,
            IsHarness        = isHarness,
            HarnessType      = harnessType,
            State            = ProcessState.Active,
        };
    }

    /// <summary>Subscribes a handler that captures HangDetectedEvents for the given PIDs only.</summary>
    private static List<HangDetectedEvent> CaptureHangsFor(params int[] pids)
    {
        var set = new HashSet<int>(pids);
        var hits = new List<HangDetectedEvent>();
        EventBus.Instance.Subscribe(evt =>
        {
            if (evt is HangDetectedEvent h && set.Contains(h.ProcessId))
            {
                lock (hits) hits.Add(h);
            }
        });
        return hits;
    }

    [Fact]
    public void IdleSystemProcess_WithNoHarnessAncestor_DoesNotAlert()
    {
        var tree = new ProcessTreeTracker();
        var rb   = Idle(900_101, 900_100, "RuntimeBroker.exe");  // parent not tracked → no ancestor
        tree.OnProcessCreated(rb);

        var hits = CaptureHangsFor(rb.Pid);
        var sut  = new HangDetector(EventBus.Instance, new ForemanSettings(), tree);

        sut.Check(rb);

        Assert.Empty(hits);
    }

    [Fact]
    public void IdleHarnessChild_Alerts_Once_AndNamesTheHarness()
    {
        var tree    = new ProcessTreeTracker();
        var harness = Idle(900_201, 900_200, "node.exe", isHarness: true, harnessType: "claude-code");
        var child   = Idle(900_202, harness.Pid, "bash.exe");
        tree.OnProcessCreated(harness);
        tree.OnProcessCreated(child);

        var hits = CaptureHangsFor(child.Pid);
        var sut  = new HangDetector(EventBus.Instance, new ForemanSettings(), tree);

        sut.Check(child);
        sut.Check(child);   // same hang episode — must NOT produce a second alert

        Assert.Single(hits);
        Assert.Equal(harness.Pid, hits[0].ParentHarnessPid);
        Assert.Contains("claude-code", hits[0].Message);
    }

    [Fact]
    public void HarnessProcessItself_Idle_DoesNotAlert()
    {
        // An agent idling while it waits for user input is normal, not a hang.
        var tree    = new ProcessTreeTracker();
        var harness = Idle(900_301, 900_300, "node.exe", isHarness: true, harnessType: "claude-code");
        tree.OnProcessCreated(harness);

        var hits = CaptureHangsFor(harness.Pid);
        var sut  = new HangDetector(EventBus.Instance, new ForemanSettings(), tree);

        sut.Check(harness);

        Assert.Empty(hits);
    }

    [Fact]
    public void HangEpisode_ReArms_WhenIoResumesThenHangsAgain()
    {
        var tree    = new ProcessTreeTracker();
        var harness = Idle(900_401, 900_400, "node.exe", isHarness: true, harnessType: "claude-code");
        var child   = Idle(900_402, harness.Pid, "python.exe");
        tree.OnProcessCreated(harness);
        tree.OnProcessCreated(child);

        var hits = CaptureHangsFor(child.Pid);
        var sut  = new HangDetector(EventBus.Instance, new ForemanSettings(), tree);

        sut.Check(child);   // first hang episode → alert #1

        // Simulate I/O resuming and the process hanging again: LastIoChangeTime advances
        // to a new (still-stale) timestamp. The epoch no longer matches → fresh alert.
        child.LastIoChangeTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15);
        child.State = ProcessState.Active;
        sut.Check(child);   // second, distinct hang episode → alert #2

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void MonitorAllProcesses_True_AlertsOnLoneIdleProcess()
    {
        var tree = new ProcessTreeTracker();
        var rb   = Idle(900_501, 900_500, "RuntimeBroker.exe");
        tree.OnProcessCreated(rb);

        var hits     = CaptureHangsFor(rb.Pid);
        var settings = new ForemanSettings { MonitorAllProcesses = true };
        var sut      = new HangDetector(EventBus.Instance, settings, tree);

        sut.Check(rb);

        Assert.Single(hits);
        Assert.Null(hits[0].ParentHarnessPid);  // no harness owner
    }

    [Fact]
    public void InfrastructureChild_ConhostUnderHarness_DoesNotAlert()
    {
        // conhost.exe is a real harness descendant but a passive console host — idling is
        // normal, so it must not be flagged even though it is inside a harness tree.
        var tree    = new ProcessTreeTracker();
        var harness = Idle(900_601, 900_600, "codex.exe", isHarness: true, harnessType: "codex");
        var conhost = Idle(900_602, harness.Pid, "conhost.exe");
        tree.OnProcessCreated(harness);
        tree.OnProcessCreated(conhost);

        var hits = CaptureHangsFor(conhost.Pid);
        var sut  = new HangDetector(EventBus.Instance, new ForemanSettings(), tree);

        sut.Check(conhost);

        Assert.Empty(hits);
    }
}
