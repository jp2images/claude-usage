using System.Text.Json.Serialization;

namespace ClaudeUsage.Models;

/// A single usage metric: utilization percentage and (optional) reset time.
public sealed class UsagePeriod
{
    [JsonPropertyName("utilization")] public double Utilization { get; set; }
    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
}

/// The optional purchased extra-credit balance.
public sealed class ExtraUsage
{
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("monthly_limit")] public double? MonthlyLimit { get; set; }
    [JsonPropertyName("used_credits")] public double? UsedCredits { get; set; }
    [JsonPropertyName("utilization")] public double? Utilization { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
}

/// Response from /api/organizations/{id}/usage.
public sealed class PlanUsage
{
    [JsonPropertyName("five_hour")] public UsagePeriod? FiveHour { get; set; }
    [JsonPropertyName("seven_day")] public UsagePeriod? SevenDay { get; set; }
    [JsonPropertyName("seven_day_oauth_apps")] public UsagePeriod? SevenDayOAuthApps { get; set; }
    [JsonPropertyName("seven_day_opus")] public UsagePeriod? SevenDayOpus { get; set; }
    [JsonPropertyName("seven_day_sonnet")] public UsagePeriod? SevenDaySonnet { get; set; }
    [JsonPropertyName("seven_day_cowork")] public UsagePeriod? SevenDayCowork { get; set; }
    [JsonPropertyName("seven_day_omelette")] public UsagePeriod? SevenDayOmelette { get; set; } // Claude Design
    [JsonPropertyName("extra_usage")] public ExtraUsage ExtraUsage { get; set; } = new();
}

/// Response from /api/organizations/{id}/rate_limits.
public sealed class RateLimits
{
    [JsonPropertyName("rate_limit_tier")] public string RateLimitTier { get; set; } = "";
}
