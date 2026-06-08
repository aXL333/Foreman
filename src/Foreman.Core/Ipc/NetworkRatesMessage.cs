namespace Foreman.Core.Ipc;

/// <summary>
/// One frame streamed from the elevated ETW sidecar to the app: per-process network throughput.
/// Serialized as a single line of JSON over the local pipe. Kept in Foreman.Core so both the
/// sidecar and the app share one contract.
/// </summary>
public sealed class NetworkRatesMessage
{
    public long TimestampUnixMs { get; set; }

    /// <summary>PID → bytes/sec (send + receive) measured over the sidecar's sampling interval.</summary>
    public Dictionary<int, double> Rates { get; set; } = new();
}
