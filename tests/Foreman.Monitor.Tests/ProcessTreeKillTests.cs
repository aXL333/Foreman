using Foreman.Core.Models;
using Foreman.Monitor;

namespace Foreman.Monitor.Tests;

/// <summary>
/// The kill path only ever terminates a process Foreman tracks whose identity (PID + captured
/// WMI start time) still matches the live record. These cover the security-critical refusals;
/// none of them reach an actual Process.Kill (each is rejected before that point).
/// </summary>
public sealed class ProcessTreeKillTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ProcessRecord Rec(int pid, string name, DateTimeOffset start)
        => new() { Pid = pid, ParentPid = 0, Name = name, StartTime = start };

    [Fact]
    public void KillProcess_UntrackedPid_IsRefused()
    {
        var tree = new ProcessTreeTracker();
        // A bare PID Foreman never tracked (e.g. a forged target) — refused, no OS call.
        Assert.False(tree.KillProcess(999_999, T0));
    }

    [Fact]
    public void KillProcess_SystemPid_IsRefused()
    {
        var tree = new ProcessTreeTracker();
        tree.OnProcessCreated(Rec(4, "System", T0));
        Assert.False(tree.KillProcess(4, T0));   // PID 0/4 are never killable
    }

    [Fact]
    public void KillProcess_ForemanOwnPid_IsRefused()
    {
        var tree = new ProcessTreeTracker();
        var self = Environment.ProcessId;
        tree.OnProcessCreated(Rec(self, "Foreman.App", T0));

        Assert.False(tree.KillProcess(self, T0));                               // never self-terminate
        Assert.False(System.Diagnostics.Process.GetCurrentProcess().HasExited);  // and prove we didn't
    }

    [Fact]
    public void KillProcess_NullStartTime_IsRefused()
    {
        var tree = new ProcessTreeTracker();
        tree.OnProcessCreated(Rec(940_001, "cmd.exe", T0));
        // No identity pin from the alert → refuse rather than kill a bare PID.
        Assert.False(tree.KillProcess(940_001, expectedStartTime: null));
    }

    [Fact]
    public void KillProcess_RecycledPid_StartTimeMismatch_IsRefused()
    {
        var tree = new ProcessTreeTracker();
        // The currently-tracked record for this PID started at a different time than the alert
        // captured — i.e. the PID was recycled. Must refuse before touching the OS.
        tree.OnProcessCreated(Rec(940_002, "newproc.exe", T0));
        Assert.False(tree.KillProcess(940_002, expectedStartTime: T0.AddMinutes(-5)));
    }
}
