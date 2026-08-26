import Foundation
import Testing

@testable import ClaudeUsage

/// One JSONL line in the shape Claude Code writes. Cache writes are reported on
/// the 1-hour TTL so the split can be checked; `TTLFallback` covers the older
/// records that report only a total.
private func assistantRecord(
    id: String,
    requestID: String,
    model: String = "claude-opus-5",
    timestamp: String,
    session: String = "s1",
    output: Int = 100,
    cache1h: Int = 0,
    toolCalls: Int = 0
) -> String {
    let content = ([#"{"type":"text"}"#] + Array(repeating: #"{"type":"tool_use"}"#, count: toolCalls))
        .joined(separator: ",")
    return """
        {"type":"assistant","timestamp":"\(timestamp)","sessionId":"\(session)",\
        "requestId":"\(requestID)","uuid":"u-\(id)-\(requestID)",\
        "message":{"id":"\(id)","model":"\(model)","content":[\(content)],\
        "usage":{"input_tokens":10,"output_tokens":\(output),"cache_read_input_tokens":100,\
        "cache_creation_input_tokens":\(cache1h),\
        "cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":\(cache1h)}}}}
        """
}

/// Writes the lines to a throwaway transcript and returns the aggregate.
private func aggregate(_ lines: [String]) throws -> UsageStats {
    let path = FileManager.default.temporaryDirectory
        .appendingPathComponent("\(UUID().uuidString).jsonl")
    try (lines.joined(separator: "\n") + "\n").write(to: path, atomically: true, encoding: .utf8)
    defer { try? FileManager.default.removeItem(at: path) }

    var accumulator = UsageAccumulator()
    try TranscriptReader.scan(path, into: &accumulator)
    return accumulator.result()
}

@Suite("Transcript aggregation")
struct TranscriptTests {

    @Test("A message repeated once per streamed block is counted once")
    func deduplicatesRepeatedMessages() throws {
        // Claude Code writes an assistant record per streamed content block, so
        // one message recurs with an identical usage payload. Counting each
        // record would roughly double every total.
        let line = assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", output: 500)
        let stats = try aggregate([line, line, line])

        #expect(stats.totalMessages == 1)
        #expect(stats.modelUsage["claude-opus-5"]?.outputTokens == 500)
    }

    @Test("A retry under a new request id is a separate message")
    func distinctRequestIDsCountSeparately() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", output: 500),
            assistantRecord(id: "msg-1", requestID: "req-2", timestamp: "2026-08-20T12:00:01.000Z", output: 500),
        ])

        #expect(stats.totalMessages == 2)
        #expect(stats.modelUsage["claude-opus-5"]?.outputTokens == 1000)
    }

    @Test("Locally generated messages are skipped")
    func skipsSyntheticModel() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", model: "<synthetic>", timestamp: "2026-08-20T12:00:00.000Z", output: 0),
            assistantRecord(id: "msg-2", requestID: "req-2", timestamp: "2026-08-20T12:00:01.000Z", output: 500),
        ])

        #expect(stats.modelUsage["<synthetic>"] == nil)
        #expect(stats.totalMessages == 1)
    }

    @Test("Cache writes keep their TTL bucket")
    func cacheTTLSplitIsPreserved() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", cache1h: 4000)
        ])

        let usage = try #require(stats.modelUsage["claude-opus-5"])
        #expect(usage.cacheCreation1hTokens == 4000)
        #expect(usage.cacheCreation5mTokens == 0)
        #expect(usage.cacheCreationInputTokens == 4000)
    }

    @Test("A cache write with no TTL breakdown falls back to the 5-minute bucket")
    func cacheWriteWithoutTTLSplit() throws {
        // Records predating the per-TTL breakdown report only a total. The
        // 5-minute TTL is the default, so the total is attributed there.
        let line = """
            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","sessionId":"s1",\
            "requestId":"req-1","uuid":"u1","message":{"id":"msg-1","model":"claude-opus-5",\
            "content":[{"type":"text"}],"usage":{"input_tokens":10,"output_tokens":100,\
            "cache_read_input_tokens":0,"cache_creation_input_tokens":2000}}}
            """
        let stats = try aggregate([line])

        let usage = try #require(stats.modelUsage["claude-opus-5"])
        #expect(usage.cacheCreation5mTokens == 2000)
        #expect(usage.cacheCreation1hTokens == 0)
    }

    @Test("Tool calls and sessions are counted per day")
    func countsToolCallsAndSessions() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", session: "s1", toolCalls: 2),
            assistantRecord(id: "msg-2", requestID: "req-2", timestamp: "2026-08-20T13:00:00.000Z", session: "s2", toolCalls: 3),
        ])

        #expect(stats.totalSessions == 2)
        let day = try #require(stats.dailyActivity.first)
        #expect(stats.dailyActivity.count == 1)
        #expect(day.toolCallCount == 5)
        #expect(day.sessionCount == 2)
        #expect(stats.lastActivityDate == day.date)
    }

    @Test("User turns, bookkeeping, and malformed lines are ignored")
    func nonAssistantRecordsAreIgnored() throws {
        let stats = try aggregate([
            #"{"type":"user","message":{"role":"user","content":"hi"}}"#,
            #"{"type":"ai-title","aiTitle":"something"}"#,
            "not valid json at all",
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z"),
        ])

        #expect(stats.totalMessages == 1)
    }

    @Test("A record with no usable timestamp is dropped rather than half-counted")
    func undatedRecordsAreDropped() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", output: 500),
            assistantRecord(id: "msg-2", requestID: "req-2", timestamp: "not-a-timestamp", output: 900),
        ])

        #expect(stats.totalMessages == 1)
        #expect(stats.modelUsage["claude-opus-5"]?.outputTokens == 500)
        // The overview's message count has to equal the sum of the daily rows.
        #expect(stats.dailyActivity.map(\.messageCount).reduce(0, +) == stats.totalMessages)
    }

    @Test("Timestamps parse at any fractional-second precision", arguments: [
        "2026-08-20T12:00:00Z",
        "2026-08-20T12:00:00.1Z",
        "2026-08-20T12:00:00.123Z",
        "2026-08-20T12:00:00.123456Z",
        "2026-08-20T12:00:00.123456789-05:00",
    ])
    func timestampPrecision(timestamp: String) throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: timestamp, output: 500)
        ])

        #expect(stats.totalMessages == 1)
        #expect(stats.dailyActivity.count == 1)
        #expect(stats.modelUsage["claude-opus-5"]?.outputTokens == 500)
    }

    @Test("A line larger than one read is assembled, and an oversized bad line is skipped")
    func oversizedLines() throws {
        // The reader has no line-length cap: a record spanning several reads is
        // stitched back together, and an oversized malformed one is skipped like
        // any other bad line rather than failing the whole file.
        let padding = String(repeating: "x", count: 3 << 20)
        let huge = """
            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","sessionId":"s1",\
            "requestId":"req-1","uuid":"u1","message":{"id":"msg-1","model":"claude-opus-5",\
            "content":[{"type":"text","text":"\(padding)"}],\
            "usage":{"input_tokens":10,"output_tokens":500,"cache_read_input_tokens":0,\
            "cache_creation_input_tokens":0}}}
            """
        let hugeAndTruncated = #"{"type":"assistant","cut off mid-record":"# + padding

        let stats = try aggregate([
            huge,
            hugeAndTruncated,
            assistantRecord(id: "msg-2", requestID: "req-2", timestamp: "2026-08-20T12:00:01.000Z"),
        ])

        #expect(stats.totalMessages == 2)
        #expect(stats.modelUsage["claude-opus-5"]?.outputTokens == 600)
    }

    @Test("The busiest session is the one with the most messages, not the longest span")
    func busiestSessionRanksByMessages() throws {
        // A session resumed days later spans more wall clock than a busy one.
        // Ranking by elapsed time would report the idle session as the notable
        // one, which is what the old stats-cache metric did.
        let stats = try aggregate([
            assistantRecord(id: "a1", requestID: "r1", timestamp: "2026-08-20T09:00:00.000Z", session: "idle"),
            assistantRecord(id: "a2", requestID: "r2", timestamp: "2026-08-23T09:00:00.000Z", session: "idle"),

            assistantRecord(id: "b1", requestID: "r3", timestamp: "2026-08-20T10:00:00.000Z", session: "busy"),
            assistantRecord(id: "b2", requestID: "r4", timestamp: "2026-08-20T10:05:00.000Z", session: "busy"),
            assistantRecord(id: "b3", requestID: "r5", timestamp: "2026-08-20T10:10:00.000Z", session: "busy"),
        ])

        #expect(stats.busiestSession.sessionID == "busy")
        #expect(stats.busiestSession.messageCount == 3)
    }

    @Test("Active duration skips the gaps a session spent idle")
    func activeDurationExcludesIdleGaps() throws {
        // Two messages five minutes apart, then a three-day pause, then one
        // more. Only the five minutes counts as active.
        let stats = try aggregate([
            assistantRecord(id: "a1", requestID: "r1", timestamp: "2026-08-20T09:00:00.000Z"),
            assistantRecord(id: "a2", requestID: "r2", timestamp: "2026-08-20T09:05:00.000Z"),
            assistantRecord(id: "a3", requestID: "r3", timestamp: "2026-08-23T09:05:00.000Z"),
        ])

        #expect(stats.busiestSession.activeMillis == 5 * 60 * 1000)
    }

    @Test("Cost is priced per model from its token counts")
    func costIsComputedPerModel() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z", output: 1_000_000)
        ])

        // 1M output at $25 + 10 input at $5/M + 100 cache reads at $0.50/M.
        let cost = try #require(stats.modelUsage["claude-opus-5"]?.costUSD)
        #expect(cost > 25 && cost < 25.001)
    }

    @Test("A total is unknown when any contributing model has no rate")
    func totalCostUnknownWhenAnyModelUnpriced() throws {
        let stats = try aggregate([
            assistantRecord(id: "msg-1", requestID: "req-1", timestamp: "2026-08-20T12:00:00.000Z"),
            assistantRecord(id: "msg-2", requestID: "req-2", model: "some-router/mystery", timestamp: "2026-08-20T12:00:01.000Z"),
        ])

        #expect(Formatting.totalTokens(stats.modelUsage).costUSD == nil)
    }

    @Test("No models at all is an unknown cost, not $0.0000")
    func emptyModelSetHasNoKnownCost() throws {
        let stats = try aggregate([#"{"type":"user","message":{"role":"user","content":"hi"}}"#])

        #expect(stats.modelUsage.isEmpty)
        #expect(Formatting.totalTokens(stats.modelUsage).costUSD == nil)
        #expect(Formatting.cost(Formatting.totalTokens(stats.modelUsage).costUSD) == "—")
    }

    @Test("A cache entry under a different fingerprint is not reused")
    func cacheRejectsForeignFingerprint() throws {
        // A cache written by an older build must not be decoded into the
        // current UsageStats, where removed fields would silently read as zero.
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("\(UUID().uuidString).jsonl")
        try "{}\n".write(to: path, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: path) }

        let fingerprint = try #require(TranscriptReader.fingerprint(for: [path]))
        #expect(TranscriptReader.readCache(matching: fingerprint + "-different-schema") == nil)
    }

    @Test("A rewritten transcript changes the fingerprint")
    func fingerprintTracksFileChanges() throws {
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("\(UUID().uuidString).jsonl")
        try "{}\n".write(to: path, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: path) }

        let before = try #require(TranscriptReader.fingerprint(for: [path]))
        try "{}\n{}\n".write(to: path, atomically: true, encoding: .utf8)
        let after = try #require(TranscriptReader.fingerprint(for: [path]))

        #expect(before != after)
    }
}
