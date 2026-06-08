using Foreman.Core.Models;
using Foreman.Monitor;

namespace Foreman.Monitor.Tests;

/// <summary>
/// A hook or shell spawned by a harness matches no harness rule itself, so it must be attributed
/// to the harness it descends from by walking the process tree. These tests cover that walk.
/// </summary>
public sealed class ProcessTreeAttributionTests
{
    private static ProcessRecord Rec(int pid, int parentPid, string name, bool isHarness = false, string? harnessType = null)
        => new()
        {
            Pid         = pid,
            ParentPid   = parentPid,
            Name        = name,
            StartTime   = DateTimeOffset.UtcNow,
            IsHarness   = isHarness,
            HarnessType = harnessType,
        };

    [Fact]
    public void FindHarnessTypeAncestor_AttributesHookChildToHarness()
    {
        var tree    = new ProcessTreeTracker();
        var node    = Rec(1000, 900, "node.exe", isHarness: true, harnessType: "claude-code");
        var pwsh    = Rec(1001, node.Pid, "powershell.exe");           // the hook launcher
        var pwshInner = Rec(1002, pwsh.Pid, "powershell.exe");         // the script host it spawns
        tree.OnProcessCreated(node);
        tree.OnProcessCreated(pwsh);
        tree.OnProcessCreated(pwshInner);

        Assert.Equal("claude-code", tree.FindHarnessTypeAncestor(pwsh.Pid)?.HarnessType);
        Assert.Equal("claude-code", tree.FindHarnessTypeAncestor(pwshInner.Pid)?.HarnessType);
        Assert.Equal(node.Pid,      tree.FindHarnessAncestor(pwshInner.Pid)?.Pid);
    }

    [Fact]
    public void FindHarnessTypeAncestor_ReturnsNull_OutsideAnyHarnessTree()
    {
        var tree = new ProcessTreeTracker();
        var lone = Rec(2001, 2000, "RuntimeBroker.exe");   // parent 2000 not tracked
        tree.OnProcessCreated(lone);

        Assert.Null(tree.FindHarnessTypeAncestor(lone.Pid));
        Assert.Null(tree.FindHarnessAncestor(lone.Pid));
    }

    [Fact]
    public void FindHarnessTypeAncestor_AttributesDisabledHarness_ButFindHarnessAncestorDoesNot()
    {
        // A disabled harness keeps its HarnessType but has IsHarness == false.
        // Attribution (HarnessType) should still resolve; active-monitoring (IsHarness) should not.
        var tree  = new ProcessTreeTracker();
        var node  = Rec(3000, 2900, "node.exe", isHarness: false, harnessType: "codex");
        var child = Rec(3001, node.Pid, "bash.exe");
        tree.OnProcessCreated(node);
        tree.OnProcessCreated(child);

        Assert.Equal("codex", tree.FindHarnessTypeAncestor(child.Pid)?.HarnessType);
        Assert.Null(tree.FindHarnessAncestor(child.Pid));
    }
}
