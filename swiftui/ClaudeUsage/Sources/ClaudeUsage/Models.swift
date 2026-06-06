import Foundation

// MARK: - Live plan usage (claude.ai API)

/// A single usage metric: utilization percentage and optional reset time.
struct UsagePeriod: Codable, Sendable {
    var utilization: Double
    var resetsAt: String?
}

/// The optional purchased extra-credit balance.
struct ExtraUsage: Codable, Sendable {
    var isEnabled: Bool
    var monthlyLimit: Double?
    var usedCredits: Double?
    var utilization: Double?
    var currency: String?
}

/// Response from /api/organizations/{id}/usage.
struct PlanUsage: Codable, Sendable {
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

// MARK: - Usage history (~/.claude/stats-cache.json)

struct DailyActivity: Codable, Sendable {
    var date: String
    var messageCount: Int
    var sessionCount: Int
    var toolCallCount: Int
}

struct ModelUsage: Codable, Sendable {
    var inputTokens: Int
    var outputTokens: Int
    var cacheReadInputTokens: Int
    var cacheCreationInputTokens: Int
    var webSearchRequests: Int
    var costUSD: Double

    // Defaults so a missing field in the JSON doesn't fail decoding.
    init(inputTokens: Int = 0, outputTokens: Int = 0, cacheReadInputTokens: Int = 0,
         cacheCreationInputTokens: Int = 0, webSearchRequests: Int = 0, costUSD: Double = 0) {
        self.inputTokens = inputTokens
        self.outputTokens = outputTokens
        self.cacheReadInputTokens = cacheReadInputTokens
        self.cacheCreationInputTokens = cacheCreationInputTokens
        self.webSearchRequests = webSearchRequests
        self.costUSD = costUSD
    }
}

struct LongestSession: Codable, Sendable {
    var duration: Int
    var messageCount: Int

    init(duration: Int = 0, messageCount: Int = 0) {
        self.duration = duration
        self.messageCount = messageCount
    }
}

struct StatsCache: Codable, Sendable {
    var lastComputedDate: String
    var dailyActivity: [DailyActivity]
    var modelUsage: [String: ModelUsage]
    var totalSessions: Int
    var totalMessages: Int
    var longestSession: LongestSession
    var firstSessionDate: String
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
