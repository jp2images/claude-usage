using System.Text.Json.Serialization;

namespace ClaudeUsage.Models;

/// A single usage metric: utilization percentage and (optional) reset time.
public sealed class UsagePeriod
{
    [JsonPropertyName("utilization")] public double Utilization { get; set; }
    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
}

/// The optional purchased extra-credit balance.
/// The API reports MonthlyLimit and UsedCredits in cents, not dollars.
public sealed class ExtraUsage
{
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("monthly_limit")] public double? MonthlyLimit { get; set; }
    [JsonPropertyName("used_credits")] public double? UsedCredits { get; set; }
    [JsonPropertyName("utilization")] public double? Utilization { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }

    /// Credits spent, converted from cents to whole currency units.
    [JsonIgnore] public double? UsedAmount => UsedCredits / 100.0;
}

/// Names the model a scoped limit applies to.
public sealed class LimitModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
}

/// Narrows a limit to one model or surface.
public sealed class LimitScope
{
    [JsonPropertyName("model")] public LimitModel? Model { get; set; }
}

/// One entry in the API's limits array, which superseded the per-model
/// seven_day_* fields — those now come back null. New models appear here
/// without a schema change, which is how Fable shows up.
public sealed class Limit
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";   // session | weekly_all | weekly_scoped
    [JsonPropertyName("group")] public string Group { get; set; } = ""; // session | weekly
    [JsonPropertyName("percent")] public double Percent { get; set; }
    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
    [JsonPropertyName("scope")] public LimitScope? Scope { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }

    /// The row heading for this limit.
    [JsonIgnore]
    public string Label => Kind switch
    {
        "session" => "Current Session",
        "weekly_all" => "All Models",
        _ => string.IsNullOrEmpty(Scope?.Model?.DisplayName) ? "Weekly" : Scope!.Model!.DisplayName,
    };
}

/// Response from /api/organizations/{id}/usage.
public sealed class PlanUsage
{
    [JsonPropertyName("limits")] public List<Limit> Limits { get; set; } = [];

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
