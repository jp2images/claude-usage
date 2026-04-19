package main

import (
	"fmt"
	"sort"
	"time"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

const autoRefreshInterval = 60 * time.Second

func main() {
	a := app.NewWithID("com.jeffpatterson.claude-usage")
	a.Settings().SetTheme(theme.DefaultTheme())

	w := a.NewWindow("Claude Code Usage")
	w.Resize(fyne.NewSize(640, 580))

	var refresh func()
	refresh = func() {
		w.SetContent(buildContent(w, refresh))
	}

	refresh()

	// Auto-refresh in the background
	go func() {
		ticker := time.NewTicker(autoRefreshInterval)
		defer ticker.Stop()
		for range ticker.C {
			refresh()
		}
	}()

	w.ShowAndRun()
}

func buildContent(w fyne.Window, refresh func()) fyne.CanvasObject {
	stats, err := loadStats()
	if err != nil {
		return container.NewCenter(
			container.NewVBox(
				widget.NewLabelWithStyle("Unable to load Claude Code stats", fyne.TextAlignCenter, fyne.TextStyle{Bold: true}),
				widget.NewLabel(err.Error()),
				widget.NewButton("Retry", refresh),
			),
		)
	}
	return buildStatsContent(w, refresh, stats)
}

func buildStatsContent(_ fyne.Window, refresh func(), stats *StatsCache) fyne.CanvasObject {
	// ── Header ────────────────────────────────────────────────────────────────
	refreshBtn := widget.NewButton("↺ Refresh", refresh)
	titleLabel := widget.NewLabelWithStyle("Claude Code Usage", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
	header := container.NewBorder(nil, nil, nil, refreshBtn, titleLabel)

	// ── Overview ──────────────────────────────────────────────────────────────
	firstDate := "—"
	if stats.FirstSessionDate != "" {
		if t, err := time.Parse(time.RFC3339Nano, stats.FirstSessionDate); err == nil {
			firstDate = t.Format("Jan 2, 2006")
		}
	}

	totals := totalTokens(stats.ModelUsage)
	costStr := "—"
	if totals.CostUSD > 0 {
		costStr = fmt.Sprintf("$%.4f", totals.CostUSD)
	}

	overviewGrid := container.NewGridWithColumns(4,
		muted("Messages"), boldLabel(formatNumber(stats.TotalMessages)),
		muted("Sessions"), boldLabel(formatNumber(stats.TotalSessions)),
		muted("Total tokens"), boldLabel(formatTokens(totals.InputTokens+totals.OutputTokens)),
		muted("Est. cost"), boldLabel(costStr),
		muted("Active since"), boldLabel(firstDate),
		muted("Last updated"), boldLabel(formatDate(stats.LastComputedDate)),
	)
	overviewCard := widget.NewCard("Overview", "", overviewGrid)

	// ── Model usage table ─────────────────────────────────────────────────────
	type modelEntry struct {
		id    string
		usage ModelUsage
	}
	var models []modelEntry
	for id, u := range stats.ModelUsage {
		models = append(models, modelEntry{id, u})
	}
	sort.Slice(models, func(i, j int) bool {
		return models[i].id > models[j].id // newest model first
	})

	modelRows := []fyne.CanvasObject{
		container.NewGridWithColumns(5,
			boldLabel("Model"),
			trailingBold("Input"),
			trailingBold("Output"),
			trailingBold("Cache Reads"),
			trailingBold("Cost"),
		),
		widget.NewSeparator(),
	}
	for _, m := range models {
		mCostStr := "—"
		if m.usage.CostUSD > 0 {
			mCostStr = fmt.Sprintf("$%.4f", m.usage.CostUSD)
		}
		modelRows = append(modelRows, container.NewGridWithColumns(5,
			widget.NewLabel(friendlyModelName(m.id)),
			trailing(formatTokens(m.usage.InputTokens)),
			trailing(formatTokens(m.usage.OutputTokens)),
			trailing(formatTokens(m.usage.CacheReadInputTokens)),
			trailing(mCostStr),
		))
	}
	// Totals row
	modelRows = append(modelRows,
		widget.NewSeparator(),
		container.NewGridWithColumns(5,
			boldLabel("Total"),
			trailingBold(formatTokens(totals.InputTokens)),
			trailingBold(formatTokens(totals.OutputTokens)),
			trailingBold(formatTokens(totals.CacheReadInputTokens)),
			trailingBold(costStr),
		),
	)
	modelCard := widget.NewCard("Token Usage by Model", "", container.NewVBox(modelRows...))

	// ── Cache breakdown ───────────────────────────────────────────────────────
	cacheGrid := container.NewGridWithColumns(4,
		muted("Cache reads"), boldLabel(formatTokens(totals.CacheReadInputTokens)),
		muted("Cache writes"), boldLabel(formatTokens(totals.CacheCreationInputTokens)),
	)
	if totals.WebSearchRequests > 0 {
		cacheGrid = container.NewGridWithColumns(4,
			muted("Cache reads"), boldLabel(formatTokens(totals.CacheReadInputTokens)),
			muted("Cache writes"), boldLabel(formatTokens(totals.CacheCreationInputTokens)),
			muted("Web searches"), boldLabel(formatNumber(totals.WebSearchRequests)),
			widget.NewLabel(""), widget.NewLabel(""),
		)
	}
	cacheCard := widget.NewCard("Cache & Tools", "", cacheGrid)

	// ── Daily activity ────────────────────────────────────────────────────────
	days := stats.DailyActivity
	if len(days) > 10 {
		days = days[:10]
	}

	activityRows := []fyne.CanvasObject{
		container.NewGridWithColumns(4,
			boldLabel("Date"),
			trailingBold("Messages"),
			trailingBold("Sessions"),
			trailingBold("Tool Calls"),
		),
		widget.NewSeparator(),
	}
	for _, day := range days {
		activityRows = append(activityRows, container.NewGridWithColumns(4,
			widget.NewLabel(formatDate(day.Date)),
			trailing(formatNumber(day.MessageCount)),
			trailing(formatNumber(day.SessionCount)),
			trailing(formatNumber(day.ToolCallCount)),
		))
	}
	activityCard := widget.NewCard("Recent Activity", "", container.NewVBox(activityRows...))

	// ── Footer ────────────────────────────────────────────────────────────────
	longestStr := "—"
	if stats.LongestSession.MessageCount > 0 {
		longestStr = fmt.Sprintf("%s messages, %s",
			formatNumber(stats.LongestSession.MessageCount),
			formatDuration(stats.LongestSession.Duration),
		)
	}
	footer := container.NewHBox(
		muted("Longest session:"),
		widget.NewLabel(longestStr),
		layout.NewSpacer(),
	)

	// ── Scrollable layout ─────────────────────────────────────────────────────
	content := container.NewVBox(
		overviewCard,
		modelCard,
		cacheCard,
		activityCard,
		footer,
	)

	return container.NewBorder(
		container.NewVBox(header, widget.NewSeparator()),
		nil, nil, nil,
		container.NewVScroll(content),
	)
}

// ── Label helpers ─────────────────────────────────────────────────────────────

func boldLabel(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
}

func muted(text string) *widget.Label {
	// Fyne doesn't have a secondary color concept in the base widget,
	// so we just use a plain label; visually distinct from bold values.
	return widget.NewLabel(text)
}

func trailing(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignTrailing, fyne.TextStyle{})
}

func trailingBold(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignTrailing, fyne.TextStyle{Bold: true})
}
