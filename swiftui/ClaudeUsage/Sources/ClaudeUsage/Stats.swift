import Foundation

/// Loads ~/.claude/stats-cache.json (Claude Code's local usage stats).
/// Counterpart to stats.go. The JSON is already camelCase, so no key strategy.
enum StatsRepository {

    static func load() throws -> StatsCache {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let url = home.appendingPathComponent(".claude/stats-cache.json")

        guard FileManager.default.fileExists(atPath: url.path) else {
            throw ClaudeUsageError(
                "Stats file not found at \(url.path)\n\nMake sure Claude Code is installed and has been used at least once.")
        }

        let data = try Data(contentsOf: url)
        var stats: StatsCache
        do {
            stats = try JSONDecoder().decode(StatsCache.self, from: data)
        } catch {
            throw ClaudeUsageError("cannot parse stats file: \(error.localizedDescription)")
        }

        stats.dailyActivity.sort { $0.date > $1.date } // newest first
        return stats
    }
}
