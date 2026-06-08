using System.IO;
using System.Windows;

namespace Foreman.App;

/// <summary>
/// On first launch, detects whether Claude Code is installed and shows
/// a one-time welcome dialog explaining how to connect Foreman.
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

        var mcpJson = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mcp.json");
        var mcpConfigured = File.Exists(mcpJson);

        var msg = hasClaudeCode
            ? $"""
              Welcome to Foreman!

              Claude Code has been detected on this machine.

              Foreman's MCP server is running on port {mcpPort}.
              {(mcpConfigured
                  ? "Your ~/.mcp.json is already configured — Claude Code will connect automatically."
                  : $"To connect Claude Code, run:\n\n  claude mcp add foreman http://localhost:{mcpPort}/mcp\n\nOr add to ~/.mcp.json:\n  \"foreman\": {{ \"type\": \"http\", \"url\": \"http://localhost:{mcpPort}/mcp\" }}")}

              Foreman will monitor harness processes, detect hangs and orphans,
              and alert you to dangerous CLI patterns — all from the system tray.

              Right-click the tray icon to open the log, or double-click to view events.
              """
            : $"""
              Welcome to Foreman!

              Foreman is running on port {mcpPort} and monitoring this machine.

              To connect an MCP client, point it at:
                http://localhost:{mcpPort}/mcp

              Right-click the tray icon to open the log, or double-click to view events.
              """;

        MessageBox.Show(msg, "Foreman — First Run", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
