using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Foreman.EtwSidecar;

/// <summary>
/// An ETW kernel-network session that accumulates per-PID network bytes (TCP + UDP, send + receive).
/// Requires elevation. Call Start() to begin processing; DrainAndReset() returns the bytes seen
/// since the previous drain so the caller can turn them into a rate; Dispose() stops the session.
/// </summary>
internal sealed class NetworkCapture : IDisposable
{
    private readonly TraceEventSession _session;
    private readonly ConcurrentDictionary<int, long> _bytes = new();
    private Thread? _thread;

    public NetworkCapture()
    {
        // A private (non-"NT Kernel Logger") kernel session; Win8+ allows multiple.
        _session = new TraceEventSession("Foreman-Etw-Net")
        {
            StopOnDispose = true,
        };
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var k = _session.Source.Kernel;
        k.TcpIpSend += d => Add(d.ProcessID, d.size);
        k.TcpIpRecv += d => Add(d.ProcessID, d.size);
        k.UdpIpSend += d => Add(d.ProcessID, d.size);
        k.UdpIpRecv += d => Add(d.ProcessID, d.size);
    }

    private void Add(int pid, int size)
    {
        if (pid > 0 && size > 0)
            _bytes.AddOrUpdate(pid, size, (_, current) => current + size);
    }

    public void Start()
    {
        _thread = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch { /* session stopped */ }
        })
        {
            IsBackground = true,
            Name = "ForemanEtwNet",
        };
        _thread.Start();
    }

    /// <summary>Bytes accumulated per PID since the last call; resets the counters.</summary>
    public Dictionary<int, long> DrainAndReset()
    {
        var snapshot = new Dictionary<int, long>(_bytes.Count);
        foreach (var pid in _bytes.Keys)
            if (_bytes.TryRemove(pid, out var bytes))
                snapshot[pid] = bytes;
        return snapshot;
    }

    public void Dispose()
    {
        try { _session.Dispose(); } catch { /* best-effort */ }
    }
}
