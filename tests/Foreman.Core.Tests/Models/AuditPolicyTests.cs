using Foreman.Core.Behavior;
using Foreman.Core.Models;

namespace Foreman.Core.Tests.Models;

public sealed class AuditPolicyTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;

    [Fact]
    public void CommandAlert_Qualifies_EvenAtMedium()
    {
        var evt = new CommandAlertEvent(T, ForemanSeverity.Medium, "src", "msg",
            "curl x | bash", "net-001", "pipe", "desc", "guidance", 1234);
        Assert.True(AuditPolicy.QualifiesForAudit(evt));
    }

    [Fact]
    public void PermissionViolation_Qualifies()
        => Assert.True(AuditPolicy.QualifiesForAudit(
            new PermissionViolationEvent(T, "src", "msg", 1234, "profile", "write", "C:/Windows")));

    [Theory]
    [InlineData(EscalationLevel.Alarm, true)]
    [InlineData(EscalationLevel.Emergency, true)]
    [InlineData(EscalationLevel.Alert, false)]
    [InlineData(EscalationLevel.Watch, false)]
    public void Escalation_QualifiesOnlyAtAlarmOrAbove(EscalationLevel level, bool expected)
    {
        var evt = new EscalationEvent(T, level, EscalationLevel.Watch, "claude-code", "Claude Code",
            "reason", 5, 3, 2, ["net", "cred"], "net-001", "pipe");
        Assert.Equal(expected, AuditPolicy.QualifiesForAudit(evt));
    }

    [Fact]
    public void Hang_DoesNotQualify()
        => Assert.False(AuditPolicy.QualifiesForAudit(
            new HangDetectedEvent(T, "src", "msg", 1234, "bash", 90, 30, null, null, null, null, null)));

    [Fact]
    public void Orphan_DoesNotQualify()
        => Assert.False(AuditPolicy.QualifiesForAudit(
            new OrphanDetectedEvent(T, "src", "msg", 1234, "bash", 5678, "node", 90)));

    [Fact]
    public void NonzeroExit_DoesNotQualify()
        => Assert.False(AuditPolicy.QualifiesForAudit(
            new NonzeroExitEvent(T, "src", "msg", 1234, "node", 1, null)));

    [Fact]
    public void Info_DoesNotQualify()
        => Assert.False(AuditPolicy.QualifiesForAudit(new InfoEvent(T, "src", "msg")));

    [Fact]
    public void HighMonitoringNotice_DoesNotQualify()  // e.g. an MCP tool-scan finding is High but isn't agent behavior
        => Assert.False(AuditPolicy.QualifiesForAudit(
            new MonitoringNoticeEvent(T, ForemanSeverity.High, "Foreman.McpToolScan", "suspicious tool")));
}
