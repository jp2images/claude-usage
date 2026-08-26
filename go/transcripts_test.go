package main

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

// assistantRecord builds one JSONL line in the shape Claude Code writes.
func assistantRecord(msgID, reqID, model, ts, session string, out, cache1h int, toolCalls int) string {
	content := `{"type":"text"}`
	for i := 0; i < toolCalls; i++ {
		content += `,{"type":"tool_use"}`
	}
	return `{"type":"assistant","timestamp":"` + ts + `","sessionId":"` + session +
		`","requestId":"` + reqID + `","uuid":"u-` + msgID + `-` + reqID +
		`","message":{"id":"` + msgID + `","model":"` + model +
		`","content":[` + content + `],"usage":{"input_tokens":10,"output_tokens":` +
		itoa(out) + `,"cache_read_input_tokens":100,"cache_creation_input_tokens":` +
		itoa(cache1h) + `,"cache_creation":{"ephemeral_5m_input_tokens":0,` +
		`"ephemeral_1h_input_tokens":` + itoa(cache1h) + `}}}}`
}

func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	var b []byte
	for n > 0 {
		b = append([]byte{byte('0' + n%10)}, b...)
		n /= 10
	}
	return string(b)
}

func scanLines(t *testing.T, lines []string) *UsageStats {
	t.Helper()
	path := filepath.Join(t.TempDir(), "session.jsonl")
	body := ""
	for _, l := range lines {
		body += l + "\n"
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}

	acc := newUsageAccumulator()
	if err := scanTranscript(path, acc); err != nil {
		t.Fatalf("scanTranscript: %v", err)
	}
	return acc.result()
}

func TestDeduplicatesRepeatedMessages(t *testing.T) {
	// Claude Code writes an assistant record per streamed content block, so
	// one message recurs with an identical usage payload. Counting each
	// record would roughly double every total.
	line := assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 500, 1000, 0)
	stats := scanLines(t, []string{line, line, line})

	if stats.TotalMessages != 1 {
		t.Errorf("TotalMessages = %d, want 1", stats.TotalMessages)
	}
	u := stats.ModelUsage["claude-opus-5"]
	if u.OutputTokens != 500 {
		t.Errorf("OutputTokens = %d, want 500 (record counted more than once)", u.OutputTokens)
	}
}

func TestDistinctRequestIDsAreSeparateMessages(t *testing.T) {
	// A retry reuses the message id under a new request id; both are billed.
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 500, 0, 0),
		assistantRecord("msg-1", "req-2", "claude-opus-5", "2026-08-20T12:00:01.000Z", "s1", 500, 0, 0),
	})

	if stats.TotalMessages != 2 {
		t.Errorf("TotalMessages = %d, want 2", stats.TotalMessages)
	}
}

func TestSkipsSyntheticModel(t *testing.T) {
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "<synthetic>", "2026-08-20T12:00:00.000Z", "s1", 0, 0, 0),
		assistantRecord("msg-2", "req-2", "claude-opus-5", "2026-08-20T12:00:01.000Z", "s1", 500, 0, 0),
	})

	if _, found := stats.ModelUsage["<synthetic>"]; found {
		t.Error("synthetic model should not appear in per-model usage")
	}
	if stats.TotalMessages != 1 {
		t.Errorf("TotalMessages = %d, want 1", stats.TotalMessages)
	}
}

func TestCountsToolCallsAndSessions(t *testing.T) {
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 100, 0, 2),
		assistantRecord("msg-2", "req-2", "claude-opus-5", "2026-08-20T13:00:00.000Z", "s2", 100, 0, 3),
	})

	if stats.TotalSessions != 2 {
		t.Errorf("TotalSessions = %d, want 2", stats.TotalSessions)
	}
	if len(stats.DailyActivity) != 1 {
		t.Fatalf("DailyActivity has %d days, want 1", len(stats.DailyActivity))
	}
	day := stats.DailyActivity[0]
	if day.ToolCallCount != 5 {
		t.Errorf("ToolCallCount = %d, want 5", day.ToolCallCount)
	}
	if day.SessionCount != 2 {
		t.Errorf("SessionCount = %d, want 2", day.SessionCount)
	}
}

func TestCacheTTLSplitIsPreserved(t *testing.T) {
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 100, 4000, 0),
	})

	u := stats.ModelUsage["claude-opus-5"]
	if u.CacheCreation1hTokens != 4000 {
		t.Errorf("CacheCreation1hTokens = %d, want 4000", u.CacheCreation1hTokens)
	}
	if u.CacheCreation5mTokens != 0 {
		t.Errorf("CacheCreation5mTokens = %d, want 0", u.CacheCreation5mTokens)
	}
}

