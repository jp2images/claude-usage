using System.IO;
using System.Text.Json;
using ClaudeUsage.Models;

namespace ClaudeUsage.Services;

/// Loads ~/.claude/stats-cache.json (Claude Code's local usage stats).
/// Counterpart to stats.go. The only platform difference from macOS is the
/// home-directory lookup, which Environment.SpecialFolder handles.
public static class StatsRepository
{
    public static StatsCache Load()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, ".claude", "stats-cache.json");

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Stats file not found at {path}\n\n" +
                "Make sure Claude Code is installed and has been used at least once.");

        var stats = JsonSerializer.Deserialize<StatsCache>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("cannot parse stats file");

        // Newest first.
        stats.DailyActivity.Sort((a, b) => string.CompareOrdinal(b.Date, a.Date));
        return stats;
    }
}
