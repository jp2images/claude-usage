using ClaudeUsage;
using ClaudeUsage.Models;
using Xunit;

public class FormattingTests
{
    [Theory]
    // One precision throughout, so a column of costs scans cleanly.
    [InlineData(0, "$0.00")]
    [InlineData(0.1511, "$0.15")]
    [InlineData(35.49, "$35.49")]
    [InlineData(431.26, "$431.26")]
    [InlineData(1108.85, "$1,108.85")]
    // A sub-cent amount rounds to zero rather than growing a third decimal.
    [InlineData(0.0004, "$0.00")]
    // Rounding has to carry into the whole part; splitting the whole and the
    // fraction and rounding each would render this as "$0.100".
    [InlineData(0.999, "$1.00")]
    [InlineData(9.999, "$10.00")]
    public void CostAlwaysUsesTwoDecimals(double usd, string expected)
    {
        Assert.Equal(expected, Formatting.Cost(usd));
    }

    [Fact]
    public void CostLabelShowsDashWhenRateIsUnknown()
    {
        // A dash means "no known rate", which is a different statement from
        // $0.00 — the latter is a real, rounded amount.
        var unpriced = new ModelUsage { CostUSD = 0, CostKnown = false };
        Assert.Equal("—", Formatting.CostLabel(unpriced));

        var priced = new ModelUsage { CostUSD = 0.0004, CostKnown = true };
        Assert.Equal("$0.00", Formatting.CostLabel(priced));
    }
}
