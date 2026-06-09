using Foreman.Core.Integration;
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

    public static void RunIfNeeded(int mcpPort, string mcpToken)
    {
        if (File.Exists(_flagPath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_flagPath)!);
        File.WriteAllText(_flagPath, DateTime.UtcNow.ToString("O"));

        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var hasClaudeCode = Directory.Exists(claudeDir);

        if (hasClaudeCode)
        {
            var choice = MessageBox.Show(
                $"""
                Welcome to Foreman — a safety monitor for AI coding agents.

                Claude Code was detected, and Foreman's MCP server is running on port {mcpPort}.

                Connect Claude Code to Foreman now?

                Yes — Foreman adds a user-scope "foreman" MCP entry to ~/.claude.json
                      (a backup is saved). Restart Claude Code afterwards to apply.
                No  — show the manual command instead.

                (Left-click the tray icon for the dashboard, right-click for tools.)
                """,
                "Foreman — First Run", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                var r = ClaudeMcpConnector.Connect(mcpPort, mcpToken);
                MessageBox.Show(
                    r.Status == ConnectStatus.Failed
                        ? $"Couldn't update Claude Code's config automatically:\n\n{r.Message}\n\n" +
                          "You can connect later from the tray menu → Connect Claude Code."
                        : $"{r.Message}\n\nRestart Claude Code to connect." +
                          (r.BackupPath is { } b ? $"\n\nBackup saved: {b}" : ""),
                    "Foreman — Connect Claude Code", MessageBoxButton.OK,
                    r.Status == ConnectStatus.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "To connect Claude Code later, run this in a terminal:\n\n" +
                    "  " + ClaudeMcpConnector.BuildCliCommand(mcpPort, "<token>") + "\n\n" +
                    "Your token is in %LocalAppData%\\Foreman\\mcp.token. Or use the tray menu → Connect Claude Code.",
                    "Foreman — Connect Claude Code", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        MessageBox.Show(
            $"""
            Welcome to Foreman!

            Foreman is running on port {mcpPort} as an agent safety monitor.

            To connect an MCP client, point it at:
              http://localhost:{mcpPort}/mcp
            (the /mcp endpoint needs the bearer token in %LocalAppData%\Foreman\mcp.token)

            Left-click the tray icon for the dashboard. Right-click for tools and settings.
            Double-click to open the event log.
            """,
            "Foreman — First Run", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
