package main

import (
	"fmt"
	"sort"
	"time"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/widget"
)

func showDetailsWindow() {
	w := fyneApp.NewWindow("Claude Usage — History")
	w.Resize(fyne.NewSize(640, 560))

	var refresh func()
	refresh = func() {
		stats, err := loadStats()
		if err != nil {
			w.SetContent(container.NewCenter(
				container.NewVBox(
					widget.NewLabelWithStyle("Unable to load usage stats", fyne.TextAlignCenter, fyne.TextStyle{Bold: true}),
					widget.NewLabel(err.Error()),
					widget.NewButton("Retry", refresh),
				),
			))
			return
		}
		w.SetContent(buildStatsContent(refresh, stats))
	}

	refresh()
	w.Show()
}

func buildStatsContent(refresh func(), stats *StatsCache) fyne.CanvasObject {
	// ── Header ────────────────────────────────────────────────────────────────
	refreshBtn := widget.NewButton("↺ Refresh", refresh)
	titleLabel := widget.NewLabelWithStyle("Claude Usage History", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
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
		dMuted("Messages"), dBold(formatNumber(stats.TotalMessages)),
		dMuted("Sessions"), dBold(formatNumber(stats.TotalSessions)),
		dMuted("Total tokens"), dBold(formatTokens(totals.InputTokens+totals.OutputTokens)),
		dMuted("Est. cost"), dBold(costStr),
		dMuted("Active since"), dBold(firstDate),
		dMuted("Last updated"), dBold(formatDate(stats.LastComputedDate)),
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
		return models[i].id > models[j].id
	})

	modelRows := []fyne.CanvasObject{
		container.NewGridWithColumns(5,
			dBold("Model"),
			dTrailingBold("Input"),
			dTrailingBold("Output"),
			dTrailingBold("Cache Reads"),
			dTrailingBold("Cost"),
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
			dTrailing(formatTokens(m.usage.InputTokens)),
			dTrailing(formatTokens(m.usage.OutputTokens)),
			dTrailing(formatTokens(m.usage.CacheReadInputTokens)),
			dTrailing(mCostStr),
		))
	}
	modelRows = append(modelRows,
		widget.NewSeparator(),
		container.NewGridWithColumns(5,
			dBold("Total"),
			dTrailingBold(formatTokens(totals.InputTokens)),
			dTrailingBold(formatTokens(totals.OutputTokens)),
			dTrailingBold(formatTokens(totals.CacheReadInputTokens)),
			dTrailingBold(costStr),
		),
	)
	modelCard := widget.NewCard("Token Usage by Model", "", container.NewVBox(modelRows...))

	// ── Cache breakdown ───────────────────────────────────────────────────────
	cacheGrid := container.NewGridWithColumns(4,
		dMuted("Cache reads"), dBold(formatTokens(totals.CacheReadInputTokens)),
		dMuted("Cache writes"), dBold(formatTokens(totals.CacheCreationInputTokens)),
	)
	if totals.WebSearchRequests > 0 {
		cacheGrid = container.NewGridWithColumns(4,
			dMuted("Cache reads"), dBold(formatTokens(totals.CacheReadInputTokens)),
			dMuted("Cache writes"), dBold(formatTokens(totals.CacheCreationInputTokens)),
			dMuted("Web searches"), dBold(formatNumber(totals.WebSearchRequests)),
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
			dBold("Date"),
			dTrailingBold("Messages"),
			dTrailingBold("Sessions"),
			dTrailingBold("Tool Calls"),
		),
		widget.NewSeparator(),
	}
	for _, day := range days {
		activityRows = append(activityRows, container.NewGridWithColumns(4,
			widget.NewLabel(formatDate(day.Date)),
			dTrailing(formatNumber(day.MessageCount)),
			dTrailing(formatNumber(day.SessionCount)),
			dTrailing(formatNumber(day.ToolCallCount)),
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
		dMuted("Longest session:"),
		widget.NewLabel(longestStr),
		layout.NewSpacer(),
	)

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

func dBold(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
}

func dMuted(text string) *widget.Label {
	return widget.NewLabel(text)
}

func dTrailing(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignTrailing, fyne.TextStyle{})
}

func dTrailingBold(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignTrailing, fyne.TextStyle{Bold: true})
}
