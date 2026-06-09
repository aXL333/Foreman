using System.Text.Json.Nodes;
using Foreman.Core.Integration;

namespace Foreman.Core.Tests.Integration;

public sealed class ClaudeMcpConnectorTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cfg;

    public ClaudeMcpConnectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "foreman-conn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _cfg = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Connect_AddsEntry_PreservesOtherConfig_AndBacksUp()
    {
        File.WriteAllText(_cfg, """{ "numStartups": 7, "projects": { "C:\\x": { "mcpServers": {} } } }""");

        var r = ClaudeMcpConnector.Connect(54321, "TOKEN123", _cfg);

        Assert.Equal(ConnectStatus.Added, r.Status);
        Assert.True(File.Exists(_cfg + ".foreman-bak"));

        var root = JsonNode.Parse(File.ReadAllText(_cfg))!;
        Assert.Equal(7, root["numStartups"]!.GetValue<int>());                       // unrelated key preserved
        Assert.NotNull(root["projects"]!["C:\\x"]!["mcpServers"]);                    // nested project block preserved
        var foreman = root["mcpServers"]!["foreman"]!;
        Assert.Equal("http", foreman["type"]!.GetValue<string>());
        Assert.Equal("http://localhost:54321/mcp", foreman["url"]!.GetValue<string>());
        Assert.Equal("Bearer TOKEN123", foreman["headers"]!["Authorization"]!.GetValue<string>());
        Assert.True(ClaudeMcpConnector.IsConfigured(54321, _cfg));
    }

    [Fact]
    public void Connect_OnMissingFile_CreatesIt()
    {
        var r = ClaudeMcpConnector.Connect(54321, "T", _cfg);
        Assert.Equal(ConnectStatus.Added, r.Status);
        Assert.Null(r.BackupPath);                       // nothing to back up
        Assert.True(ClaudeMcpConnector.IsConfigured(54321, _cfg));
    }

    [Fact]
    public void Connect_Twice_ReportsUpdated_AndStaysValid()
    {
        ClaudeMcpConnector.Connect(54321, "old", _cfg);
        var r = ClaudeMcpConnector.Connect(54321, "new", _cfg);

        Assert.Equal(ConnectStatus.Updated, r.Status);
        var auth = JsonNode.Parse(File.ReadAllText(_cfg))!["mcpServers"]!["foreman"]!["headers"]!["Authorization"]!.GetValue<string>();
        Assert.Equal("Bearer new", auth);
    }

    [Fact]
    public void IsConfigured_FalseForWrongPortOrNoToken()
    {
        ClaudeMcpConnector.Connect(54321, "T", _cfg);
        Assert.False(ClaudeMcpConnector.IsConfigured(9999, _cfg));    // different port
        Assert.False(ClaudeMcpConnector.IsConfigured(54321, Path.Combine(_dir, "nope.json")));
    }

    [Fact]
    public void BuildCliCommand_HasTransportHeaderAndScope()
    {
        var cmd = ClaudeMcpConnector.BuildCliCommand(54321, "TOK");
        Assert.Contains("--transport http", cmd);
        Assert.Contains("http://localhost:54321/mcp", cmd);
        Assert.Contains("Authorization: Bearer TOK", cmd);
        Assert.Contains("--scope user", cmd);
    }
}
