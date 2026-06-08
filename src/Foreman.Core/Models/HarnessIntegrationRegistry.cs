namespace Foreman.Core.Models;

public sealed record HarnessIntegration(
    string HarnessId,
    string DisplayName,
    string DefaultProfileName,
    string[] TrustedHookPathMarkers,
    string[] LauncherSuppressedRuleIds,
    string SetupHint,
    string McpConfigSnippet);

public static class HarnessIntegrationRegistry
{
    public static readonly IReadOnlyList<HarnessIntegration> All =
    [
        new(
            "claude-code",
            "Claude Code",
            "claude-code-default",
            [@".claude\hooks\", ".claude/hooks/"],
            ["win-002"],
            "Run: claude mcp add foreman http://localhost:{port}/mcp",
            """
            {
              "mcpServers": {
                "foreman": {
                  "type": "http",
                  "url": "http://localhost:{port}/mcp"
                }
              }
            }
            """),
        new(
            "codex",
            "Codex CLI",
            "codex-default",
            [],
            [],
            "Add Foreman's HTTP MCP endpoint to your Codex MCP configuration.",
            """
            {
              "mcpServers": {
                "foreman": {
                  "type": "http",
                  "url": "http://localhost:{port}/mcp"
                }
              }
            }
            """),
    ];

    public static HarnessIntegration? Get(string harnessId) =>
        All.FirstOrDefault(h => string.Equals(h.HarnessId, harnessId, StringComparison.OrdinalIgnoreCase));

    public static string? GetDefaultProfileName(string harnessId) =>
        Get(harnessId)?.DefaultProfileName;

    public static KnownHarness? GetKnownHarness(string harnessId) =>
        KnownHarnesses.GetById(harnessId);
}
