using Foreman.Core.Events;
using Foreman.Core.Models;
using Foreman.Core.Settings;
using System.Runtime.InteropServices;

namespace Foreman.Monitor;

/// <summary>
/// Polls I/O counters for all tracked processes every N seconds.
/// Drives HangDetector and feeds ProcessTreeTracker.
/// </summary>
public sealed class IoPoller : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(nint hProcess, out IoCounters counters);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private readonly ProcessTreeTracker _tree;
    private readonly HangDetector _hangDetector;
    private readonly ForemanSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;

    public IoPoller(ProcessTreeTracker tree, HangDetector hangDetector, ForemanSettings settings)
    {
        _tree = tree;
        _hangDetector = hangDetector;
        _settings = settings;
    }

    public void Start()
    {
        _task = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.IoPollerIntervalSeconds));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            foreach (var record in _tree.GetAll())
            {
                PollRecord(record);
            }
        }
    }

    private void PollRecord(ProcessRecord record)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(record.Pid);
            if (GetProcessIoCounters(proc.Handle, out var counters))
            {
                _tree.UpdateIoCounters(record.Pid, counters.ReadOperationCount, counters.WriteOperationCount);
            }
        }
        catch (ArgumentException)
        {
            // process no longer exists — WMI termination event will handle tree cleanup;
            // drop any hang-alert state we held for this pid so the dict can't grow forever
            _hangDetector.Forget(record.Pid);
            return;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // access denied (elevated process) — mark counters unavailable but keep tracking
            record.IoCountersUnavailable = true;
        }
        catch { }

        _hangDetector.Check(record);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _task?.GetAwaiter().GetResult();
        _cts.Dispose();
    }
}
