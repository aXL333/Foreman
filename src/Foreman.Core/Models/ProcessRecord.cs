namespace Foreman.Core.Models;

public enum ProcessState
{
    Active,
    Hanging,
    Orphaned,
    Terminated,
}

public sealed class ProcessRecord
{
    public int Pid { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public int ParentPid { get; init; }
    public bool IsHarness { get; set; }
    public string? HarnessType { get; set; }
    public string? ProfileName { get; set; }
    public ProcessState State { get; set; } = ProcessState.Active;

    public ulong LastReadOps { get; set; }
    public ulong LastWriteOps { get; set; }
    public DateTimeOffset LastIoChangeTime { get; set; }
    public bool IoCountersUnavailable { get; set; }

    public int UptimeMinutes => (int)(DateTimeOffset.UtcNow - StartTime).TotalMinutes;
    public int SilentMinutes => (int)(DateTimeOffset.UtcNow - LastIoChangeTime).TotalMinutes;

    // stable key: pid + start tick avoids reuse confusion
    public string Key => $"{Pid}:{StartTime.ToUnixTimeMilliseconds()}";
}
