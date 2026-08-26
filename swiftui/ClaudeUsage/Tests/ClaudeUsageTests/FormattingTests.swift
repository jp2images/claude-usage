import Testing
@testable import ClaudeUsage

@Suite("Cost formatting")
struct FormattingTests {
    @Test(
        "Always two decimals, so a column of costs scans cleanly",
        arguments: [
            (0.0, "$0.00"),
            (0.1511, "$0.15"),
            (35.49, "$35.49"),
            (431.26, "$431.26"),
            (1108.85, "$1,108.85"),
            // A sub-cent amount rounds to zero rather than growing a third decimal.
            (0.0004, "$0.00"),
            // Rounding has to carry into the whole part; rounding the whole and
            // the fraction separately would render this as "$0.100".
            (0.999, "$1.00"),
            (9.999, "$10.00"),
        ] as [(Double, String)]
    )
    func costPrecision(usd: Double, expected: String) {
        #expect(Formatting.cost(usd) == expected)
    }

    @Test("A dash means no known rate, which $0.00 does not")
    func unknownRateShowsDash() {
        #expect(Formatting.cost(nil) == "—")
        #expect(Formatting.cost(0.0004) == "$0.00")
    }
}
