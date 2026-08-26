package main

import (
	"fmt"
	"strings"
	"time"
)

type DailyActivity struct {
	Date          string `json:"date"`
	MessageCount  int    `json:"messageCount"`
	SessionCount  int    `json:"sessionCount"`
	ToolCallCount int    `json:"toolCallCount"`
}

type ModelUsage struct {
	InputTokens              int `json:"inputTokens"`
	OutputTokens             int `json:"outputTokens"`
	CacheReadInputTokens     int `json:"cacheReadInputTokens"`
	CacheCreationInputTokens int `json:"cacheCreationInputTokens"`
	// Cache writes are billed per TTL — 1.25x input at 5 minutes, 2x at one
	// hour — so the two are tracked apart. Their sum is
	// CacheCreationInputTokens.
	CacheCreation5mTokens int `json:"cacheCreation5mTokens"`
	CacheCreation1hTokens int `json:"cacheCreation1hTokens"`
	WebSearchRequests     int `json:"webSearchRequests"`
	MessageCount          int `json:"messageCount"`

	// CostUSD is computed from the token counts and this app's pricing table,
	// not reported by Claude. CostKnown is false for a model with no known
	// rate, such as one routed through a non-Anthropic provider.
	CostUSD   float64 `json:"costUsd"`
	CostKnown bool    `json:"costKnown"`
}

// SessionSummary describes a single session. ActiveMillis counts only the
// time between messages that arrived close together, so a session left open
// overnight doesn't report the idle hours as work.
type SessionSummary struct {
	SessionID    string `json:"sessionId"`
	ActiveMillis int64  `json:"activeMillis"`
	MessageCount int    `json:"messageCount"`
	Timestamp    string `json:"timestamp"`
}

// UsageStats is the aggregate the history window renders, built by walking
// Claude Code's JSONL transcripts.
type UsageStats struct {
	DailyActivity    []DailyActivity       `json:"dailyActivity"`
	ModelUsage       map[string]ModelUsage `json:"modelUsage"`
	TotalSessions    int                   `json:"totalSessions"`
	TotalMessages    int                   `json:"totalMessages"`
	BusiestSession   SessionSummary        `json:"busiestSession"`
	FirstSessionDate string                `json:"firstSessionDate"`
	LastActivityDate string                `json:"lastActivityDate"`
}

func loadStats() (*UsageStats, error) {
	return loadTranscriptStats()
}

// friendlyModelNames maps an ID substring to a display name. Order matters:
// the first match wins, so a more specific version precedes its family.
var friendlyModelNames = []struct{ match, name string }{
	{"fable-5", "Fable 5"},
	{"mythos-5", "Mythos 5"},
	{"opus-5", "Opus 5"},
	{"opus-4-8", "Opus 4.8"},
	{"opus-4-7", "Opus 4.7"},
	{"opus-4-6", "Opus 4.6"},
	{"opus-4-5", "Opus 4.5"},
	{"opus-4-1", "Opus 4.1"},
	{"opus-4", "Opus 4"},
	{"opus-3", "Opus 3"},
	{"sonnet-5", "Sonnet 5"},
	{"sonnet-4-6", "Sonnet 4.6"},
	{"sonnet-4-5", "Sonnet 4.5"},
	{"sonnet-4", "Sonnet 4"},
	{"sonnet-3-7", "Sonnet 3.7"},
	{"sonnet-3-5", "Sonnet 3.5"},
	{"sonnet-3", "Sonnet 3"},
	{"haiku-4-5", "Haiku 4.5"},
	{"haiku-4", "Haiku 4"},
	{"haiku-3-5", "Haiku 3.5"},
	{"haiku-3", "Haiku 3"},
}

func friendlyModelName(modelID string) string {
	id := strings.ToLower(modelID)
	for _, m := range friendlyModelNames {
		if strings.Contains(id, m.match) {
			return m.name
		}
	}
	return modelID
}

// formatTokens returns a compact human-readable token count.
func formatTokens(n int) string {
	switch {
	case n >= 1_000_000_000:
		return fmt.Sprintf("%.1fB", float64(n)/1_000_000_000)
	case n >= 1_000_000:
		return fmt.Sprintf("%.1fM", float64(n)/1_000_000)
	case n >= 1_000:
		return fmt.Sprintf("%.1fK", float64(n)/1_000)
	default:
		return fmt.Sprintf("%d", n)
	}
}

// formatNumber formats an integer with comma separators.
func formatNumber(n int) string {
	s := fmt.Sprintf("%d", n)
	if len(s) <= 3 {
		return s
	}
	var result []byte
	for i, ch := range s {
		pos := len(s) - i
		if i > 0 && pos%3 == 0 {
			result = append(result, ',')
		}
		result = append(result, byte(ch))
	}
	return string(result)
}

// formatCost renders a computed dollar amount. Cents-level precision is
// pointless below a cent and misleading above a dollar, so the scale varies.
func formatCost(usd float64) string {
	switch {
	case usd >= 100:
		return fmt.Sprintf("$%s", formatNumber(int(usd+0.5)))
	case usd >= 1:
		return fmt.Sprintf("$%.2f", usd)
	default:
		return fmt.Sprintf("$%.4f", usd)
	}
}

// formatDate formats a YYYY-MM-DD string to "Jan 2, 2006".
func formatDate(dateStr string) string {
	t, err := time.Parse("2006-01-02", dateStr)
	if err != nil {
		return dateStr
	}
	return t.Format("Jan 2, 2006")
}

// formatDuration formats a millisecond duration to "Xh Ym" or "Zm".
func formatDuration(ms int64) string {
	d := time.Duration(ms) * time.Millisecond
	h := int(d.Hours())
	m := int(d.Minutes()) % 60
	if h > 0 {
		return fmt.Sprintf("%dh %dm", h, m)
	}
	return fmt.Sprintf("%dm", m)
}

// totalTokens sums all model usage into a single ModelUsage. CostKnown on the
// result is true only when every model contributing tokens had a known rate,
// so a partial total is never shown as if it were complete.
func totalTokens(usage map[string]ModelUsage) ModelUsage {
	// An empty set has no known cost — reporting $0.00 for "no data yet"
	// reads as a real total. Rendered as a dash instead.
	total := ModelUsage{CostKnown: len(usage) > 0}
	for _, u := range usage {
		total.InputTokens += u.InputTokens
		total.OutputTokens += u.OutputTokens
		total.CacheReadInputTokens += u.CacheReadInputTokens
		total.CacheCreationInputTokens += u.CacheCreationInputTokens
		total.CacheCreation5mTokens += u.CacheCreation5mTokens
		total.CacheCreation1hTokens += u.CacheCreation1hTokens
		total.WebSearchRequests += u.WebSearchRequests
		total.MessageCount += u.MessageCount
		total.CostUSD += u.CostUSD
		if !u.CostKnown {
			total.CostKnown = false
		}
	}
	return total
}
