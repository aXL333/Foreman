namespace Foreman.Core.Mcp;

/// <summary>One MCP server discovered in an AI harness's configuration.</summary>
public sealed record McpServerEntry(
    string Harness,     // e.g. "claude-code"
    string Name,        // the server name as configured
    string Transport,   // "http" | "sse" | "stdio" | "unknown"
    string Target,      // url (http/sse) or command + args (stdio)
    string Scope,       // "global" or a project path
    string SourceFile)  // the config file it was read from
{
    /// <summary>Stable identity for the seen-set. A changed target re-alerts (URL swap = new).</summary>
    public string Key => $"{Harness}|{Name}|{Transport}|{Target}";
}
