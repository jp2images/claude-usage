import Foundation

/// Display-formatting helpers, ported from main.go / stats.go.
enum Formatting {

    static func friendlyTier(_ tier: String) -> String {
        switch tier {
        case "default_claude_max_5x": return "Max (5x)"
        case "default_claude_max_20x": return "Max (20x)"
        case "default_claude_pro": return "Pro"
        default: return tier
        }
    }

    static func friendlyModel(_ modelID: String) -> String {
        let id = modelID.lowercased()
        let table: [(String, String)] = [
            ("opus-4-6", "Opus 4.6"), ("sonnet-4-6", "Sonnet 4.6"), ("haiku-4-5", "Haiku 4.5"),
            ("opus-4", "Opus 4"), ("sonnet-4-5", "Sonnet 4.5"), ("haiku-4", "Haiku 4"),
            ("opus-3-7", "Opus 3.7"), ("sonnet-3-7", "Sonnet 3.7"), ("sonnet-3-5", "Sonnet 3.5"),
            ("haiku-3-5", "Haiku 3.5"), ("opus-3", "Opus 3"), ("sonnet-3", "Sonnet 3"),
            ("haiku-3", "Haiku 3"),
        ]
        for (needle, name) in table where id.contains(needle) { return name }
        return modelID
    }

    /// Compact human-readable token count (1.2K / 3.4M / 5.6B).
    static func tokens(_ n: Int) -> String {
        switch n {
        case 1_000_000_000...: return String(format: "%.1fB", Double(n) / 1_000_000_000)
        case 1_000_000...: return String(format: "%.1fM", Double(n) / 1_000_000)
        case 1_000...: return String(format: "%.1fK", Double(n) / 1_000)
        default: return "\(n)"
        }
    }

    static func number(_ n: Int) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        return f.string(from: NSNumber(value: n)) ?? "\(n)"
    }

    /// "yyyy-MM-dd" -> "Jan 2, 2026" (falls back to the raw string).
    static func date(_ dateStr: String) -> String {
        let input = DateFormatter()
        input.dateFormat = "yyyy-MM-dd"
        input.locale = Locale(identifier: "en_US_POSIX")
        guard let d = input.date(from: dateStr) else { return dateStr }
        let output = DateFormatter()
        output.dateFormat = "MMM d, yyyy"
        output.locale = Locale(identifier: "en_US_POSIX")
        return output.string(from: d)
    }

    static func longDate(_ iso: String) -> String {
        guard let d = isoDate(iso) else { return "—" }
        let output = DateFormatter()
        output.dateFormat = "MMM d, yyyy"
        output.locale = Locale(identifier: "en_US_POSIX")
        return output.string(from: d)
    }

    /// Millisecond duration -> "Xh Ym" or "Zm".
    static func duration(_ ms: Int) -> String {
        let totalMinutes = ms / 60_000
        let h = totalMinutes / 60
        let m = totalMinutes % 60
        return h > 0 ? "\(h)h \(m)m" : "\(m)m"
    }

    static func timeUntil(_ resetsAt: String?) -> String {
        guard let resetsAt, let target = isoDate(resetsAt) else { return "" }
        let interval = target.timeIntervalSinceNow
        if interval <= 0 { return "Resetting soon" }
        let minutes = Int(interval) / 60
        let h = minutes / 60
        let m = minutes % 60
        return h > 0 ? "Resets in \(h)h \(m)m" : "Resets in \(m)m"
    }

    static func resetDay(_ resetsAt: String?) -> String {
        guard let resetsAt, let target = isoDate(resetsAt) else { return "" }
        let f = DateFormatter()
        f.dateFormat = "'Resets' EEE h:mm a"
        return f.string(from: target)
    }

    static func totalTokens(_ usage: [String: ModelUsage]) -> ModelUsage {
        var total = ModelUsage()
        for u in usage.values {
            total.inputTokens += u.inputTokens
            total.outputTokens += u.outputTokens
            total.cacheReadInputTokens += u.cacheReadInputTokens
            total.cacheCreationInputTokens += u.cacheCreationInputTokens
            total.webSearchRequests += u.webSearchRequests
            total.costUSD += u.costUSD
        }
        return total
    }

    private static func isoDate(_ s: String) -> Date? {
        let withFractional = ISO8601DateFormatter()
        withFractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = withFractional.date(from: s) { return d }
        let plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
        return plain.date(from: s)
    }
}
