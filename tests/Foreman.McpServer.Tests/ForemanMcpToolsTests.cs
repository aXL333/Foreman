using Foreman.Core.Heuristics;
using Foreman.Core.Models;
using Foreman.Core.Profiles;
using System.Text.Json;

namespace Foreman.McpServer.Tests;

public sealed class ForemanMcpToolsTests : IDisposable
{
    private readonly string _profileDir = Path.Combine(Path.GetTempPath(), "foreman-mcp-profile-test-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;
    private readonly ProfileMatcher _matcher;
    private readonly ProcessRecord _harness;
    private readonly ProcessRecord _child;

    public ForemanMcpToolsTests()
    {
        PatternLibrary.Instance.Initialize();

        _store = new ProfileStore(_profileDir);
        _store.Initialize();
        _matcher = new ProfileMatcher(_store);

        _harness = new ProcessRecord
        {
            Pid = 930_001,
            ParentPid = 0,
            Name = "codex.exe",
            StartTime = DateTimeOffset.UtcNow,
            IsHarness = true,
            HarnessType = "codex",
            ProfileName = "codex-default",
        };
        _child = new ProcessRecord
        {
            Pid = 930_002,
            ParentPid = _harness.Pid,
            Name = "cmd.exe",
            StartTime = DateTimeOffset.UtcNow,
        };

        ForemanMcpTools.SetState(new ForemanState
        {
            McpPort = 12345,
            GetProcessSnapshot = () => [_harness, _child],
            GetProfileByName = name => _matcher.Get(name),
            GetDefaultProfileNameByHarnessId = HarnessIntegrationRegistry.GetDefaultProfileName,
            FindHarnessAncestorByPid = pid => pid == _child.Pid || pid == _harness.Pid ? _harness : null,
            GetMcpSessionCount = () => 2,
        });
    }

    [Fact]
    public void GetMyPermissions_ResolvesProfileFromHarnessId()
    {
        using var doc = ToJson(ForemanMcpTools.GetMyPermissions(harnessId: "codex"));

        Assert.Equal("codex", doc.RootElement.GetProperty("harnessId").GetString());
        Assert.Equal("codex-default", doc.RootElement.GetProperty("profileName").GetString());
        Assert.Contains("cred-001", doc.RootElement.GetProperty("commands").GetProperty("blockedPatterns").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void ReportSuspiciousCommand_AppliesProfileBlockedRules()
    {
        using var doc = ToJson(ForemanMcpTools.ReportSuspiciousCommand(
            "reg save HKLM\\SAM sam.hiv",
            harnessId: "codex"));

        Assert.Equal("escalate", doc.RootElement.GetProperty("decision").GetString());
        Assert.Equal("cred-001", doc.RootElement.GetProperty("matchedRule").GetString());
        Assert.True(doc.RootElement.GetProperty("profileBlocked").GetBoolean());
        Assert.Equal("codex-default", doc.RootElement.GetProperty("profileName").GetString());
    }

    [Fact]
    public void ListMonitoredProcesses_ScopesToHarnessTree()
    {
        using var doc = ToJson(ForemanMcpTools.ListMonitoredProcesses(harnessId: "codex"));

        Assert.Equal("codex", doc.RootElement.GetProperty("harnessId").GetString());
        var pids = doc.RootElement.GetProperty("processes").EnumerateArray()
            .Select(e => e.GetProperty("Pid").GetInt32())
            .ToHashSet();
        Assert.Contains(_harness.Pid, pids);
        Assert.Contains(_child.Pid, pids);
    }

    [Fact]
    public void IntegrationInstructions_AndValidation_ReportHarnessSetupState()
    {
        using var instructions = ToJson(ForemanMcpTools.GetIntegrationInstructions("claude-code"));
        Assert.Equal("claude-code", instructions.RootElement.GetProperty("harnessId").GetString());
        Assert.Contains("12345", instructions.RootElement.GetProperty("mcpConfigSnippet").GetString());

        using var validation = ToJson(ForemanMcpTools.ValidateHarnessIntegration("codex"));
        Assert.True(validation.RootElement.GetProperty("knownHarness").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("profileLoaded").GetBoolean());
        Assert.Equal(2, validation.RootElement.GetProperty("runningProcessCount").GetInt32());
        Assert.Equal(2, validation.RootElement.GetProperty("mcpSessions").GetInt32());
    }

    [Fact]
    public void AuditRouting_SelectsPreferredNonSelfAuditor()
    {
        using var route = ToJson(ForemanMcpTools.GetAuditRoute("claude-code", severity: "High"));

        var selected = route.RootElement.GetProperty("selected");
        Assert.Equal("codex", selected.GetProperty("AuditorId").GetString());
        Assert.Equal("harness", selected.GetProperty("AuditorType").GetString());
        Assert.True(selected.GetProperty("available").GetBoolean());
    }

    [Fact]
    public void AuditRouting_CanRequireAvailableAuditor()
    {
        using var route = ToJson(ForemanMcpTools.GetAuditRoute("codex", severity: "High", requireAvailable: true));

        Assert.Equal(0, route.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.True(route.RootElement.GetProperty("reason").GetString()?.Contains("No auditor preference") == true);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_profileDir))
            Directory.Delete(_profileDir, recursive: true);
    }

    private static JsonDocument ToJson(object value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value));
}
