package main

import (
	"bufio"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"time"
)

// Claude Code writes one JSONL transcript per session under ~/.claude/projects.
// Only assistant records carry a usage payload, and they are the only lines
// this reader parses.
const assistantMarker = `"type":"assistant"`

// syntheticModel is the placeholder Claude Code records for messages it
// generates locally. They have no tokens and no cost, so they are skipped.
const syntheticModel = "<synthetic>"

// maxTranscriptLine caps a single JSONL line. Tool results and attachments can
// be large, so this is far above a typical line; a file with a longer line is
// reported rather than silently truncated.
const maxTranscriptLine = 64 * 1024 * 1024

// transcriptRecord is the subset of an assistant record that usage
// aggregation needs.
type transcriptRecord struct {
	Timestamp string `json:"timestamp"`
	SessionID string `json:"sessionId"`
	RequestID string `json:"requestId"`
	UUID      string `json:"uuid"`
	Message   struct {
		ID      string `json:"id"`
		Model   string `json:"model"`
		Content []struct {
			Type string `json:"type"`
		} `json:"content"`
		Usage struct {
			InputTokens              int `json:"input_tokens"`
			OutputTokens             int `json:"output_tokens"`
			CacheReadInputTokens     int `json:"cache_read_input_tokens"`
			CacheCreationInputTokens int `json:"cache_creation_input_tokens"`
			CacheCreation            struct {
				Ephemeral5m int `json:"ephemeral_5m_input_tokens"`
				Ephemeral1h int `json:"ephemeral_1h_input_tokens"`
			} `json:"cache_creation"`
			ServerToolUse struct {
				WebSearchRequests int `json:"web_search_requests"`
			} `json:"server_tool_use"`
		} `json:"usage"`
	} `json:"message"`
}

func transcriptDir() (string, error) {
	home, err := os.UserHomeDir()
	if err != nil {
		return "", fmt.Errorf("cannot find home directory: %w", err)
	}
	return filepath.Join(home, ".claude", "projects"), nil
}

func findTranscripts(root string) ([]string, error) {
	var paths []string
	err := filepath.WalkDir(root, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			// An unreadable subdirectory shouldn't abort the whole walk.
			return nil
		}
		if !d.IsDir() && filepath.Ext(path) == ".jsonl" {
			paths = append(paths, path)
		}
		return nil
	})
	if err != nil {
		return nil, err
	}
	sort.Strings(paths)
	return paths, nil
}

// idleGap is the pause after which a session is treated as picked up again
// rather than still running. A resumed session can span days of wall clock,
// so its first-to-last extent says nothing about how long it was worked on.
const idleGap = 30 * time.Minute

// sessionSpan tracks a session so the busiest one can be reported. active
// accumulates only the gaps shorter than idleGap.
type sessionSpan struct {
	first    time.Time
	last     time.Time
	active   time.Duration
	messages int
}

// dayTotals accumulates one calendar day of activity. Sessions are held as a
// set because a session's messages are spread across the day's records.
type dayTotals struct {
	messages  int
	toolCalls int
	sessions  map[string]struct{}
}

// usageAccumulator collects every deduplicated record into the shape the
// history window renders.
type usageAccumulator struct {
	seen     map[string]struct{}
	models   map[string]*ModelUsage
	days     map[string]*dayTotals
	sessions map[string]*sessionSpan
	first    time.Time
	messages int
}

func newUsageAccumulator() *usageAccumulator {
	return &usageAccumulator{
		seen:     make(map[string]struct{}),
		models:   make(map[string]*ModelUsage),
		days:     make(map[string]*dayTotals),
		sessions: make(map[string]*sessionSpan),
	}
}

