using System.Text.Json.Nodes;

namespace Foreman.Core.Mcp;

/// <summary>
/// Discovers the MCP servers an AI harness is configured to use, by reading its config files.
/// Tier 0: pure file + JSON reads — no network, no elevation. Currently understands Claude Code's
/// .claude.json shape (top-level mcpServers plus per-project mcpServers); structured so other
/// harnesses can be added as more sources.
/// </summary>
public static class McpInventoryScanner
{
    /// <summary>Config files to scan by default, paired with the harness they belong to.</summary>
    public static IEnumerable<(string Path, string Harness)> DefaultSources()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return (Path.Combine(home, ".claude.json"), "claude-code");
    }

    public static List<McpServerEntry> Scan() => Scan(DefaultSources());

    public static List<McpServerEntry> Scan(IEnumerable<(string Path, string Harness)> sources)
    {
        var found = new List<McpServerEntry>();
        foreach (var (path, harness) in sources)
        {
            if (!File.Exists(path)) continue;
            try { found.AddRange(ParseClaudeJson(File.ReadAllText(path), harness, path)); }
            catch { /* unreadable / malformed — skip this source */ }
        }
        // A server can appear both globally and under a project; collapse by identity.
        return found.GroupBy(e => e.Key).Select(g => g.First()).ToList();
    }

    /// <summary>Parses Claude Code's .claude.json: top-level "mcpServers" plus "projects.*.mcpServers".</summary>
    public static List<McpServerEntry> ParseClaudeJson(string json, string harness, string sourceFile)
    {
        var list = new List<McpServerEntry>();
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return list; }
        if (root is null) return list;

        AddServers(list, root["mcpServers"], harness, "global", sourceFile);

        if (root["projects"] is JsonObject projects)
            foreach (var project in projects)
                AddServers(list, project.Value?["mcpServers"], harness, project.Key, sourceFile);

        return list;
    }

    private static void AddServers(List<McpServerEntry> list, JsonNode? mcpServers, string harness, string scope, string sourceFile)
    {
        if (mcpServers is not JsonObject servers) return;

        foreach (var kv in servers)
        {
            var cfg     = kv.Value;
            var type    = Str(cfg?["type"])?.ToLowerInvariant();
            var url     = Str(cfg?["url"]);
            var command = Str(cfg?["command"]);

            var transport = type
                ?? (!string.IsNullOrEmpty(url) ? "http"
                    : !string.IsNullOrEmpty(command) ? "stdio"
                    : "unknown");

            string target;
            if (!string.IsNullOrEmpty(url))
            {
                target = url;
            }
            else if (!string.IsNullOrEmpty(command))
            {
                var args = (cfg?["args"] as JsonArray)?.Select(a => Str(a) ?? "").Where(s => s.Length > 0)
                           ?? Enumerable.Empty<string>();
                target = (command + " " + string.Join(" ", args)).Trim();
            }
            else
            {
                target = "";
            }

            list.Add(new McpServerEntry(harness, kv.Key, transport, target, scope, sourceFile));
        }
    }

    private static string? Str(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return null; }   // value wasn't a string
    }
}
