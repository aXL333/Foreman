using Foreman.Core.Mcp;

namespace Foreman.Core.Tests.Mcp;

public sealed class McpInventoryScannerTests
{
    [Fact]
    public void Parses_Http_Stdio_GlobalAndProject_Servers()
    {
        const string json = """
        {
          "mcpServers": {
            "foreman": { "type": "http", "url": "http://localhost:54321/mcp" },
            "fs":       { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:/work"] }
          },
          "projects": {
            "C:\\proj": {
              "mcpServers": {
                "evil": { "url": "https://attacker.example/mcp" }
              }
            }
          }
        }
        """;

        var entries = McpInventoryScanner.ParseClaudeJson(json, "claude-code", "test.json");

        var foreman = entries.Single(e => e.Name == "foreman");
        Assert.Equal("http", foreman.Transport);
        Assert.Equal("http://localhost:54321/mcp", foreman.Target);
        Assert.Equal("global", foreman.Scope);

        var fs = entries.Single(e => e.Name == "fs");
        Assert.Equal("stdio", fs.Transport);
        Assert.Contains("server-filesystem", fs.Target);

        var evil = entries.Single(e => e.Name == "evil");
        Assert.Equal("http", evil.Transport);                 // inferred from url
        Assert.Equal("https://attacker.example/mcp", evil.Target);
        Assert.Equal(@"C:\proj", evil.Scope);                 // project-scoped
    }

    [Fact]
    public void ChangedTarget_ProducesADifferentKey()
    {
        var a = McpInventoryScanner.ParseClaudeJson(
            """{ "mcpServers": { "x": { "url": "http://a" } } }""", "claude-code", "f").Single();
        var b = McpInventoryScanner.ParseClaudeJson(
            """{ "mcpServers": { "x": { "url": "http://b" } } }""", "claude-code", "f").Single();

        Assert.NotEqual(a.Key, b.Key);   // a swapped URL re-alerts as "new"
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "mcpServers": null }""")]
    [InlineData("""{ "mcpServers": "weird" }""")]
    public void MalformedOrEmpty_ReturnsNoEntries_AndDoesNotThrow(string json)
    {
        Assert.Empty(McpInventoryScanner.ParseClaudeJson(json, "claude-code", "f"));
    }
}
