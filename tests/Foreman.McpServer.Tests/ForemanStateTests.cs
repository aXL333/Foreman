using Foreman.Core.Events;
using Foreman.Core.Models;

namespace Foreman.McpServer.Tests;

public sealed class ForemanStateTests
{
    [Fact]
    public void InfoEvents_DoNotCountAsActiveAlerts()
    {
        var state = new ForemanState();

        ((IEventSink)state).OnEvent(new InfoEvent(DateTimeOffset.UtcNow, "Foreman", "startup"));

        Assert.Equal(0, state.ActiveAlerts);
        Assert.False(state.HasCritical);
    }

    [Fact]
    public void AcknowledgedAlerts_DoNotRemainActive()
    {
        var state = new ForemanState();
        var alert = new CommandAlertEvent(
            DateTimeOffset.UtcNow,
            ForemanSeverity.High,
            "cmd.exe (pid 1)",
            "suspicious",
            "reg save HKLM\\SAM sam.hiv",
            "cred-001",
            "SAM hive export",
            "credential access",
            "review",
            1);

        ((IEventSink)state).OnEvent(alert);
        Assert.Equal(1, state.ActiveAlerts);
        Assert.True(state.HasCritical);

        Assert.True(state.AcknowledgeAlert(alert.Id));

        Assert.Equal(0, state.ActiveAlerts);
        Assert.False(state.HasCritical);
    }
}
