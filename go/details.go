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

// costLabel renders a computed cost, or a dash when a contributing model has
// no known rate. Cost is derived from token counts here, not reported by
// Claude, so it is labelled an estimate wherever it appears.
func costLabel(u ModelUsage) string {
	if !u.CostKnown {
		return "—"
	}
	return formatCost(u.CostUSD)
}

func buildStatsContent(refresh func(), stats *UsageStats) fyne.CanvasObject {
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
	costStr := costLabel(totals)

	overviewGrid := container.NewGridWithColumns(4,
		dMuted("Messages"), dBold(formatNumber(stats.TotalMessages)),
		dMuted("Sessions"), dBold(formatNumber(stats.TotalSessions)),
		dMuted("Total tokens"), dBold(formatTokens(totals.InputTokens+totals.OutputTokens)),
		dMuted("Est. cost"), dBold(costStr),
		dMuted("Active since"), dBold(firstDate),
		dMuted("Last activity"), dBold(formatDate(stats.LastActivityDate)),
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
		mCostStr := costLabel(m.usage)
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
	// The TTL split is shown because it drives cost: a cache write bills at
	// 2x the input rate on the 1-hour TTL against 1.25x on the 5-minute one.
	cachePairs := []fyne.CanvasObject{
		dMuted("Cache reads"), dBold(formatTokens(totals.CacheReadInputTokens)),
		dMuted("Cache writes"), dBold(formatTokens(totals.CacheCreationInputTokens)),
		dMuted("   5-min TTL"), dBold(formatTokens(totals.CacheCreation5mTokens)),
		dMuted("   1-hour TTL"), dBold(formatTokens(totals.CacheCreation1hTokens)),
	}
	if totals.WebSearchRequests > 0 {
		cachePairs = append(cachePairs,
			dMuted("Web searches"), dBold(formatNumber(totals.WebSearchRequests)))
	}
	// Pad out the last row so the two-pair grid stays aligned.
	if len(cachePairs)%4 != 0 {
		cachePairs = append(cachePairs, widget.NewLabel(""), widget.NewLabel(""))
	}
	cacheCard := widget.NewCard("Cache & Tools", "", container.NewGridWithColumns(4, cachePairs...))

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
	busiestStr := "—"
	if stats.BusiestSession.MessageCount > 0 {
		busiestStr = fmt.Sprintf("%s messages, %s active",
			formatNumber(stats.BusiestSession.MessageCount),
			formatDuration(stats.BusiestSession.ActiveMillis),
		)
	}
	footer := container.NewHBox(
		dMuted("Busiest session:"),
		widget.NewLabel(busiestStr),
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
