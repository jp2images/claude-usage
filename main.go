package main

import (
	"fmt"
	"time"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

const autoRefreshInterval = 60 * time.Second

var fyneApp fyne.App

func main() {
	fyneApp = app.NewWithID("com.jeffpatterson.claude-usage")
	fyneApp.Settings().SetTheme(theme.DefaultTheme())

	w := fyneApp.NewWindow("Claude Usage")
	w.Resize(fyne.NewSize(320, 380))

	var refresh func()
	refresh = func() {
		w.SetContent(buildContent(refresh))
	}

	refresh()

	go func() {
		ticker := time.NewTicker(autoRefreshInterval)
		defer ticker.Stop()
		for range ticker.C {
			refresh()
		}
	}()

	w.ShowAndRun()
}

func buildContent(refresh func()) fyne.CanvasObject {
	usage, limits, err := loadPlanUsage()

	// ── Header ────────────────────────────────────────────────────────────────
	planLabel := ""
	if limits != nil {
		planLabel = friendlyTierName(limits.RateLimitTier)
	}
	titleLabel := widget.NewLabelWithStyle("Claude Usage", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
	planTierLabel := widget.NewLabel(planLabel)
	refreshBtn := widget.NewButton("↺", refresh)
	header := container.NewBorder(nil, nil, nil, refreshBtn,
		container.NewHBox(titleLabel, layout.NewSpacer(), planTierLabel),
	)

	if err != nil {
		return container.NewVBox(
			header,
			widget.NewSeparator(),
			widget.NewLabel("⚠  "+err.Error()),
		)
	}

	// ── Build sections ────────────────────────────────────────────────────────
	items := []fyne.CanvasObject{
		header,
		widget.NewSeparator(),
		sectionLabel("Current Session"),
	}
	items = append(items, buildSessionRows(usage)...)
	items = append(items, widget.NewSeparator())
	items = append(items, sectionLabel("Weekly Limits"))
	items = append(items, buildWeeklyRows(usage)...)

	extraRows := buildExtraRows(usage)
	items = append(items, widget.NewSeparator())
	items = append(items, sectionLabel("Extra Usage"))
	items = append(items, extraRows...)

	items = append(items, widget.NewSeparator())
	items = append(items, container.NewCenter(
		widget.NewButton("Usage History…", func() { showDetailsWindow() }),
	))

	return container.NewVBox(items...)
}

// ── Section builders ───────────────────────────────────────────────────────────

func buildSessionRows(usage *PlanUsage) []fyne.CanvasObject {
	p := usage.FiveHour
	if p == nil {
		return []fyne.CanvasObject{widget.NewLabel("No data")}
	}
	rows := []fyne.CanvasObject{
		usageBar("", p.Utilization),
		widget.NewLabel(timeUntil(p.ResetsAt)),
	}
	return rows
}

func buildWeeklyRows(usage *PlanUsage) []fyne.CanvasObject {
	type modelRow struct {
		label string
		p     *UsagePeriod
	}
	models := []modelRow{
		{"All Models", usage.SevenDay},
		{"Sonnet", usage.SevenDaySonnet},
		{"Claude Design", usage.SevenDayOmelette},
		{"Opus", usage.SevenDayOpus},
	}

	var rows []fyne.CanvasObject
	for _, m := range models {
		if m.p == nil {
			continue
		}
		rows = append(rows, usageBar(m.label, m.p.Utilization))
	}

	// Single reset label (all weekly limits share the same reset time)
	if usage.SevenDay != nil {
		rows = append(rows, widget.NewLabel(resetDay(usage.SevenDay.ResetsAt)))
	}
	return rows
}

func buildExtraRows(usage *PlanUsage) []fyne.CanvasObject {
	eu := usage.ExtraUsage

	if eu.Utilization != nil {
		pct := *eu.Utilization
		bar := usageBar("", pct)
		info := ""
		if eu.UsedCredits != nil {
			info = fmt.Sprintf("$%.2f spent", *eu.UsedCredits)
		}
		return []fyne.CanvasObject{bar, widget.NewLabel(info)}
	}

	// No utilization data
	status := "Not enabled"
	if eu.IsEnabled {
		status = "Enabled — no usage yet"
	}
	return []fyne.CanvasObject{
		usageBar("", 0),
		widget.NewLabel(status),
	}
}

// ── Widgets ────────────────────────────────────────────────────────────────────

// usageBar returns a [label  ████░░░░  pct%] row.
func usageBar(label string, pct float64) fyne.CanvasObject {
	bar := widget.NewProgressBar()
	bar.SetValue(pct / 100.0)
	bar.TextFormatter = func() string { return "" }

	pctLabel := widget.NewLabelWithStyle(
		fmt.Sprintf("%.0f%%", pct),
		fyne.TextAlignTrailing,
		fyne.TextStyle{},
	)

	if label == "" {
		return container.NewBorder(nil, nil, nil, pctLabel, bar)
	}
	return container.NewBorder(nil, nil, widget.NewLabel(label), pctLabel, bar)
}

func sectionLabel(text string) *widget.Label {
	return widget.NewLabelWithStyle(text, fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
}

// ── Time helpers ───────────────────────────────────────────────────────────────

func timeUntil(resetsAt *string) string {
	if resetsAt == nil || *resetsAt == "" {
		return ""
	}
	t, err := parseResetTime(*resetsAt)
	if err != nil {
		return ""
	}
	d := time.Until(t)
	if d <= 0 {
		return "Resetting soon"
	}
	h := int(d.Hours())
	m := int(d.Minutes()) % 60
	if h > 0 {
		return fmt.Sprintf("Resets in %dh %dm", h, m)
	}
	return fmt.Sprintf("Resets in %dm", m)
}

func resetDay(resetsAt *string) string {
	if resetsAt == nil || *resetsAt == "" {
		return ""
	}
	t, err := parseResetTime(*resetsAt)
	if err != nil {
		return ""
	}
	return t.Local().Format("Resets Mon 3:04 PM")
}

func parseResetTime(s string) (time.Time, error) {
	if t, err := time.Parse(time.RFC3339Nano, s); err == nil {
		return t, nil
	}
	return time.Parse(time.RFC3339, s)
}

// ── Label helpers ──────────────────────────────────────────────────────────────

func friendlyTierName(tier string) string {
	switch tier {
	case "default_claude_max_5x":
		return "Max (5x)"
	case "default_claude_max_20x":
		return "Max (20x)"
	case "default_claude_pro":
		return "Pro"
	default:
		return tier
	}
}
