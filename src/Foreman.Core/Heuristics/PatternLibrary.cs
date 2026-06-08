using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foreman.Core.Heuristics;

/// <summary>
/// Loads all pattern JSON files from embedded resources and pre-compiles the regexes.
/// Call Initialize() once at startup before any analysis.
/// </summary>
public sealed class PatternLibrary
{
    public static PatternLibrary Instance { get; } = new();

    private List<(PatternRule Rule, Regex Compiled)> _rules = [];

    private PatternLibrary() { }

    public void Initialize()
    {
        var asm = Assembly.GetAssembly(typeof(PatternLibrary))!;
        var resources = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".patterns.") && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        var loaded = new List<(PatternRule Rule, Regex Compiled)>();
        foreach (var res in resources)
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            var file = JsonSerializer.Deserialize<PatternFile>(stream);
            if (file is null) continue;

            foreach (var rule in file.Rules)
            {
                try
                {
                    var rx = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
                    loaded.Add((rule, rx));
                }
                catch (ArgumentException)
                {
                    // bad regex in pattern file — skip, don't crash
                }
            }
        }

        // sort descending by severity so we hit critical rules first
        _rules = loaded.OrderByDescending(r => r.Rule.ParsedSeverity).ToList();
    }

    public IReadOnlyList<(PatternRule Rule, Regex Compiled)> Rules => _rules;
}
