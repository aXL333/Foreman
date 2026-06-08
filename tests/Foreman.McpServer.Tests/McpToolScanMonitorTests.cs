using Foreman.McpServer;

namespace Foreman.McpServer.Tests;

public sealed class McpToolScanMonitorTests
{
    private const int OwnPort = 54321;

    [Theory]
    [InlineData("http://localhost:8080/mcp", true)]      // third-party local server on another port
    [InlineData("https://remote.example/mcp", true)]     // remote http(s)
    [InlineData("http://127.0.0.1:9000/mcp", true)]      // loopback, different port
    public void Scannable_HttpEndpoints(string target, bool expected)
        => Assert.Equal(expected, McpToolScanMonitor.IsScannableTarget(target, OwnPort));

    [Theory]
    [InlineData("http://localhost:54321/mcp")]           // Foreman's own server
    [InlineData("http://127.0.0.1:54321/mcp")]           // own server via loopback IP
    public void NotScannable_OwnServer(string target)
        => Assert.False(McpToolScanMonitor.IsScannableTarget(target, OwnPort));

    [Theory]
    [InlineData("npx -y @modelcontextprotocol/server-filesystem C:/work")]  // stdio command, not a URL
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://host/x")]                                             // non-http scheme
    public void NotScannable_StdioOrNonHttp(string target)
        => Assert.False(McpToolScanMonitor.IsScannableTarget(target, OwnPort));
}
