package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

type DailyActivity struct {
	Date          string `json:"date"`
	MessageCount  int    `json:"messageCount"`
	SessionCount  int    `json:"sessionCount"`
	ToolCallCount int    `json:"toolCallCount"`
}

type DailyModelTokens struct {
	Date          string         `json:"date"`
	TokensByModel map[string]int `json:"tokensByModel"`
}

type ModelUsage struct {
	InputTokens              int     `json:"inputTokens"`
	OutputTokens             int     `json:"outputTokens"`
	CacheReadInputTokens     int     `json:"cacheReadInputTokens"`
	CacheCreationInputTokens int     `json:"cacheCreationInputTokens"`
	WebSearchRequests        int     `json:"webSearchRequests"`
	CostUSD                  float64 `json:"costUSD"`
}

type LongestSession struct {
	SessionID    string `json:"sessionId"`
	Duration     int64  `json:"duration"`
	MessageCount int    `json:"messageCount"`
	Timestamp    string `json:"timestamp"`
}

type StatsCache struct {
	Version          int                   `json:"version"`
	LastComputedDate string                `json:"lastComputedDate"`
	DailyActivity    []DailyActivity       `json:"dailyActivity"`
	DailyModelTokens []DailyModelTokens    `json:"dailyModelTokens"`
	ModelUsage       map[string]ModelUsage `json:"modelUsage"`
	TotalSessions    int                   `json:"totalSessions"`
	TotalMessages    int                   `json:"totalMessages"`
	LongestSession   LongestSession        `json:"longestSession"`
	FirstSessionDate string                `json:"firstSessionDate"`
	HourCounts       map[string]int        `json:"hourCounts"`
}

func loadStats() (*StatsCache, error) {
	home, err := os.UserHomeDir()
	if err != nil {
		return nil, fmt.Errorf("cannot find home directory: %w", err)
	}

	path := filepath.Join(home, ".claude", "stats-cache.json")
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("stats file not found at %s\n\nMake sure Claude Code is installed and has been used at least once.", path)
	}

	var stats StatsCache
	if err := json.Unmarshal(data, &stats); err != nil {
		return nil, fmt.Errorf("cannot parse stats file: %w", err)
	}

	// Sort daily activity by date, newest first
	sort.Slice(stats.DailyActivity, func(i, j int) bool {
		return stats.DailyActivity[i].Date > stats.DailyActivity[j].Date
	})

	return &stats, nil
}

func friendlyModelName(modelID string) string {
	id := strings.ToLower(modelID)
	switch {
	case strings.Contains(id, "opus-4-6"):
		return "Opus 4.6"
	case strings.Contains(id, "sonnet-4-6"):
		return "Sonnet 4.6"
	case strings.Contains(id, "haiku-4-5"):
		return "Haiku 4.5"
	case strings.Contains(id, "opus-4"):
		return "Opus 4"
	case strings.Contains(id, "sonnet-4-5"):
		return "Sonnet 4.5"
	case strings.Contains(id, "haiku-4"):
		return "Haiku 4"
	case strings.Contains(id, "opus-3-7"):
		return "Opus 3.7"
	case strings.Contains(id, "sonnet-3-7"):
		return "Sonnet 3.7"
	case strings.Contains(id, "sonnet-3-5"):
		return "Sonnet 3.5"
	case strings.Contains(id, "haiku-3-5"):
		return "Haiku 3.5"
	case strings.Contains(id, "opus-3"):
		return "Opus 3"
	case strings.Contains(id, "sonnet-3"):
		return "Sonnet 3"
	case strings.Contains(id, "haiku-3"):
		return "Haiku 3"
	default:
		return modelID
	}
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

// totalTokens sums all model usage into a single ModelUsage.
func totalTokens(usage map[string]ModelUsage) ModelUsage {
	var total ModelUsage
	for _, u := range usage {
		total.InputTokens += u.InputTokens
		total.OutputTokens += u.OutputTokens
		total.CacheReadInputTokens += u.CacheReadInputTokens
		total.CacheCreationInputTokens += u.CacheCreationInputTokens
		total.WebSearchRequests += u.WebSearchRequests
		total.CostUSD += u.CostUSD
	}
	return total
}
