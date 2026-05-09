package main

import (
	"fmt"
	"image/color"
	"time"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

const autoRefreshInterval = 60 * time.Second

var fyneApp fyne.App

func main() {
	fyneApp = app.NewWithID("com.jeffpatterson.claude-usage")
	fyneApp.Settings().SetTheme(compactTheme{})

	w := fyneApp.NewWindow("Claude Usage")
	w.Resize(fyne.NewSize(420, 130))

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
		buildSessionRow(usage),
		widget.NewSeparator(),
		sectionLabel("Weekly Limits"),
	}
	items = append(items, buildWeeklyRows(usage)...)
	items = append(items, widget.NewSeparator())
	items = append(items, buildExtraRow(usage))
	items = append(items, widget.NewSeparator())
	items = append(items, container.NewCenter(
		widget.NewButton("Usage History…", func() { showDetailsWindow() }),
	))

	return container.NewVBox(items...)
}

// ── Section builders ───────────────────────────────────────────────────────────

// barRow lays out [label + subtext | thin bar + "X% used"] as a 2-column grid
// so all bars start from the same x position regardless of label length.
func barRow(label, subtext string, pct float64) fyne.CanvasObject {
	left := container.NewVBox(
		widget.NewLabel(label),
		widget.NewLabel(subtext),
	)
	right := rightBarCol(pct)
	return container.NewGridWithColumns(2, left, right)
}

func buildSessionRow(usage *PlanUsage) fyne.CanvasObject {
	p := usage.FiveHour
	if p == nil {
		return widget.NewLabel("No session data")
	}
	left := container.NewVBox(
		sectionLabel("Current Session"),
		widget.NewLabel(timeUntil(p.ResetsAt)),
	)
	return container.NewGridWithColumns(2, left, rightBarCol(p.Utilization))
}

func buildWeeklyRows(usage *PlanUsage) []fyne.CanvasObject {
	type modelEntry struct {
		label string
		p     *UsagePeriod
	}
	models := []modelEntry{
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
		rows = append(rows, barRow(m.label, resetDay(m.p.ResetsAt), m.p.Utilization))
	}
	return rows
}

func buildExtraRow(usage *PlanUsage) fyne.CanvasObject {
	eu := usage.ExtraUsage
	pct := 0.0
	subtext := "Not enabled"
	if eu.IsEnabled {
		subtext = "Enabled — no usage yet"
	}
	if eu.Utilization != nil {
		pct = *eu.Utilization
		subtext = ""
		if eu.UsedCredits != nil {
			subtext = fmt.Sprintf("$%.2f spent", *eu.UsedCredits)
		}
	}
	return barRow("Extra Usage", subtext, pct)
}

// ── Widgets ────────────────────────────────────────────────────────────────────

// rightBarCol returns a column with the percentage label on top and the thin
// bar below, stretching to fill the full column width.
func rightBarCol(pct float64) fyne.CanvasObject {
	pctLabel := widget.NewRichText(&widget.TextSegment{
		Text: fmt.Sprintf("%.0f%%", pct),
		Style: widget.RichTextStyle{
			SizeName:  theme.SizeNameCaptionText,
			Alignment: fyne.TextAlignTrailing,
			ColorName: theme.ColorNameForeground,
		},
	})
	return container.NewVBox(pctLabel, newThinBar(pct/100.0))
}

const thinBarHeight = float32(3)

// ── ThinBar widget ────────────────────────────────────────────────────────────

type ThinBar struct {
	widget.BaseWidget
	value float64 // 0.0–1.0
}

func newThinBar(value float64) *ThinBar {
	b := &ThinBar{value: value}
	b.ExtendBaseWidget(b)
	return b
}

func (b *ThinBar) CreateRenderer() fyne.WidgetRenderer {
	bg := canvas.NewRectangle(theme.Color(theme.ColorNameDisabledButton))
	bg.CornerRadius = thinBarHeight / 2
	fill := canvas.NewRectangle(theme.Color(theme.ColorNamePrimary))
	fill.CornerRadius = thinBarHeight / 2
	return &thinBarRenderer{bar: b, bg: bg, fill: fill}
}

type thinBarRenderer struct {
	bar  *ThinBar
	bg   *canvas.Rectangle
	fill *canvas.Rectangle
}

func (r *thinBarRenderer) MinSize() fyne.Size {
	return fyne.NewSize(0, thinBarHeight)
}

func (r *thinBarRenderer) Layout(size fyne.Size) {
	r.bg.Move(fyne.NewPos(0, 0))
	r.bg.Resize(size)
	r.fill.Move(fyne.NewPos(0, 0))
	r.fill.Resize(fyne.NewSize(size.Width*float32(r.bar.value), size.Height))
}

func (r *thinBarRenderer) Refresh() {
	r.bg.FillColor = theme.Color(theme.ColorNameDisabledButton)
	r.fill.FillColor = theme.Color(theme.ColorNamePrimary)
	r.bg.Refresh()
	r.fill.Refresh()
}

func (r *thinBarRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.fill}
}

func (r *thinBarRenderer) Destroy() {}

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

// ── Compact theme ─────────────────────────────────────────────────────────────
// Reduces inner and outer padding from 4dp to 2dp so rows pack tighter.

type compactTheme struct{}

func (compactTheme) Color(name fyne.ThemeColorName, variant fyne.ThemeVariant) color.Color {
	return theme.DefaultTheme().Color(name, variant)
}

func (compactTheme) Font(style fyne.TextStyle) fyne.Resource {
	return theme.DefaultTheme().Font(style)
}

func (compactTheme) Icon(name fyne.ThemeIconName) fyne.Resource {
	return theme.DefaultTheme().Icon(name)
}

func (compactTheme) Size(name fyne.ThemeSizeName) float32 {
	switch name {
	case theme.SizeNamePadding:
		return 2
	case theme.SizeNameInnerPadding:
		return 2
	default:
		return theme.DefaultTheme().Size(name)
	}
}
