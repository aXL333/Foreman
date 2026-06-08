using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Foreman.Core.Ipc;
using Foreman.EtwSidecar;
using Microsoft.Diagnostics.Tracing.Session;

// Foreman ETW network sidecar (capture-only, elevated).
//   Usage: Foreman.EtwSidecar --pipe <name> --nonce <token> --parent <pid>
// It connects to the app's local pipe, proves itself with the nonce, then streams per-PID
// network byte rates. It exits when the pipe breaks or the parent app exits, so it never
// lingers elevated.

return Run(args);

static int Run(string[] args)
{
    string? pipeName = null, nonce = null;
    var parentPid = 0;
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        switch (args[i])
        {
            case "--pipe":   pipeName = args[i + 1]; break;
            case "--nonce":  nonce = args[i + 1]; break;
            case "--parent": _ = int.TryParse(args[i + 1], out parentPid); break;
        }
    }

    if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(nonce)) return 2;   // bad args
    if (TraceEventSession.IsElevated() != true) return 3;                          // must be admin

    using var capture = new NetworkCapture();
    capture.Start();

    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
    try { pipe.Connect(5000); }
    catch { return 4; }   // app never opened the pipe

    using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
    try { writer.WriteLine(nonce); }   // handshake
    catch { return 5; }

    var parent = SafeGetProcess(parentPid);
    var clock = Stopwatch.StartNew();
    var interval = TimeSpan.FromMilliseconds(1500);

    while (pipe.IsConnected)
    {
        Thread.Sleep(interval);
        if (parent is { HasExited: true }) break;

        var elapsed = clock.Elapsed.TotalSeconds;
        clock.Restart();
        if (elapsed <= 0.1) continue;

        var drained = capture.DrainAndReset();
        var msg = new NetworkRatesMessage { TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        foreach (var (pid, bytes) in drained)
            msg.Rates[pid] = bytes / elapsed;

        try { writer.WriteLine(JsonSerializer.Serialize(msg)); }
        catch { break; }   // pipe gone — app exited
    }

    return 0;
}

static Process? SafeGetProcess(int pid)
{
    if (pid <= 0) return null;
    try { return Process.GetProcessById(pid); }
    catch { return null; }
}
