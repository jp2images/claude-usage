package main

import (
	"strings"
	"testing"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
)

// labelTexts walks a rendered tree and collects every label's text, so a test
// can assert on what the window actually shows.
func labelTexts(obj fyne.CanvasObject) []string {
	var out []string
	switch o := obj.(type) {
	case *widget.Label:
		out = append(out, o.Text)
	case *fyne.Container:
		for _, child := range o.Objects {
			out = append(out, labelTexts(child)...)
		}
	case *widget.Card:
		out = append(out, o.Title)
		if o.Content != nil {
			out = append(out, labelTexts(o.Content)...)
		}
	case *container.Scroll:
		out = append(out, labelTexts(o.Content)...)
	}
	return out
}

func sampleStats() *UsageStats {
	return &UsageStats{
		DailyActivity: []DailyActivity{
			{Date: "2026-08-26", MessageCount: 76, SessionCount: 1, ToolCallCount: 17},
			{Date: "2026-08-25", MessageCount: 42, SessionCount: 2, ToolCallCount: 4},
		},
		ModelUsage: map[string]ModelUsage{
			"claude-opus-5": {
				InputTokens: 11201, OutputTokens: 2195440,
				CacheReadInputTokens: 883393229, CacheCreationInputTokens: 13173188,
				CacheCreation5mTokens: 500000, CacheCreation1hTokens: 12673188,
				MessageCount: 3868, CostUSD: 627.42, CostKnown: true,
			},
			"some-router/mystery": {
				InputTokens: 100, OutputTokens: 200, MessageCount: 2,
			},
		},
		TotalSessions:    53,
		TotalMessages:    3870,
		BusiestSession:   SessionSummary{MessageCount: 538, ActiveMillis: 31_260_000},
		FirstSessionDate: "2026-03-05T09:00:00Z",
		LastActivityDate: "2026-08-26",
	}
}

func TestHistoryWindowRenders(t *testing.T) {
	test.NewApp()

	content := buildStatsContent(func() {}, sampleStats())
	if content == nil {
		t.Fatal("buildStatsContent returned nil")
	}

	texts := strings.Join(labelTexts(content), "\n")

	for _, want := range []string{
		"Overview",
		"Token Usage by Model",
		"Cache & Tools",
		"Recent Activity",
		"Opus 5",           // friendly name, not the raw model id
		"Busiest session:", // replaces the old elapsed-time metric
		"5-min TTL",
		"1-hour TTL",
	} {
		if !strings.Contains(texts, want) {
			t.Errorf("history window is missing %q", want)
		}
	}

	// The total mixes a priced model with an unpriced one, so the overall
	// cost must read as unknown rather than as a confident partial sum.
	if !strings.Contains(texts, "—") {
		t.Error("expected a dash for the unknown total cost")
	}
	if strings.Contains(texts, "$0.0000") {
		t.Error("cost rendered as $0.0000; an unpriced model should show a dash")
	}
}

func TestHistoryWindowRendersEmptyStats(t *testing.T) {
	test.NewApp()

	// No transcripts yet: the window must still build rather than panic on
	// empty maps and a zero-valued session.
	content := buildStatsContent(func() {}, &UsageStats{ModelUsage: map[string]ModelUsage{}})
	if content == nil {
		t.Fatal("buildStatsContent returned nil for empty stats")
	}
}
