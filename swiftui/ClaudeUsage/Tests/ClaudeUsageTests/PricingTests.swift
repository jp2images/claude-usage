import Foundation
import Testing

@testable import ClaudeUsage

@Suite("Pricing")
struct PricingTests {

    @Test("Known models bill at their posted rates", arguments: [
        ("claude-opus-5", 5.0, 25.0),
        ("claude-opus-4-8", 5.0, 25.0),
        ("claude-opus-4-7", 5.0, 25.0),
        ("claude-opus-4-6", 5.0, 25.0),
        ("claude-opus-4-5", 5.0, 25.0),
        ("claude-fable-5", 10.0, 50.0),
        ("claude-mythos-5", 10.0, 50.0),
        ("claude-haiku-4-5-20251001", 1.0, 5.0),

        // Sonnet 5 is cheaper than Sonnet 4.6 — pricing it at the older family
        // rate overstates its cost by half.
        ("claude-sonnet-5", 2.0, 10.0),
        ("claude-sonnet-4-6", 3.0, 15.0),
        ("claude-sonnet-4-5-20250929", 3.0, 15.0),

        // Opus billed $15/$75 before the 4.5 price drop.
        ("claude-3-opus-20240229", 15.0, 75.0),
        ("claude-opus-4-20250514", 15.0, 75.0),
        ("claude-opus-4-1-20250805", 15.0, 75.0),
    ])
    func ratesForKnownModels(modelID: String, input: Double, output: Double) throws {
        let rates = try #require(Pricing.rates(for: modelID), "no rate found for \(modelID)")
        #expect(rates == ModelPricing(input: input, output: output))
    }

    @Test("A model routed elsewhere has no Claude rate", arguments: ["gpt-4o", "llama-3.1-70b", ""])
    func ratesForUnknownModel(modelID: String) {
        #expect(Pricing.rates(for: modelID) == nil)
    }

    @Test("Cache traffic bills as a multiple of the input rate")
    func cacheMultipliers() throws {
        let rates = try #require(Pricing.rates(for: "claude-opus-5"))
        #expect(rates.cacheWrite5m == 6.25)
        #expect(rates.cacheWrite1h == 10)
        #expect(rates.cacheRead == 0.5)
    }

    @Test("Each token type is priced at its own rate")
    func cost() throws {
        // One million of each token type on Opus 5, so each term reads as its
        // own rate: 5 + 25 + 6.25 + 10 + 0.5.
        let usage = ModelUsage(
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheReadInputTokens: 1_000_000,
            cacheCreation5mTokens: 1_000_000,
            cacheCreation1hTokens: 1_000_000
        )

        let cost = try #require(Pricing.cost(for: "claude-opus-5", usage: usage))
        #expect(abs(cost - 46.75) < 1e-9)
    }

    @Test("The two cache TTLs bill differently")
    func costSeparatesCacheTTLs() throws {
        // Collapsing the two into one 1.25x bucket, as a single cache_creation
        // rate would, understates the 1-hour case.
        let write5m = ModelUsage(cacheCreation5mTokens: 1_000_000)
        let write1h = ModelUsage(cacheCreation1hTokens: 1_000_000)

        #expect(Pricing.cost(for: "claude-opus-5", usage: write5m) == 6.25)
        #expect(Pricing.cost(for: "claude-opus-5", usage: write1h) == 10)
    }

    @Test("An unpriced model has no cost rather than a zero one")
    func costOfUnknownModelIsUnknown() {
        let usage = ModelUsage(inputTokens: 1_000_000, outputTokens: 1_000_000)
        #expect(Pricing.cost(for: "some-other-provider/model", usage: usage) == nil)
    }

    // Cost rendering lives in the "Cost formatting" suite.

    @Test("A specific model version beats its family in the name table")
    func friendlyModelNames() {
        #expect(Formatting.friendlyModel("claude-sonnet-5") == "Sonnet 5")
        #expect(Formatting.friendlyModel("claude-opus-4-8") == "Opus 4.8")
        #expect(Formatting.friendlyModel("claude-opus-4-20250514") == "Opus 4")
        #expect(Formatting.friendlyModel("claude-fable-5") == "Fable 5")
        #expect(Formatting.friendlyModel("gpt-4o") == "gpt-4o")
    }
}