func TestCacheWriteWithoutTTLSplitFallsBackToFiveMinute(t *testing.T) {
	// Records predating the per-TTL breakdown report only a total. The
	// 5-minute TTL is the default, so the total is attributed there.
	line := `{"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","sessionId":"s1",` +
		`"requestId":"req-1","uuid":"u1","message":{"id":"msg-1","model":"claude-opus-5",` +
		`"content":[{"type":"text"}],"usage":{"input_tokens":10,"output_tokens":100,` +
		`"cache_read_input_tokens":0,"cache_creation_input_tokens":2000}}}`

	stats := scanLines(t, []string{line})
	u := stats.ModelUsage["claude-opus-5"]
	if u.CacheCreation5mTokens != 2000 {
		t.Errorf("CacheCreation5mTokens = %d, want 2000", u.CacheCreation5mTokens)
	}
	if u.CacheCreation1hTokens != 0 {
		t.Errorf("CacheCreation1hTokens = %d, want 0", u.CacheCreation1hTokens)
	}
}

func TestNonAssistantRecordsAreIgnored(t *testing.T) {
	stats := scanLines(t, []string{
		`{"type":"user","message":{"role":"user","content":"hi"}}`,
		`{"type":"ai-title","aiTitle":"something"}`,
		`not valid json at all`,
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 100, 0, 0),
	})

	if stats.TotalMessages != 1 {
		t.Errorf("TotalMessages = %d, want 1", stats.TotalMessages)
	}
}

func TestCostIsComputedPerModel(t *testing.T) {
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 1_000_000, 0, 0),
	})

	u := stats.ModelUsage["claude-opus-5"]
	if !u.CostKnown {
		t.Fatal("CostKnown = false, want true for claude-opus-5")
	}
	// 1M output at $25 + 10 input at $5/M + 100 cache reads at $0.50/M.
	if u.CostUSD < 25 || u.CostUSD > 25.001 {
		t.Errorf("CostUSD = %g, want ~25", u.CostUSD)
	}
}

func TestBusiestSessionRanksByMessagesNotElapsedTime(t *testing.T) {
	// A session resumed days later spans more wall clock than a busy one.
	// Ranking by elapsed time would report the idle session as the notable
	// one, which is what the old stats-cache metric did.
	stats := scanLines(t, []string{
		assistantRecord("a1", "r1", "claude-opus-5", "2026-08-20T09:00:00.000Z", "idle", 10, 0, 0),
		assistantRecord("a2", "r2", "claude-opus-5", "2026-08-23T09:00:00.000Z", "idle", 10, 0, 0),

		assistantRecord("b1", "r3", "claude-opus-5", "2026-08-20T10:00:00.000Z", "busy", 10, 0, 0),
		assistantRecord("b2", "r4", "claude-opus-5", "2026-08-20T10:05:00.000Z", "busy", 10, 0, 0),
		assistantRecord("b3", "r5", "claude-opus-5", "2026-08-20T10:10:00.000Z", "busy", 10, 0, 0),
	})

	if got := stats.BusiestSession.SessionID; got != "busy" {
		t.Errorf("BusiestSession = %q, want \"busy\"", got)
	}
	if got := stats.BusiestSession.MessageCount; got != 3 {
		t.Errorf("MessageCount = %d, want 3", got)
	}
}

func TestActiveDurationExcludesIdleGaps(t *testing.T) {
	// Two messages five minutes apart, then a three-day pause, then one
	// more. Only the five minutes counts as active.
	stats := scanLines(t, []string{
		assistantRecord("a1", "r1", "claude-opus-5", "2026-08-20T09:00:00.000Z", "s1", 10, 0, 0),
		assistantRecord("a2", "r2", "claude-opus-5", "2026-08-20T09:05:00.000Z", "s1", 10, 0, 0),
		assistantRecord("a3", "r3", "claude-opus-5", "2026-08-23T09:05:00.000Z", "s1", 10, 0, 0),
	})

	if got, want := stats.BusiestSession.ActiveMillis, (5 * time.Minute).Milliseconds(); got != want {
		t.Errorf("ActiveMillis = %d, want %d", got, want)
	}
}

func TestFingerprintChangesWithSchemaVersion(t *testing.T) {
	// A cache written by an older build must not be decoded into the current
	// UsageStats, where removed fields would silently read as zero.
	dir := t.TempDir()
	path := filepath.Join(dir, "session.jsonl")
	if err := os.WriteFile(path, []byte("{}\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	fp, err := transcriptFingerprint([]string{path})
	if err != nil {
		t.Fatal(err)
	}
	if readStatsCache(fp+"-different-schema") != nil {
		t.Error("a cache entry under a different fingerprint must not be reused")
	}
}

func TestTotalCostUnknownWhenAnyModelUnpriced(t *testing.T) {
	stats := scanLines(t, []string{
		assistantRecord("msg-1", "req-1", "claude-opus-5", "2026-08-20T12:00:00.000Z", "s1", 100, 0, 0),
		assistantRecord("msg-2", "req-2", "some-router/mystery", "2026-08-20T12:00:01.000Z", "s1", 100, 0, 0),
	})

	totals := totalTokens(stats.ModelUsage)
	if totals.CostKnown {
		t.Error("CostKnown = true, want false when a model has no known rate")
	}
}
