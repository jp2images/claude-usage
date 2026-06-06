import Foundation

/// Fetches live plan usage from the Claude.ai API using the desktop app's
/// session cookies. Counterpart to api.go.
enum UsageAPI {

    struct Result: Sendable {
        let usage: PlanUsage
        let limits: RateLimits
    }

    static func load() async throws -> Result {
        let cookies = try ClaudeCookies.read()

        let usage: PlanUsage = try await fetch(
            "https://claude.ai/api/organizations/\(cookies.orgID)/usage",
            sessionKey: cookies.sessionKey)
        let limits: RateLimits = try await fetch(
            "https://claude.ai/api/organizations/\(cookies.orgID)/rate_limits",
            sessionKey: cookies.sessionKey)

        return Result(usage: usage, limits: limits)
    }

    private static func fetch<T: Decodable>(_ urlString: String, sessionKey: String) async throws -> T {
        guard let url = URL(string: urlString) else {
            throw ClaudeUsageError("invalid URL")
        }
        var request = URLRequest(url: url)
        request.setValue("sessionKey=\(sessionKey)", forHTTPHeaderField: "Cookie")
        request.setValue("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)", forHTTPHeaderField: "User-Agent")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = 15

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw ClaudeUsageError("no HTTP response")
        }
        if http.statusCode == 401 || http.statusCode == 403 {
            throw ClaudeUsageError("session expired — open the Claude desktop app to refresh")
        }
        guard http.statusCode == 200 else {
            throw ClaudeUsageError("HTTP \(http.statusCode)")
        }

        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .convertFromSnakeCase
        return try decoder.decode(T.self, from: data)
    }
}
