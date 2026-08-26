import CryptoKit
import Foundation

/// Reads Claude Code's JSONL transcripts under ~/.claude/projects and
/// aggregates token usage, cost, and daily activity. Counterpart to
/// transcripts.go.
///
/// This replaces ~/.claude/stats-cache.json, which Claude Code no longer
/// maintains. The transcripts also carry per-message timestamps and cache-TTL
/// detail that the cache file never held.
enum TranscriptReader {

    /// Only assistant records carry a usage payload, and they are the only
    /// lines this reader parses. Transcripts are compact JSON, so the marker
    /// has no space after the colon.
    private static let assistantMarker = Data(#""type":"assistant""#.utf8)

    /// Bytes read per `read(upToCount:)`. Lines are handed to the accumulator
    /// as they complete, so a 200 MB corpus never lands in memory at once.
    private static let chunkSize = 1 << 20

    static var defaultRoot: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".claude/projects")
    }

    /// Aggregates every transcript under `root`, using the cached aggregate
    /// when no transcript has changed since it was written.
    static func load(root: URL = defaultRoot) throws -> UsageStats {
        let paths = transcriptPaths(under: root)
        guard !paths.isEmpty else {
            throw ClaudeUsageError(
                "No usage transcripts found under \(root.path)\n\nMake sure Claude Code is installed and has been used at least once.")
        }

        let fingerprint = fingerprint(for: paths)
        if let fingerprint, let cached = readCache(matching: fingerprint) {
            return cached
        }

        var accumulator = UsageAccumulator()
        for path in paths {
            do {
                try scan(path, into: &accumulator)
            } catch {
                throw ClaudeUsageError("cannot read \(path.lastPathComponent): \(error.localizedDescription)")
            }
        }

        let stats = accumulator.result()
        if let fingerprint {
            writeCache(fingerprint, stats)
        }
        return stats
    }

    /// Every .jsonl under `root`, sorted so a run is reproducible.
    static func transcriptPaths(under root: URL) -> [URL] {
        // An unreadable subdirectory shouldn't abort the whole walk.
        guard let walker = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: nil,
            options: [],
            errorHandler: { _, _ in true }
        ) else { return [] }

        var paths: [URL] = []
        for case let url as URL in walker where url.pathExtension == "jsonl" {
            paths.append(url)
        }
        return paths.sorted { $0.path < $1.path }
    }

    /// Folds one file's assistant records into the accumulator.
    static func scan(_ url: URL, into accumulator: inout UsageAccumulator) throws {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }

        let decoder = JSONDecoder()
        var buffer = Data()

        while let chunk = try handle.read(upToCount: chunkSize), !chunk.isEmpty {
            buffer.append(chunk)

            var lineStart = buffer.startIndex
            while let newline = buffer[lineStart...].firstIndex(of: UInt8(ascii: "\n")) {
                process(buffer[lineStart..<newline], decoder, into: &accumulator)
                lineStart = buffer.index(after: newline)
            }
            // Keep only the partial trailing line; copying it drops the rest of
            // the chunk's allocation.
            buffer = Data(buffer[lineStart...])
        }

        if !buffer.isEmpty {
            process(buffer, decoder, into: &accumulator)
        }
    }

    private static func process(_ line: Data, _ decoder: JSONDecoder, into accumulator: inout UsageAccumulator) {
        // Most lines are user turns, attachments, or bookkeeping. A substring
        // test skips them without paying for a JSON parse.
        guard line.range(of: assistantMarker) != nil else { return }
        // A malformed line shouldn't discard the rest of the session.
        guard let record = try? decoder.decode(TranscriptRecord.self, from: line) else { return }
        guard record.message?.id?.isEmpty == false || record.uuid?.isEmpty == false else { return }
        accumulator.add(record)
    }
}

// MARK: - Record shape

/// The subset of an assistant record that usage aggregation needs. Every field
/// is optional: transcripts written by older Claude Code builds omit some of
/// them, and a missing field means zero rather than a discarded record.
struct TranscriptRecord: Decodable {
    var timestamp: String?
    var sessionId: String?
    var requestId: String?
    var uuid: String?
    var message: Message?

    struct Message: Decodable {
        var id: String?
        var model: String?
        var content: [ContentBlock]?
        var usage: Usage?
    }

    struct ContentBlock: Decodable {
        var type: String?
    }

    struct Usage: Decodable {
        var inputTokens: Int?
        var outputTokens: Int?
        var cacheReadInputTokens: Int?
        var cacheCreationInputTokens: Int?
        var cacheCreation: CacheCreation?
        var serverToolUse: ServerToolUse?

        enum CodingKeys: String, CodingKey {
            case inputTokens = "input_tokens"
            case outputTokens = "output_tokens"
            case cacheReadInputTokens = "cache_read_input_tokens"
            case cacheCreationInputTokens = "cache_creation_input_tokens"
            case cacheCreation = "cache_creation"
            case serverToolUse = "server_tool_use"
        }
    }

