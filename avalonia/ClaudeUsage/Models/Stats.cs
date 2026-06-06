using System.Text.Json.Serialization;

namespace ClaudeUsage.Models;

public sealed class DailyActivity
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }
    [JsonPropertyName("sessionCount")] public int SessionCount { get; set; }
    [JsonPropertyName("toolCallCount")] public int ToolCallCount { get; set; }
}

public sealed class ModelUsage
{
    [JsonPropertyName("inputTokens")] public long InputTokens { get; set; }
    [JsonPropertyName("outputTokens")] public long OutputTokens { get; set; }
    [JsonPropertyName("cacheReadInputTokens")] public long CacheReadInputTokens { get; set; }
    [JsonPropertyName("cacheCreationInputTokens")] public long CacheCreationInputTokens { get; set; }
    [JsonPropertyName("webSearchRequests")] public long WebSearchRequests { get; set; }
    [JsonPropertyName("costUSD")] public double CostUSD { get; set; }
}

public sealed class LongestSession
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("duration")] public long Duration { get; set; }
    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
}

/// Mirror of ~/.claude/stats-cache.json (only the fields the UI uses).
public sealed class StatsCache
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("lastComputedDate")] public string LastComputedDate { get; set; } = "";
    [JsonPropertyName("dailyActivity")] public List<DailyActivity> DailyActivity { get; set; } = new();
    [JsonPropertyName("modelUsage")] public Dictionary<string, ModelUsage> ModelUsage { get; set; } = new();
    [JsonPropertyName("totalSessions")] public int TotalSessions { get; set; }
    [JsonPropertyName("totalMessages")] public int TotalMessages { get; set; }
    [JsonPropertyName("longestSession")] public LongestSession LongestSession { get; set; } = new();
    [JsonPropertyName("firstSessionDate")] public string FirstSessionDate { get; set; } = "";
}
