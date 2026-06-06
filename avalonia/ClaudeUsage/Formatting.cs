using System.Globalization;
using Avalonia.Media;
using ClaudeUsage.Models;

namespace ClaudeUsage;

/// Display-formatting helpers, ported from main.go / stats.go / status.go.
public static class Formatting
{
    public static string FriendlyTier(string tier) => tier switch
    {
        "default_claude_max_5x" => "Max (5x)",
        "default_claude_max_20x" => "Max (20x)",
        "default_claude_pro" => "Pro",
        _ => tier,
    };

    public static string FriendlyModel(string modelId)
    {
        var id = modelId.ToLowerInvariant();
        return true switch
        {
            _ when id.Contains("opus-4-6") => "Opus 4.6",
            _ when id.Contains("sonnet-4-6") => "Sonnet 4.6",
            _ when id.Contains("haiku-4-5") => "Haiku 4.5",
            _ when id.Contains("opus-4") => "Opus 4",
            _ when id.Contains("sonnet-4-5") => "Sonnet 4.5",
            _ when id.Contains("haiku-4") => "Haiku 4",
            _ when id.Contains("opus-3-7") => "Opus 3.7",
            _ when id.Contains("sonnet-3-7") => "Sonnet 3.7",
            _ when id.Contains("sonnet-3-5") => "Sonnet 3.5",
            _ when id.Contains("haiku-3-5") => "Haiku 3.5",
            _ when id.Contains("opus-3") => "Opus 3",
            _ when id.Contains("sonnet-3") => "Sonnet 3",
            _ when id.Contains("haiku-3") => "Haiku 3",
            _ => modelId,
        };
    }

    /// Compact human-readable token count (1.2K / 3.4M / 5.6B).
    public static string Tokens(long n) => n switch
    {
        >= 1_000_000_000 => $"{n / 1_000_000_000.0:F1}B",
        >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
        >= 1_000 => $"{n / 1_000.0:F1}K",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    public static string Number(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    public static string Date(string dateStr) =>
        DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t)
            ? t.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : dateStr;

    public static string LongDate(string isoDate) =>
        DateTimeOffset.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : "—";

    public static string Duration(long ms)
    {
        var d = TimeSpan.FromMilliseconds(ms);
        var h = (int)d.TotalHours;
        return h > 0 ? $"{h}h {d.Minutes}m" : $"{d.Minutes}m";
    }

    public static string TimeUntil(string? resetsAt)
    {
        if (!TryParseReset(resetsAt, out var t)) return "";
        var d = t - DateTimeOffset.Now;
        if (d <= TimeSpan.Zero) return "Resetting soon";
        var h = (int)d.TotalHours;
        return h > 0 ? $"Resets in {h}h {d.Minutes}m" : $"Resets in {d.Minutes}m";
    }

    public static string ResetDay(string? resetsAt) =>
        TryParseReset(resetsAt, out var t) ? t.LocalDateTime.ToString("'Resets 'ddd h:mm tt") : "";

    private static bool TryParseReset(string? s, out DateTimeOffset t)
    {
        t = default;
        return !string.IsNullOrEmpty(s) &&
               DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t);
    }

    public static ModelUsage TotalTokens(IReadOnlyDictionary<string, ModelUsage> usage)
    {
        var total = new ModelUsage();
        foreach (var u in usage.Values)
        {
            total.InputTokens += u.InputTokens;
            total.OutputTokens += u.OutputTokens;
            total.CacheReadInputTokens += u.CacheReadInputTokens;
            total.CacheCreationInputTokens += u.CacheCreationInputTokens;
            total.WebSearchRequests += u.WebSearchRequests;
            total.CostUSD += u.CostUSD;
        }
        return total;
    }

    /// Maps a Statuspage indicator to a traffic-light brush (statusColor in status.go).
    public static IBrush StatusBrush(string indicator) => indicator switch
    {
        "none" => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
        "minor" => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
        "major" => new SolidColorBrush(Color.FromRgb(253, 126, 20)),
        "critical" => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
        _ => new SolidColorBrush(Color.FromRgb(108, 117, 125)),
    };
}