    struct CacheCreation: Decodable {
        var ephemeral5m: Int?
        var ephemeral1h: Int?

        enum CodingKeys: String, CodingKey {
            case ephemeral5m = "ephemeral_5m_input_tokens"
            case ephemeral1h = "ephemeral_1h_input_tokens"
        }
    }

    struct ServerToolUse: Decodable {
        var webSearchRequests: Int?

        enum CodingKeys: String, CodingKey {
            case webSearchRequests = "web_search_requests"
        }
    }
}

// MARK: - Accumulator

/// Collects every deduplicated record into the shape the history window
/// renders.
struct UsageAccumulator {

    /// The placeholder Claude Code records for messages it generates locally.
    /// They have no tokens and no cost, so they are skipped.
    private static let syntheticModel = "<synthetic>"

    /// The pause after which a session counts as picked up again rather than
    /// still running. A resumed session can span days of wall clock, so its
    /// first-to-last extent says nothing about how long it was worked on.
    private static let idleGap: TimeInterval = 30 * 60

    /// One calendar day of activity. Sessions are held as a set because a
    /// session's messages are spread across the day's records.
    private struct DayTotals {
        var messages = 0
        var toolCalls = 0
        var sessions: Set<String> = []
    }

    /// One session, tracked so the busiest can be reported. `active`
    /// accumulates only the gaps shorter than `idleGap`.
    private struct SessionSpan {
        var first: Date
        var last: Date
        var active: TimeInterval = 0
        var messages = 0
    }

    private var seen: Set<String> = []
    private var models: [String: ModelUsage] = [:]
    private var days: [String: DayTotals] = [:]
    private var sessions: [String: SessionSpan] = [:]
    private var first: Date?
    private var messages = 0

    // Formatters are held per accumulator rather than shared: they are not
    // Sendable, and rebuilding one per record would dominate the walk.
    private let fractionalTimestamps: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private let plainTimestamps: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    /// Days are keyed by the local calendar date, which is what the activity
    /// table shows.
    private let dayKeys: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        f.locale = Locale(identifier: "en_US_POSIX")
        return f
    }()

    private let sessionTimestamps: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        f.timeZone = .current
        return f
    }()

    /// Folds one record into the totals, ignoring records already seen.
    mutating func add(_ record: TranscriptRecord) {
        // Claude Code writes an assistant record per streamed content block, so
        // the same message recurs with an identical usage payload. Deduplicate
        // on (message id, request id) and keep the first — counting every
        // record roughly doubles every total.
        let id = record.message?.id.flatMap { $0.isEmpty ? nil : $0 } ?? record.uuid ?? ""
        let key = id + "|" + (record.requestId ?? "")
        guard seen.insert(key).inserted else { return }

        guard let model = record.message?.model,
              !model.isEmpty, model != Self.syntheticModel
        else { return }

        // A record that can't be placed in time is malformed, and is dropped
        // like malformed JSON rather than counted. Half-counting it — tokens
        // and totalMessages but no day — would leave the overview's message
        // count disagreeing with the sum of the daily rows.
        guard let timestamp = record.timestamp, let time = date(from: timestamp) else { return }

        models[model, default: ModelUsage()].accumulate(record.message?.usage)
        messages += 1

        let toolCalls = record.message?.content?.count { $0.type == "tool_use" } ?? 0

        first = min(first ?? time, time)

        let sessionID = record.sessionId.flatMap { $0.isEmpty ? nil : $0 }

        let dayKey = dayKeys.string(from: time)
        var day = days[dayKey] ?? DayTotals()
        day.messages += 1
        day.toolCalls += toolCalls
        if let sessionID {
            day.sessions.insert(sessionID)
        }
        days[dayKey] = day

        guard let sessionID else { return }

        var span = sessions[sessionID] ?? SessionSpan(first: time, last: time)
        span.first = min(span.first, time)
        // Records arrive in order within a session, so a forward step is the
        // time since the previous message. Anything longer than idleGap was the
        // session sitting idle, not being worked on.
        if time > span.last {
            let gap = time.timeIntervalSince(span.last)
            if gap < Self.idleGap {
                span.active += gap
            }
            span.last = time
        }
        span.messages += 1
        sessions[sessionID] = span
    }

    /// Converts the accumulated maps into a `UsageStats`.
    func result() -> UsageStats {
        var stats = UsageStats()
        stats.totalMessages = messages
        stats.totalSessions = sessions.count

        for (model, usage) in models {
            var priced = usage
            priced.costUSD = Pricing.cost(for: model, usage: usage)
            stats.modelUsage[model] = priced
        }

        stats.dailyActivity = days.map { date, totals in
            DailyActivity(
                date: date,
                messageCount: totals.messages,
                sessionCount: totals.sessions.count,
                toolCallCount: totals.toolCalls
            )
        }
        .sorted { $0.date > $1.date }

        // Rank by message count, not elapsed time: the session left open
        // longest is usually one that was idle, not one that did the most work.
        if let busiest = sessions.max(by: { $0.value.messages < $1.value.messages }),
           busiest.value.messages > 0 {
            stats.busiestSession = SessionSummary(
                sessionID: busiest.key,
                activeMillis: Int((busiest.value.active * 1000).rounded()),
                messageCount: busiest.value.messages,
                timestamp: sessionTimestamps.string(from: busiest.value.first)
            )
        }

        if let first {
            stats.firstSessionDate = sessionTimestamps.string(from: first)
        }
        stats.lastActivityDate = stats.dailyActivity.first?.date ?? ""

        return stats
    }

    /// Claude Code writes millisecond precision, but the fractional formatter
    /// takes any number of digits, and the plain one covers a timestamp written
    /// with no fraction at all.
    private func date(from timestamp: String) -> Date? {
        fractionalTimestamps.date(from: timestamp) ?? plainTimestamps.date(from: timestamp)
    }
}

