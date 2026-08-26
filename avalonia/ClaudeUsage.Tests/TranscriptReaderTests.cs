using ClaudeUsage.Services;
using Xunit;

namespace ClaudeUsage.Tests;

public class TranscriptReaderTests
{
    [Fact]
    public void RepeatedRecordsAreCountedOnce()
    {
        // Claude Code writes an assistant record per streamed content block, so
        // one message recurs with an identical usage payload. Counting each
        // record would roughly double every total.
        var line = Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5",
            "2026-08-20T12:00:00.000Z", "s1", outputTokens: 500, cacheWrite1h: 1000);

        var stats = Transcript.Scan(line, line, line);

        Assert.Equal(1, stats.TotalMessages);
        Assert.Equal(500L, stats.ModelUsage["claude-opus-5"].OutputTokens);
    }

    [Fact]
    public void DistinctRequestIdsAreSeparateMessages()
    {
        // A retry reuses the message id under a new request id; both are billed.
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 500),
            Transcript.AssistantRecord("msg-1", "req-2", "claude-opus-5", "2026-08-20T12:00:01.000Z", "s1", 500));

        Assert.Equal(2, stats.TotalMessages);
        Assert.Equal(1000L, stats.ModelUsage["claude-opus-5"].OutputTokens);
    }

    [Fact]
    public void SyntheticModelIsSkipped()
    {
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("msg-1", "req-1", "<synthetic>", "2026-08-20T12:00:00.000Z", "s1", 0),
            Transcript.AssistantRecord("msg-2", "req-2", "claude-opus-5", "2026-08-20T12:00:01.000Z", "s1", 500));

        Assert.DoesNotContain("<synthetic>", stats.ModelUsage.Keys);
        Assert.Equal(1, stats.TotalMessages);
    }

    [Fact]
    public void ToolCallsAndSessionsAreCountedPerDay()
    {
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", toolCalls: 2),
            Transcript.AssistantRecord("msg-2", "req-2", "claude-opus-5", "2026-08-20T13:00:00.000Z", "s2", toolCalls: 3));

        Assert.Equal(2, stats.TotalSessions);
        var day = Assert.Single(stats.DailyActivity);
        Assert.Equal(5, day.ToolCallCount);
        Assert.Equal(2, day.SessionCount);
    }

    [Fact]
    public void CacheTtlSplitIsPreserved()
    {
        var stats = Transcript.Scan(Transcript.AssistantRecord(
            "msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", cacheWrite1h: 4000));

        var usage = stats.ModelUsage["claude-opus-5"];
        Assert.Equal(4000L, usage.CacheCreation1hTokens);
        Assert.Equal(0L, usage.CacheCreation5mTokens);
        Assert.Equal(4000L, usage.CacheCreationInputTokens);
    }

    [Fact]
    public void CacheWriteWithoutTtlSplitFallsBackToFiveMinute()
    {
        // Records predating the per-TTL breakdown report only a total. The
        // 5-minute TTL is the default, so the total is attributed there.
        const string line = """
                            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","sessionId":"s1","requestId":"req-1","uuid":"u1","message":{"id":"msg-1","model":"claude-opus-5","content":[{"type":"text"}],"usage":{"input_tokens":10,"output_tokens":100,"cache_read_input_tokens":0,"cache_creation_input_tokens":2000}}}
                            """;

        var usage = Transcript.Scan(line).ModelUsage["claude-opus-5"];
        Assert.Equal(2000L, usage.CacheCreation5mTokens);
        Assert.Equal(0L, usage.CacheCreation1hTokens);
    }

    [Fact]
    public void NonAssistantAndMalformedLinesAreIgnored()
    {
        var stats = Transcript.Scan(
            """{"type":"user","message":{"role":"user","content":"hi"}}""",
            """{"type":"ai-title","aiTitle":"something"}""",
            "not valid json at all",
            Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1"));

        Assert.Equal(1, stats.TotalMessages);
    }

    [Fact]
    public void UndatedRecordIsDroppedEntirely()
    {
        // A record that can't be placed in time is malformed, so it counts for
        // nothing — not its tokens, and not the overview's message total.
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 500),
            Transcript.AssistantRecord("msg-2", "req-2", "claude-opus-5", "not-a-timestamp", "s1", 500));

        var usage = stats.ModelUsage["claude-opus-5"];
        Assert.Equal(500L, usage.OutputTokens);
        Assert.Equal(1, usage.MessageCount);
        Assert.Equal(1, stats.TotalMessages);
        // Counting a record's tokens but not its day would leave these apart.
        Assert.Equal(stats.TotalMessages, stats.DailyActivity.Sum(d => d.MessageCount));
    }

    [Fact]
    public void EmptyModelSetHasNoKnownCost()
    {
        // Transcripts with no assistant records should read as a dash, not as
        // an authoritative $0.0000.
        var stats = Transcript.Scan("""{"type":"user","message":{"role":"user","content":"hi"}}""");

        Assert.Empty(stats.ModelUsage);
        var totals = Formatting.TotalTokens(stats.ModelUsage);
        Assert.False(totals.CostKnown);
        Assert.Equal("—", Formatting.CostLabel(totals));
    }

    [Fact]
    public void CostIsComputedPerModel()
    {
        var stats = Transcript.Scan(Transcript.AssistantRecord(
            "msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", outputTokens: 1_000_000));

        var usage = stats.ModelUsage["claude-opus-5"];
        Assert.True(usage.CostKnown);
        // 1M output at $25 + 10 input at $5/M + 100 cache reads at $0.50/M.
        Assert.Equal(25.0, usage.CostUSD, 3);
    }

    [Fact]
    public void TotalCostIsUnknownWhenAnyModelIsUnpriced()
    {
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1"),
            Transcript.AssistantRecord("msg-2", "req-2", "some-router/mystery", "2026-08-20T12:00:01.000Z", "s1"));

        Assert.False(Formatting.TotalTokens(stats.ModelUsage).CostKnown);
        Assert.Equal("—", Formatting.CostLabel(Formatting.TotalTokens(stats.ModelUsage)));
    }

    [Fact]
    public void BusiestSessionRanksByMessagesNotElapsedTime()
    {
        // A session resumed days later spans more wall clock than a busy one.
        // Ranking by elapsed time would report the idle session as the notable
        // one, which is what the old stats-cache metric did.
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("a1", "r1", "claude-opus-5", "2026-08-20T09:00:00.000Z", "idle"),
            Transcript.AssistantRecord("a2", "r2", "claude-opus-5", "2026-08-23T09:00:00.000Z", "idle"),
            Transcript.AssistantRecord("b1", "r3", "claude-opus-5", "2026-08-20T10:00:00.000Z", "busy"),
            Transcript.AssistantRecord("b2", "r4", "claude-opus-5", "2026-08-20T10:05:00.000Z", "busy"),
            Transcript.AssistantRecord("b3", "r5", "claude-opus-5", "2026-08-20T10:10:00.000Z", "busy"));

        Assert.Equal("busy", stats.BusiestSession.SessionId);
        Assert.Equal(3, stats.BusiestSession.MessageCount);
    }

    [Fact]
    public void ActiveDurationExcludesIdleGaps()
    {
        // Two messages five minutes apart, then a three-day pause, then one
        // more. Only the five minutes counts as active.
        var stats = Transcript.Scan(
            Transcript.AssistantRecord("a1", "r1", "claude-opus-5", "2026-08-20T09:00:00.000Z", "s1"),
            Transcript.AssistantRecord("a2", "r2", "claude-opus-5", "2026-08-20T09:05:00.000Z", "s1"),
            Transcript.AssistantRecord("a3", "r3", "claude-opus-5", "2026-08-23T09:05:00.000Z", "s1"));

        Assert.Equal((long)TimeSpan.FromMinutes(5).TotalMilliseconds, stats.BusiestSession.ActiveMillis);
    }

    [Fact]
    public void MissingRootYieldsNoTranscripts()
    {
        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Assert.Throws<FileNotFoundException>(() => TranscriptReader.Load(missing));
    }
}
