using System.IO;
using System.Windows;

namespace Foreman.App;

/// <summary>
/// On first launch, detects whether Claude Code is installed and shows
/// a one-time welcome dialog explaining Foreman's safety monitor role.
/// </summary>
public static class FirstRunDetector
{
    private static readonly string _flagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Foreman", "first-run-complete.flag");

    public static void RunIfNeeded(int mcpPort)
    {
        if (File.Exists(_flagPath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_flagPath)!);
        File.WriteAllText(_flagPath, DateTime.UtcNow.ToString("O"));

        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var hasClaudeCode = Directory.Exists(claudeDir);

        var msg = hasClaudeCode
            ? $"""
              Welcome to Foreman!

              Claude Code has been detected on this machine.

              Foreman's MCP server is running on port {mcpPort}.
              To connect Claude Code, run:

                claude mcp add --transport http foreman http://localhost:{mcpPort}/mcp --header "Authorization: Bearer <token>" --scope user

              Your token is in %LocalAppData%\Foreman\mcp.token — the /mcp endpoint
              requires it; without it the connection is refused (401).

              Foreman will watch agent process trees, flag risky CLI patterns,
              attribute spawned processes and expose audit routes so another
              harness or API can review the session.

              Left-click the tray icon for the dashboard. Right-click for tools and settings.
              Double-click to open the event log.
              """
            : $"""
              Welcome to Foreman!

              Foreman is running on port {mcpPort} as an agent safety monitor.

              To connect an MCP client, point it at:
                http://localhost:{mcpPort}/mcp

              Left-click the tray icon for the dashboard. Right-click for tools and settings.
              Double-click to open the event log.
              """;

        MessageBox.Show(msg, "Foreman — First Run", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
