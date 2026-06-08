namespace Foreman.Core.Heuristics;

/// <summary>
/// Suppresses known-safe matches to reduce noise.
/// </summary>
public static class FalsePositiveFilter
{
    private static readonly HashSet<string> _suppressedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Foreman.App",
        "Foreman.App.exe",
        "dotnet",         // test runners
        "xunit.console",
        "pytest",
        "vstest.console",
    };

    // A harness launching its OWN configured hook scripts lives under one of these paths.
    // (Claude Code stores hooks in .claude/hooks/ and configures them in .claude/settings.json.)
    private static readonly string[] _harnessHookMarkers =
    [
        @".claude\hooks\",
        ".claude/hooks/",
    ];

    // "Launcher hygiene" rules: how a harness legitimately invokes its own hook scripts.
    // Bypassing the execution policy and skipping the profile is expected when a harness runs
    // a user-configured hook — it is not the agent doing something suspicious. We suppress
    // ONLY these launcher rules for hook paths; anything the hook script then does spawns its
    // own processes, which Foreman still analyzes normally.
    private static readonly HashSet<string> _launcherHygieneRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "win-001",   // encoded command
        "win-002",   // execution policy bypass
    };

    public static bool IsSuppressed(PatternRule rule, string commandLine, string? processName)
    {
        if (processName is not null && _suppressedProcesses.Contains(processName))
            return true;

        if (_launcherHygieneRules.Contains(rule.Id) && IsHarnessOwnHook(commandLine))
            return true;

        return false;
    }

    private static bool IsHarnessOwnHook(string commandLine)
    {
        foreach (var marker in _harnessHookMarkers)
            if (commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
