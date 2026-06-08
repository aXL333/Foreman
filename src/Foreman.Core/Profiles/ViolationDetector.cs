using Foreman.Core.Events;
using Foreman.Core.Models;

namespace Foreman.Core.Profiles;

/// <summary>
/// Checks a command line or file path against the matched profile and emits
/// PermissionViolationEvent when the profile's rules are breached.
/// </summary>
public sealed class ViolationDetector
{
    private readonly ProfileMatcher _matcher;
    private readonly EventBus _bus;

    public ViolationDetector(ProfileMatcher matcher, EventBus bus)
    {
        _matcher = matcher;
        _bus = bus;
    }

    /// <summary>
    /// Called by the WMI watcher after a command is analysed.
    /// If the command matches a blocked pattern in the harness profile, emit a violation.
    /// </summary>
    public void CheckCommandLine(ProcessRecord record, string commandLine)
    {
        var profile = _matcher.Match(record);
        if (profile is null) return;
        if (profile.Commands.EnforceMode == "monitor") return;

        foreach (var patternId in profile.Commands.BlockedPatterns)
        {
            // simple substring match against rule ID prefix — real implementation
            // would cross-reference against CommandAnalyzer result already computed
            if (commandLine.Contains(patternId, StringComparison.OrdinalIgnoreCase))
            {
                Emit(record, profile.Name, "CommandBlocked", $"Pattern '{patternId}' matched in: {Truncate(commandLine)}");
                return;
            }
        }
    }

    /// <summary>
    /// Checks a file path against the profile's denied paths.
    /// </summary>
    public void CheckFilePath(ProcessRecord record, string path)
    {
        var profile = _matcher.Match(record);
        if (profile is null) return;
        if (profile.FileSystem.EnforceMode == "monitor") return;

        foreach (var denied in profile.FileSystem.DeniedPaths)
        {
            var expanded = ExpandVars(denied);
            if (PathMatchesGlob(path, expanded))
            {
                Emit(record, profile.Name, "FilesystemViolation", $"Access to denied path: {path}");
                return;
            }
        }
    }

    private void Emit(ProcessRecord record, string profileName, string violationType, string detail)
    {
        _bus.Publish(new PermissionViolationEvent(
            DateTimeOffset.UtcNow,
            "Foreman.Profiles",
            $"[{profileName}] {violationType}: {detail}",
            record.Pid,
            profileName,
            violationType,
            detail
        ));
    }

    private static string ExpandVars(string path) =>
        Environment.ExpandEnvironmentVariables(path.Replace("%USERNAME%", Environment.UserName));

    private static bool PathMatchesGlob(string path, string pattern)
    {
        // simple prefix match with trailing wildcard
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string s, int max = 80) =>
        s.Length <= max ? s : s[..max] + "…";
}
