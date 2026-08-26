import Foundation

/// A model's USD rates per million tokens. Counterpart to pricing.go.
///
/// Cache traffic is billed as a multiple of the input rate rather than at its
/// own posted rate: a cache write costs 1.25x input on the 5-minute TTL and 2x
/// input on the 1-hour TTL, and a cache read costs 0.1x input.
struct ModelPricing: Sendable, Equatable {
    var input: Double
    var output: Double

    var cacheWrite5m: Double { input * 1.25 }
    var cacheWrite1h: Double { input * 2 }
    var cacheRead: Double { input * 0.1 }
}

enum Pricing {

    /// The Opus versions that predate the 4.5 price drop and still bill at
    /// $15/$75. Matching is by ID substring, so these are listed explicitly:
    /// "opus-4-1" would not catch Opus 4.0, whose ID is
    /// claude-opus-4-20250514, and a looser "opus-4" pattern would wrongly
    /// catch Opus 4.5 and later.
    private static let legacyOpusIDs = [
        "claude-3-opus",
        "claude-opus-4-0",
        "claude-opus-4-20",
        "claude-opus-4-1",
    ]

    /// A model's rates, or nil when we have none. A model routed through a
    /// non-Anthropic provider has no Claude rate; callers show its cost as
    /// unknown rather than pricing it as though it were Claude.
    static func rates(for modelID: String) -> ModelPricing? {
        let id = modelID.lowercased()

        if legacyOpusIDs.contains(where: { id.contains($0) }) {
            return ModelPricing(input: 15, output: 75)
        }
        if id.contains("fable") || id.contains("mythos") {
            return ModelPricing(input: 10, output: 50)
        }
        if id.contains("opus") {
            return ModelPricing(input: 5, output: 25)
        }
        // Sonnet dropped from $3/$15 to $2/$10 with Sonnet 5.
        if id.contains("sonnet-5") {
            return ModelPricing(input: 2, output: 10)
        }
        if id.contains("sonnet") {
            return ModelPricing(input: 3, output: 15)
        }
        if id.contains("haiku") {
            return ModelPricing(input: 1, output: 5)
        }
        return nil
    }

    /// Prices one model's token counts. nil means the model has no known rate,
    /// and the caller shows a dash instead of a number that would look
    /// authoritative.
    static func cost(for modelID: String, usage: ModelUsage) -> Double? {
        guard let rates = rates(for: modelID) else { return nil }

        let total = Double(usage.inputTokens) * rates.input
            + Double(usage.outputTokens) * rates.output
            + Double(usage.cacheCreation5mTokens) * rates.cacheWrite5m
            + Double(usage.cacheCreation1hTokens) * rates.cacheWrite1h
            + Double(usage.cacheReadInputTokens) * rates.cacheRead

        return total / 1_000_000
    }
}
