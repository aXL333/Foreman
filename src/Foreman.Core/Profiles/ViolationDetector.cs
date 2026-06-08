using Foreman.Core.Events;
using Foreman.Core.Heuristics;
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
    private readonly Func<int, ProcessRecord?>? _findProfileAncestor;

    public ViolationDetector(
        ProfileMatcher matcher,
        EventBus bus,
        Func<int, ProcessRecord?>? findProfileAncestor = null)
    {
        _matcher = matcher;
        _bus = bus;
        _findProfileAncestor = findProfileAncestor;
    }

    /// <summary>
    /// Called by the WMI watcher after a command is analysed.
    /// If the command matches a blocked pattern in the harness profile, emit a violation.
    /// </summary>
    public void CheckCommandLine(ProcessRecord record, RuleMatch? match)
    {
        if (match is null) return;

        var profile = ResolveProfile(record);
        if (profile is null) return;
        if (string.Equals(profile.Commands.EnforceMode, "monitor", StringComparison.OrdinalIgnoreCase)) return;

        if (profile.Commands.BlockedPatterns.Contains(match.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            Emit(record, profile.Name, "CommandBlocked",
                $"Blocked rule [{match.RuleId}] {match.RuleName}: {match.Description}");
        }
    }

    /// <summary>
    /// Checks a file path against the profile's denied paths.
    /// </summary>
    public void CheckFilePath(ProcessRecord record, string path)
    {
        var profile = ResolveProfile(record);
        if (profile is null) return;
        if (string.Equals(profile.FileSystem.EnforceMode, "monitor", StringComparison.OrdinalIgnoreCase)) return;

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
        ) { ProcessStartTime = record.StartTime });
    }

    private HarnessProfile? ResolveProfile(ProcessRecord record)
    {
        if (record.ProfileName is not null && _matcher.Get(record.ProfileName) is { } byName)
            return byName;

        if (_matcher.Match(record) is { } direct)
        {
            record.ProfileName = direct.Name;
            return direct;
        }

        var ancestor = _findProfileAncestor?.Invoke(record.Pid);
        if (ancestor?.ProfileName is not null && _matcher.Get(ancestor.ProfileName) is { } inherited)
        {
            record.ProfileName = inherited.Name;
            return inherited;
        }

        return null;
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

}
