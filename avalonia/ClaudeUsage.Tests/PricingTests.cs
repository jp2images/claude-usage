using ClaudeUsage.Models;
using ClaudeUsage.Services;
using Xunit;

namespace ClaudeUsage.Tests;

public class PricingTests
{
    [Theory]
    [InlineData("claude-opus-5", 5d, 25d)]
    [InlineData("claude-opus-4-8", 5d, 25d)]
    [InlineData("claude-opus-4-7", 5d, 25d)]
    [InlineData("claude-opus-4-6", 5d, 25d)]
    [InlineData("claude-opus-4-5", 5d, 25d)]
    [InlineData("claude-fable-5", 10d, 50d)]
    [InlineData("claude-mythos-5", 10d, 50d)]
    [InlineData("claude-haiku-4-5-20251001", 1d, 5d)]
    // Sonnet 5 is cheaper than Sonnet 4.6 — pricing it at the older family rate
    // overstates its cost by half.
    [InlineData("claude-sonnet-5", 2d, 10d)]
    [InlineData("claude-sonnet-4-6", 3d, 15d)]
    [InlineData("claude-sonnet-4-5-20250929", 3d, 15d)]
    // Opus billed $15/$75 before the 4.5 price drop.
    [InlineData("claude-3-opus-20240229", 15d, 75d)]
    [InlineData("claude-opus-4-20250514", 15d, 75d)]
    [InlineData("claude-opus-4-1-20250805", 15d, 75d)]
    public void KnownModelsHaveTheirPostedRates(string modelId, double input, double output)
    {
        Assert.True(Pricing.TryGetPricing(modelId, out var p), $"no rate found for {modelId}");
        Assert.Equal(input, p.Input);
        Assert.Equal(output, p.Output);
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("llama-3.1-70b")]
    [InlineData("")]
    public void UnknownModelHasNoRate(string modelId)
    {
        // A model routed through a non-Anthropic provider must not be priced as
        // though it were Claude.
        Assert.False(Pricing.TryGetPricing(modelId, out _));
    }

    [Fact]
    public void CacheRatesAreMultiplesOfInput()
    {
        Assert.True(Pricing.TryGetPricing("claude-opus-5", out var p));
        Assert.Equal(6.25, p.CacheWrite5m);
        Assert.Equal(10.0, p.CacheWrite1h);
        Assert.Equal(0.5, p.CacheRead);
    }

    [Fact]
    public void CostSumsEveryTokenKindAtItsOwnRate()
    {
        // One million of each token type on Opus 5, so each term reads as its
        // own rate: 5 + 25 + 6.25 + 10 + 0.5.
        var usage = new ModelUsage
        {
            InputTokens = 1_000_000,
            OutputTokens = 1_000_000,
            CacheCreation5mTokens = 1_000_000,
            CacheCreation1hTokens = 1_000_000,
            CacheReadInputTokens = 1_000_000,
        };

        Assert.True(Pricing.TryGetCost("claude-opus-5", usage, out var cost));
        Assert.Equal(46.75, cost, 9);
    }

    [Fact]
    public void CacheWriteTtlsArePricedApart()
    {
        // The same cache-write volume costs more on the 1-hour TTL. Collapsing
        // the two into one 1.25x bucket, as a single cache_creation rate would,
        // understates the 1-hour case.
        Pricing.TryGetCost("claude-opus-5", new ModelUsage { CacheCreation5mTokens = 1_000_000 }, out var cost5m);
        Pricing.TryGetCost("claude-opus-5", new ModelUsage { CacheCreation1hTokens = 1_000_000 }, out var cost1h);

        Assert.Equal(6.25, cost5m);
        Assert.Equal(10.0, cost1h);
    }

    [Fact]
    public void UnknownModelCostsNothingAndIsFlagged()
    {
        var usage = new ModelUsage { InputTokens = 1_000_000, OutputTokens = 1_000_000 };

        Assert.False(Pricing.TryGetCost("some-other-provider/model", usage, out var cost));
        Assert.Equal(0d, cost);
    }
}
