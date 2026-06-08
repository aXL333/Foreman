using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foreman.Monitor;

/// <summary>
/// Reads .claude/settings.json from a harness working directory to understand
/// which permissions the harness has been granted.
/// </summary>
public static class ClaudeSettingsReader
{
    public static ClaudeHarnessConfig? TryRead(string workingDirectory)
    {
        foreach (var candidate in SettingsCandidates(workingDirectory))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                return Parse(json);
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> SettingsCandidates(string dir)
    {
        yield return Path.Combine(dir, ".claude", "settings.json");
        yield return Path.Combine(dir, ".claude", "settings.local.json");
        // walk up one level (harness might be run from a project subdir)
        var parent = Path.GetDirectoryName(dir);
        if (parent is not null)
        {
            yield return Path.Combine(parent, ".claude", "settings.json");
            yield return Path.Combine(parent, ".claude", "settings.local.json");
        }
    }

    private static ClaudeHarnessConfig Parse(string json)
    {
        var doc = JsonNode.Parse(json);
        var config = new ClaudeHarnessConfig();

        var permissions = doc?["permissions"];
        if (permissions is not null)
        {
            config.Allow = permissions["allow"]?.AsArray()
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToList() ?? [];

            config.Deny = permissions["deny"]?.AsArray()
                .Select(n => n?.GetValue<string>() ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToList() ?? [];
        }

        // Extract hook commands so we know which child processes are hooks
        var hooks = doc?["hooks"];
        if (hooks is not null)
        {
            foreach (var hookEntry in hooks.AsObject())
            {
                if (hookEntry.Value is not JsonArray arr) continue;
                foreach (var group in arr)
                {
                    var hookList = group?["hooks"]?.AsArray();
                    if (hookList is null) continue;
                    foreach (var h in hookList)
                    {
                        var cmd = h?["command"]?.GetValue<string>();
                        if (cmd is not null) config.HookCommands.Add(cmd);
                    }
                }
            }
        }

        return config;
    }
}

public sealed class ClaudeHarnessConfig
{
    public List<string> Allow { get; set; } = [];
    public List<string> Deny  { get; set; } = [];
    public List<string> HookCommands { get; set; } = [];
}
