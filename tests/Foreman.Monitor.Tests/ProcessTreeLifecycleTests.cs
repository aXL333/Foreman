using Foreman.Core.Models;
using Foreman.Monitor;

namespace Foreman.Monitor.Tests;

public sealed class ProcessTreeLifecycleTests
{
    private static ProcessRecord Rec(int pid, int parentPid, string name, bool isHarness = false, string? harnessType = null)
        => new()
        {
            Pid = pid,
            ParentPid = parentPid,
            Name = name,
            StartTime = DateTimeOffset.UtcNow,
            IsHarness = isHarness,
            HarnessType = harnessType,
        };

    [Fact]
    public void OnProcessDeleted_ReturnsDeletedRecord_AndMarksChildrenOrphaned()
    {
        var tree = new ProcessTreeTracker();
        var harness = Rec(910_001, 900_000, "codex.exe", isHarness: true, harnessType: "codex");
        var child = Rec(910_002, harness.Pid, "python.exe");
        tree.OnProcessCreated(harness);
        tree.OnProcessCreated(child);

        var orphans = tree.OnProcessDeleted(harness.Pid, harness.StartTime, out var deleted);

        Assert.Same(harness, deleted);
        Assert.Single(orphans);
        Assert.Same(child, orphans[0]);
        Assert.Equal(ProcessState.Orphaned, child.State);
    }

    [Fact]
    public void OnProcessDeleted_UsesStartTime_WhenPidWasReused()
    {
        var tree = new ProcessTreeTracker();
        var oldStart = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var newStart = DateTimeOffset.UtcNow;
        var old = new ProcessRecord
        {
            Pid = 910_101,
            ParentPid = 0,
            Name = "old.exe",
            StartTime = oldStart,
        };
        var newer = new ProcessRecord
        {
            Pid = 910_101,
            ParentPid = 0,
            Name = "new.exe",
            StartTime = newStart,
        };

        tree.OnProcessCreated(old);
        tree.OnProcessCreated(newer);

        tree.OnProcessDeleted(old.Pid, oldStart, out var deleted);

        Assert.Same(old, deleted);
        Assert.Same(newer, tree.GetByPid(newer.Pid));
    }

    [Fact]
    public void GetTreeByHarnessType_IncludesHarnessDescendants()
    {
        var tree = new ProcessTreeTracker();
        var harness = Rec(910_201, 0, "codex.exe", isHarness: true, harnessType: "codex");
        var child = Rec(910_202, harness.Pid, "powershell.exe");
        tree.OnProcessCreated(harness);
        tree.OnProcessCreated(child);

        var pids = tree.GetTreeByHarnessType("codex").Select(r => r.Pid).ToHashSet();

        Assert.Contains(harness.Pid, pids);
        Assert.Contains(child.Pid, pids);
    }
}
