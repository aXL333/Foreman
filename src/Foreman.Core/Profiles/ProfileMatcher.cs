using Foreman.Core.Models;

namespace Foreman.Core.Profiles;

/// <summary>
/// Matches a live ProcessRecord to the best-fitting HarnessProfile.
/// Returns null if no profile matches (means use default / monitor-only behavior).
/// </summary>
public sealed class ProfileMatcher
{
    private readonly ProfileStore _store;

    public ProfileMatcher(ProfileStore store)
    {
        _store = store;
    }

    public HarnessProfile? Match(ProcessRecord record)
    {
        foreach (var profile in _store.All)
        {
            if (Matches(record, profile.ProcessMatch))
                return profile;
        }
        return null;
    }

    private static bool Matches(ProcessRecord record, ProcessMatchConfig cfg)
    {
        var name = record.Name.ToLowerInvariant();
        var cmd = record.CommandLine.ToLowerInvariant();

        foreach (var exe in cfg.ExecutableNames)
        {
            if (name == exe.ToLowerInvariant()) return true;
        }

        foreach (var marker in cfg.CommandLineContains)
        {
            if (cmd.Contains(marker.ToLowerInvariant())) return true;
        }

        return false;
    }
}
