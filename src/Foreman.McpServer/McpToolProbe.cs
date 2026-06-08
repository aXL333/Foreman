using Foreman.Core.Mcp;
using ModelContextProtocol.Client;

namespace Foreman.McpServer;

/// <summary>
/// Tier-1 (opt-in) MCP client probe. Connects to an HTTP/SSE MCP server the user's harness is
/// configured to use, lists its tools, and runs <see cref="McpToolScanner"/> over the names +
/// descriptions to surface prompt-injection / exfil text smuggled into tool docs.
///
/// Deliberately does NOT launch stdio servers — spawning the very process you are suspicious of is
/// worse than not scanning it, so stdio entries are skipped by the caller. No credentials are sent
/// to third-party servers (Foreman has none for them); servers that require their own auth simply
/// fail to enumerate and are reported as "unreachable".
/// </summary>
public sealed class McpToolProbe
{
    /// <summary>
    /// Connects to one HTTP/SSE server and returns any scanner findings.
    /// Throws on connect/enumerate failure (caller decides how to surface it).
    /// </summary>
    public async Task<IReadOnlyList<McpToolFinding>> ProbeAsync(
        McpServerEntry server, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(server.Target, UriKind.Absolute, out var endpoint))
            throw new ArgumentException($"Server '{server.Name}' has no usable URL: {server.Target}");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint          = endpoint,
            Name              = $"Foreman scan: {server.Name}",
            ConnectionTimeout = timeout,
        }, null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        await using var client = await McpClient.CreateAsync(transport, null, null, cts.Token);
        var tools = await client.ListToolsAsync(options: null, cancellationToken: cts.Token);

        return McpToolScanner.Scan(
            server.Name,
            tools.Select(t => (t.Name, (string?)t.Description)));
    }
}
