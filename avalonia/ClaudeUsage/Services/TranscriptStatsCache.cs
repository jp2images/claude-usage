using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsage.Models;

namespace ClaudeUsage.Services;

/// Caches the transcript aggregate on disk. Re-reading every transcript on each
/// refresh is wasteful when nothing has changed, so the aggregate is stored
/// against a fingerprint of the file set; any write to any transcript changes
/// the fingerprint and forces a re-read.
///
/// Every failure here is swallowed: a bad read or write only costs a full
/// re-read, which is what the caller would have done anyway.
internal static class TranscriptStatsCache
{
    /// Folded into the fingerprint so a cache written by an older build is
    /// discarded rather than decoded into the current UsageStats, where removed
    /// fields would silently read as zero. Bump this whenever UsageStats or
    /// ModelUsage changes shape.
    private const int SchemaVersion = 1;

    private sealed class CachedStats
    {
        [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
        [JsonPropertyName("stats")] public UsageStats? Stats { get; set; }
    }

    /// Hashes the schema version and each file's path, size, and modification
    /// time. Returns null when a file can't be stat'ed, which disables caching
    /// for this pass rather than caching a fingerprint that doesn't describe
    /// what was read.
    internal static string? Fingerprint(IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder().Append("schema:").Append(SchemaVersion).Append('\n');
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            sb.Append(path).Append(':')
              .Append(info.Length).Append(':')
              .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static UsageStats? Read(string fingerprint)
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return null;

            var cached = JsonSerializer.Deserialize<CachedStats>(File.ReadAllText(path));
            return cached?.Fingerprint == fingerprint ? cached.Stats : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static void Write(string fingerprint, UsageStats stats)
    {
        try
        {
            var path = CachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(new CachedStats { Fingerprint = fingerprint, Stats = stats }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A failed cache write only costs a re-read next time.
        }
    }

    /// The filename carries the implementation. All three ports write the same
    /// kind of aggregate under the same app directory, and their field names
    /// differ only in case (costUsd against Swift's costUSD), so a shared
    /// filename would let one decode another's JSON and silently read every
    /// cost as absent. LocalApplicationData happens to resolve elsewhere than
    /// the Go and Swift cache directory on macOS, but that is the runtime's
    /// choice, not a guarantee to build on.
    private static string CachePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeUsage", "transcript-stats-dotnet.json");
}
