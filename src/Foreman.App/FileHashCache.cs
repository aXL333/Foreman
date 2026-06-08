using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace Foreman.App;

/// <summary>
/// SHA-256 hashes of executable files, computed once per path on a background thread and cached.
///
/// The Process Monitor refreshes every 2 seconds; it must never hash on the UI thread and must not
/// re-hash a path it already knows. GetOrCompute returns the cached hash, or null while it kicks off
/// a one-shot background computation — so a hash fills into the column within a refresh tick or two.
/// </summary>
public static class FileHashCache
{
    // path -> hash; "" means "tried and unreadable" so we don't retry it forever.
    private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached uppercase-hex SHA-256, or null if not computed yet (computation is started).</summary>
    public static string? GetOrCompute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (_cache.TryGetValue(path, out var hash))
            return hash.Length == 0 ? null : hash;   // "" => unreadable, treat as no hash

        if (_inflight.TryAdd(path, 0))
            _ = Task.Run(() => Compute(path));
        return null;
    }

    private static void Compute(string path)
    {
        var result = "";
        try
        {
            using var fs = File.OpenRead(path);
            result = Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { /* gone / access denied / locked — cache empty so we stop retrying */ }
        finally
        {
            _cache[path] = result;
            _inflight.TryRemove(path, out _);
        }
    }
}
