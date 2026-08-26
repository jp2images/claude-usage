using ClaudeUsage.Models;

namespace ClaudeUsage.Services;

/// A model's USD rates per million tokens. Cache traffic is billed as a
/// multiple of the input rate rather than at its own posted rate: a cache write
/// costs 1.25x input on the 5-minute TTL and 2x input on the 1-hour TTL, and a
/// cache read costs 0.1x input.
public readonly record struct ModelPricing(double Input, double Output)
{
    public double CacheWrite5m => Input * 1.25;
    public double CacheWrite1h => Input * 2.0;
    public double CacheRead => Input * 0.1;
}

/// Local pricing table. Counterpart to pricing.go.
public static class Pricing
{
    /// The Opus versions that predate the 4.5 price drop and still bill at
    /// $15/$75. Matching is by ID substring, so these are listed explicitly:
    /// "opus-4-1" would not catch Opus 4.0, whose ID is
    /// claude-opus-4-20250514, and a looser "opus-4" pattern would wrongly
    /// catch Opus 4.5 and later.
    private static readonly string[] LegacyOpusIds =
    {
        "claude-3-opus",
        "claude-opus-4-0",
        "claude-opus-4-20",
        "claude-opus-4-1",
    };

    /// Returns a model's rates and whether we know them. A model routed through
    /// a non-Anthropic provider has no Claude rate; callers show its cost as
    /// unknown rather than pricing it as though it were Claude.
    public static bool TryGetPricing(string modelId, out ModelPricing pricing)
    {
        var id = modelId.ToLowerInvariant();

        ModelPricing? rate = true switch
        {
            _ when LegacyOpusIds.Any(id.Contains) => new ModelPricing(15, 75),
            _ when id.Contains("fable") || id.Contains("mythos") => new ModelPricing(10, 50),
            _ when id.Contains("opus") => new ModelPricing(5, 25),
            // Sonnet dropped from $3/$15 to $2/$10 with Sonnet 5.
            _ when id.Contains("sonnet-5") => new ModelPricing(2, 10),
            _ when id.Contains("sonnet") => new ModelPricing(3, 15),
            _ when id.Contains("haiku") => new ModelPricing(1, 5),
            _ => null,
        };

        pricing = rate ?? default;
        return rate.HasValue;
    }

    /// Prices one model's token counts. Returns false when the model has no
    /// known rate; the cost is then 0 and the caller shows a dash instead of a
    /// number that would look authoritative.
    public static bool TryGetCost(string modelId, ModelUsage usage, out double costUsd)
    {
        if (!TryGetPricing(modelId, out var p))
        {
            costUsd = 0;
            return false;
        }

        const double perMillion = 1_000_000.0;
        costUsd = (usage.InputTokens * p.Input +
                   usage.OutputTokens * p.Output +
                   usage.CacheCreation5mTokens * p.CacheWrite5m +
                   usage.CacheCreation1hTokens * p.CacheWrite1h +
                   usage.CacheReadInputTokens * p.CacheRead) / perMillion;
        return true;
    }
}