// add folds one record into the totals, ignoring records already seen.
func (a *usageAccumulator) add(rec *transcriptRecord) {
	// Claude Code writes an assistant record per streamed content block, so the
	// same message recurs with an identical usage payload. Deduplicate on
	// (message id, request id) and keep the first — counting every record
	// roughly doubles every total.
	id := rec.Message.ID
	if id == "" {
		id = rec.UUID
	}
	key := id + "|" + rec.RequestID
	if _, dup := a.seen[key]; dup {
		return
	}
	a.seen[key] = struct{}{}

	model := rec.Message.Model
	if model == "" || model == syntheticModel {
		return
	}

	u := a.models[model]
	if u == nil {
		u = &ModelUsage{}
		a.models[model] = u
	}

	usage := rec.Message.Usage
	u.InputTokens += usage.InputTokens
	u.OutputTokens += usage.OutputTokens
	u.CacheReadInputTokens += usage.CacheReadInputTokens
	u.CacheCreationInputTokens += usage.CacheCreationInputTokens
	u.WebSearchRequests += usage.ServerToolUse.WebSearchRequests
	u.MessageCount++

	// Cache writes bill differently per TTL, so they are tracked separately.
	// Records predating the split report only a total; attribute those to the
	// 5-minute TTL, which is the default.
	write5m, write1h := usage.CacheCreation.Ephemeral5m, usage.CacheCreation.Ephemeral1h
	if write5m == 0 && write1h == 0 {
		write5m = usage.CacheCreationInputTokens
	}
	u.CacheCreation5mTokens += write5m
	u.CacheCreation1hTokens += write1h

	a.messages++

	var toolCalls int
	for _, block := range rec.Message.Content {
		if block.Type == "tool_use" {
			toolCalls++
		}
	}

	ts, err := time.Parse(time.RFC3339Nano, rec.Timestamp)
	if err != nil {
		return
	}
	ts = ts.Local()

	if a.first.IsZero() || ts.Before(a.first) {
		a.first = ts
	}

	date := ts.Format("2006-01-02")
	day := a.days[date]
	if day == nil {
		day = &dayTotals{sessions: make(map[string]struct{})}
		a.days[date] = day
	}
	day.messages++
	day.toolCalls += toolCalls
	if rec.SessionID != "" {
		day.sessions[rec.SessionID] = struct{}{}
	}

	if rec.SessionID != "" {
		span := a.sessions[rec.SessionID]
		if span == nil {
			span = &sessionSpan{first: ts, last: ts}
			a.sessions[rec.SessionID] = span
		}
		if ts.Before(span.first) {
			span.first = ts
		}
		// Records arrive in order within a session, so a forward step is the
		// time since the previous message. Anything longer than idleGap was
		// the session sitting idle, not being worked on.
		if ts.After(span.last) {
			if gap := ts.Sub(span.last); gap < idleGap {
				span.active += gap
			}
			span.last = ts
		}
		span.messages++
	}
}

// result converts the accumulated maps into a UsageStats.
func (a *usageAccumulator) result() *UsageStats {
	stats := &UsageStats{
		ModelUsage:    make(map[string]ModelUsage, len(a.models)),
		TotalMessages: a.messages,
		TotalSessions: len(a.sessions),
	}

	for model, u := range a.models {
		u.CostUSD, u.CostKnown = modelCost(model, *u)
		stats.ModelUsage[model] = *u
	}

	for date, day := range a.days {
		stats.DailyActivity = append(stats.DailyActivity, DailyActivity{
			Date:          date,
			MessageCount:  day.messages,
			SessionCount:  len(day.sessions),
			ToolCallCount: day.toolCalls,
		})
	}
	sort.Slice(stats.DailyActivity, func(i, j int) bool {
		return stats.DailyActivity[i].Date > stats.DailyActivity[j].Date
	})

	// Rank by message count, not elapsed time: the session left open longest
	// is usually one that was idle, not one that did the most work.
	for id, span := range a.sessions {
		if span.messages <= stats.BusiestSession.MessageCount {
			continue
		}
		stats.BusiestSession = SessionSummary{
			SessionID:    id,
			ActiveMillis: span.active.Milliseconds(),
			MessageCount: span.messages,
			Timestamp:    span.first.Format(time.RFC3339),
		}
	}

	if !a.first.IsZero() {
		stats.FirstSessionDate = a.first.Format(time.RFC3339)
	}
	if len(stats.DailyActivity) > 0 {
		stats.LastActivityDate = stats.DailyActivity[0].Date
	}

	return stats
}

