using System.Text.Json;
using Foreman.Core.Ipc;

namespace Foreman.Core.Tests.Ipc;

public sealed class NetworkRatesMessageTests
{
    [Fact]
    public void RoundTrips_IntKeyedRates_AsASingleJsonLine()
    {
        var msg = new NetworkRatesMessage
        {
            TimestampUnixMs = 1_700_000_000_000,
            Rates = { [1234] = 5678.5, [4242] = 0 },
        };

        var json = JsonSerializer.Serialize(msg);
        var back = JsonSerializer.Deserialize<NetworkRatesMessage>(json);

        Assert.NotNull(back);
        Assert.Equal(msg.TimestampUnixMs, back!.TimestampUnixMs);
        Assert.Equal(5678.5, back.Rates[1234]);   // int keys survive the JSON string-key round-trip
        Assert.Equal(0, back.Rates[4242]);

        // The pipe protocol is newline-delimited, so a single frame must never contain a newline.
        Assert.DoesNotContain('\n', json);
    }
}
