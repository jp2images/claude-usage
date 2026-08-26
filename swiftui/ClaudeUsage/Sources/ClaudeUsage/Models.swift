import Foundation

// MARK: - Live plan usage (claude.ai API)

/// A single usage metric: utilization percentage and optional reset time.
struct UsagePeriod: Codable, Sendable {
    var utilization: Double
    var resetsAt: String?
}

/// The optional purchased extra-credit balance.
///
/// The API reports `monthlyLimit` and `usedCredits` in cents, not dollars.
struct ExtraUsage: Codable, Sendable {
    var isEnabled: Bool
    var monthlyLimit: Double?
    var usedCredits: Double?
    var utilization: Double?
    var currency: String?

    /// Credits spent, converted from cents to whole currency units.
    var usedAmount: Double? { usedCredits.map { $0 / 100 } }
}

/// Names the model a scoped limit applies to.
struct LimitModel: Codable, Sendable {
    var id: String?
    var displayName: String?
}

/// Narrows a limit to one model or surface.
struct LimitScope: Codable, Sendable {
    var model: LimitModel?
}

/// One entry in the API's limits array, which superseded the per-model
/// `sevenDay*` fields — those now come back null. New models appear here
/// without a schema change, which is how Fable shows up.
struct Limit: Codable, Sendable {
    var kind: String    // session | weekly_all | weekly_scoped
    var group: String   // session | weekly
    var percent: Double
    var resetsAt: String?
    var scope: LimitScope?
    var isActive: Bool?

    /// The row heading for this limit.
    var label: String {
        switch kind {
        case "session": return "Current Session"
        case "weekly_all": return "All Models"
        default:
            if let name = scope?.model?.displayName, !name.isEmpty { return name }
            return "Weekly"
        }
    }
}

/// Response from /api/organizations/{id}/usage.
struct PlanUsage: Codable, Sendable {
    var limits: [Limit]?

    var fiveHour: UsagePeriod?
    var sevenDay: UsagePeriod?
    var sevenDayOpus: UsagePeriod?
    var sevenDaySonnet: UsagePeriod?
    var sevenDayOmelette: UsagePeriod? // Claude Design
    var extraUsage: ExtraUsage?
}

/// Response from /api/organizations/{id}/rate_limits.
struct RateLimits: Codable, Sendable {
    var rateLimitTier: String
}

// MARK: - Usage history (Claude Code's JSONL transcripts)

struct DailyActivity: Codable, Sendable {
    var date: String
    var messageCount: Int
    var sessionCount: Int
    var toolCallCount: Int
}

struct ModelUsage: Codable, Sendable {
    var inputTokens = 0
    var outputTokens = 0
    var cacheReadInputTokens = 0
    var cacheCreationInputTokens = 0
    /// Cache writes are billed per TTL — 1.25x input at five minutes, 2x at one
    /// hour — so the two are tracked apart. Their sum is
    /// `cacheCreationInputTokens`.
    var cacheCreation5mTokens = 0
    var cacheCreation1hTokens = 0
    var webSearchRequests = 0
    var messageCount = 0

    /// Computed from the token counts and this app's pricing table, not
    /// reported by Claude. nil for a model with no known rate, such as one
    /// routed through a non-Anthropic provider; the UI shows a dash rather than
    /// a number that would look authoritative.
    var costUSD: Double?
}

/// One session's extent. `activeMillis` counts only the time between messages
/// that arrived close together, so a session left open overnight doesn't report
/// the idle hours as work.
struct SessionSummary: Codable, Sendable {
    var sessionID = ""
    var activeMillis = 0
    var messageCount = 0
    var timestamp = ""
}

/// The aggregate the history window renders, built by walking Claude Code's
/// JSONL transcripts.
struct UsageStats: Codable, Sendable {
    var dailyActivity: [DailyActivity] = []
    var modelUsage: [String: ModelUsage] = [:]
    var totalSessions = 0
    var totalMessages = 0
    var busiestSession = SessionSummary()
    var firstSessionDate = ""
    var lastActivityDate = ""
}

// MARK: - Service status (status.claude.com)

struct ServiceStatus: Sendable {
    var indicator: String
    var description: String

    static let unknown = ServiceStatus(indicator: "unknown", description: "Status unavailable")
}

// MARK: - Errors

struct ClaudeUsageError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
    init(_ message: String) { self.message = message }
}