// scanTranscript folds one file's assistant records into the accumulator.
func scanTranscript(path string, acc *usageAccumulator) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()

	scanner := bufio.NewScanner(f)
	scanner.Buffer(make([]byte, 0, 256*1024), maxTranscriptLine)

	marker := []byte(assistantMarker)
	for scanner.Scan() {
		line := scanner.Bytes()
		// Most lines are user turns, attachments, or bookkeeping. A substring
		// test skips them without paying for a JSON parse.
		if !bytes.Contains(line, marker) {
			continue
		}
		var rec transcriptRecord
		if err := json.Unmarshal(line, &rec); err != nil {
			// A malformed line shouldn't discard the rest of the session.
			continue
		}
		if rec.Message.ID == "" && rec.UUID == "" {
			continue
		}
		acc.add(&rec)
	}

	return scanner.Err()
}

// loadTranscriptStats walks Claude Code's JSONL transcripts and aggregates
// token usage, cost, and daily activity.
//
// This replaces ~/.claude/stats-cache.json, which Claude Code no longer
// maintains. The transcripts also carry per-message timestamps and cache-TTL
// detail that the cache file never held.
func loadTranscriptStats() (*UsageStats, error) {
	root, err := transcriptDir()
	if err != nil {
		return nil, err
	}
	return loadTranscriptStatsFrom(root)
}

// loadTranscriptStatsFrom aggregates the transcripts under an explicit root.
func loadTranscriptStatsFrom(root string) (*UsageStats, error) {
	paths, err := findTranscripts(root)
	if err != nil {
		return nil, fmt.Errorf("cannot read %s: %w", root, err)
	}
	if len(paths) == 0 {
		return nil, fmt.Errorf("no usage transcripts found under %s\n\nMake sure Claude Code is installed and has been used at least once.", root)
	}

	fingerprint, err := transcriptFingerprint(paths)
	if err == nil {
		if cached := readStatsCache(fingerprint); cached != nil {
			return cached, nil
		}
	}

	acc := newUsageAccumulator()
	for _, path := range paths {
		if err := scanTranscript(path, acc); err != nil {
			return nil, fmt.Errorf("cannot read %s: %w", filepath.Base(path), err)
		}
	}

	stats := acc.result()
	if fingerprint != "" {
		writeStatsCache(fingerprint, stats)
	}
	return stats, nil
}

// ── Aggregate cache ─────────────────────────────────────────────────────────
//
// Re-reading every transcript on each refresh is wasteful when nothing has
// changed. The aggregate is cached against a fingerprint of the file set; any
// write to any transcript changes the fingerprint and forces a re-read.

type cachedStats struct {
	Fingerprint string      `json:"fingerprint"`
	Stats       *UsageStats `json:"stats"`
}

// statsSchemaVersion is folded into the fingerprint so a cache written by an
// older build is discarded rather than decoded into the current UsageStats,
// where removed fields would silently read as zero. Bump it whenever
// UsageStats or ModelUsage changes shape.
const statsSchemaVersion = 1

func transcriptFingerprint(paths []string) (string, error) {
	h := sha256.New()
	fmt.Fprintf(h, "schema:%d\n", statsSchemaVersion)
	for _, path := range paths {
		info, err := os.Stat(path)
		if err != nil {
			return "", err
		}
		fmt.Fprintf(h, "%s:%d:%d\n", path, info.Size(), info.ModTime().UnixNano())
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

func statsCachePath() (string, error) {
	dir, err := os.UserCacheDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "ClaudeUsage", "transcript-stats.json"), nil
}

func readStatsCache(fingerprint string) *UsageStats {
	path, err := statsCachePath()
	if err != nil {
		return nil
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return nil
	}
	var cached cachedStats
	if err := json.Unmarshal(data, &cached); err != nil {
		return nil
	}
	if cached.Fingerprint != fingerprint {
		return nil
	}
	return cached.Stats
}

func writeStatsCache(fingerprint string, stats *UsageStats) {
	path, err := statsCachePath()
	if err != nil {
		return
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return
	}
	data, err := json.Marshal(cachedStats{Fingerprint: fingerprint, Stats: stats})
	if err != nil {
		return
	}
	// A failed cache write only costs a re-read next time.
	_ = os.WriteFile(path, data, 0o644)
}
