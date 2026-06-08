using Foreman.Core.Models;
using System.Text.RegularExpressions;

namespace Foreman.Core.Heuristics;

public sealed class CommandAnalyzer
{
    public static CommandAnalyzer Instance { get; } = new();

    private CommandAnalyzer() { }

    /// <summary>
    /// Evaluates a command line against all loaded rules.
    /// Returns null if no rule matches or the match is suppressed.
    /// Call from any thread — thread-safe after PatternLibrary.Initialize().
    /// </summary>
    public RuleMatch? Analyze(string commandLine, string? processName = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        foreach (var (rule, regex) in PatternLibrary.Instance.Rules)
        {
            try
            {
                var m = regex.Match(commandLine);
                if (!m.Success) continue;

                if (FalsePositiveFilter.IsSuppressed(rule, commandLine, processName)) continue;

                return new RuleMatch(
                    rule.Id,
                    rule.Name,
                    rule.Description,
                    rule.Guidance,
                    rule.ParsedSeverity,
                    rule.Id.Split('-')[0],
                    m.Value
                );
            }
            catch (RegexMatchTimeoutException)
            {
                // regex timed out — move on, don't hang
            }
        }

        return null;
    }
}
