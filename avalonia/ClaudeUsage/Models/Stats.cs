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

    // Cache writes are billed per TTL — 1.25x input at 5 minutes, 2x at one
    // hour — so the two are tracked apart. Their sum is CacheCreationInputTokens.
    [JsonPropertyName("cacheCreation5mTokens")] public long CacheCreation5mTokens { get; set; }
    [JsonPropertyName("cacheCreation1hTokens")] public long CacheCreation1hTokens { get; set; }

    [JsonPropertyName("webSearchRequests")] public long WebSearchRequests { get; set; }
    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }

    /// Computed from the token counts and this app's pricing table, not
    /// reported by Claude. CostKnown is false for a model with no known rate,
    /// such as one routed through a non-Anthropic provider.
    [JsonPropertyName("costUsd")] public double CostUSD { get; set; }
    [JsonPropertyName("costKnown")] public bool CostKnown { get; set; }
}

/// One session's extent. ActiveMillis counts only the time between messages
/// that arrived close together, so a session left open overnight doesn't report
/// the idle hours as work.
public sealed class SessionSummary
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("activeMillis")] public long ActiveMillis { get; set; }
    [JsonPropertyName("messageCount")] public int MessageCount { get; set; }
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
}

/// The aggregate the history window renders, built by walking Claude Code's
/// JSONL transcripts. Mirror of UsageStats in stats.go.
public sealed class UsageStats
{
    [JsonPropertyName("dailyActivity")] public List<DailyActivity> DailyActivity { get; set; } = new();
    [JsonPropertyName("modelUsage")] public Dictionary<string, ModelUsage> ModelUsage { get; set; } = new();
    [JsonPropertyName("totalSessions")] public int TotalSessions { get; set; }
    [JsonPropertyName("totalMessages")] public int TotalMessages { get; set; }
    [JsonPropertyName("busiestSession")] public SessionSummary BusiestSession { get; set; } = new();
    [JsonPropertyName("firstSessionDate")] public string FirstSessionDate { get; set; } = "";
    [JsonPropertyName("lastActivityDate")] public string LastActivityDate { get; set; } = "";
}