private extension ModelUsage {
    /// Folds one record's usage payload into the running per-model total.
    mutating func accumulate(_ usage: TranscriptRecord.Usage?) {
        inputTokens += usage?.inputTokens ?? 0
        outputTokens += usage?.outputTokens ?? 0
        cacheReadInputTokens += usage?.cacheReadInputTokens ?? 0
        cacheCreationInputTokens += usage?.cacheCreationInputTokens ?? 0
        webSearchRequests += usage?.serverToolUse?.webSearchRequests ?? 0
        messageCount += 1

        // Cache writes bill differently per TTL, so they are tracked
        // separately. Records predating the split report only a total;
        // attribute those to the 5-minute TTL, which is the default.
        var write5m = usage?.cacheCreation?.ephemeral5m ?? 0
        let write1h = usage?.cacheCreation?.ephemeral1h ?? 0
        if write5m == 0 && write1h == 0 {
            write5m = usage?.cacheCreationInputTokens ?? 0
        }
        cacheCreation5mTokens += write5m
        cacheCreation1hTokens += write1h
    }
}

// MARK: - Aggregate cache

/// Re-reading every transcript on each refresh is wasteful when nothing has
/// changed. The aggregate is cached against a fingerprint of the file set; any
/// write to any transcript changes the fingerprint and forces a re-read.
extension TranscriptReader {

    private struct CachedStats: Codable {
        var fingerprint: String
        var stats: UsageStats
    }

    /// Folded into the fingerprint so a cache written by an older build is
    /// discarded rather than decoded into the current `UsageStats`, where
    /// removed fields would silently read as zero. Bump this whenever
    /// `UsageStats` or `ModelUsage` changes shape.
    static let schemaVersion = 1

    /// A hash over the schema version and every transcript's path, size, and
    /// modification time. nil when a file can't be stat'ed, which forces a full
    /// read rather than trusting a stale aggregate.
    ///
    /// Size and mtime come from stat rather than `URL.resourceValues`, which
    /// caches them on the URL instance and would keep reporting a rewritten
    /// file's old values.
    static func fingerprint(for paths: [URL]) -> String? {
        var hasher = SHA256()
        hasher.update(data: Data("schema:\(schemaVersion)\n".utf8))

        for path in paths {
            var info = stat()
            guard stat(path.path, &info) == 0 else { return nil }

            let modified = info.st_mtimespec
            hasher.update(data: Data("\(path.path):\(info.st_size):\(modified.tv_sec).\(modified.tv_nsec)\n".utf8))
        }

        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// The filename carries the implementation. On macOS this resolves to the
    /// same directory as the Go app's `os.UserCacheDir()`, and both write the
    /// same kind of aggregate — sharing one filename would have them decode
    /// each other's JSON, whose field names differ only in case (`costUSD`
    /// against `costUsd`), and silently read every cost as absent.
    static var cacheURL: URL? {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first?
            .appendingPathComponent("ClaudeUsage/transcript-stats-swift.json")
    }

    /// The cached aggregate when it matches, nil otherwise. A failed read falls
    /// back to a full re-read rather than surfacing an error.
    static func readCache(matching fingerprint: String) -> UsageStats? {
        guard let url = cacheURL,
              let data = try? Data(contentsOf: url),
              let cached = try? JSONDecoder().decode(CachedStats.self, from: data),
              cached.fingerprint == fingerprint
        else { return nil }
        return cached.stats
    }

    /// A failed cache write only costs a re-read next time.
    static func writeCache(_ fingerprint: String, _ stats: UsageStats) {
        guard let url = cacheURL else { return }
        try? FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard let data = try? JSONEncoder().encode(CachedStats(fingerprint: fingerprint, stats: stats)) else { return }
        try? data.write(to: url)
    }
}
