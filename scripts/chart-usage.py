#!/usr/bin/env python3
"""
Render a stacked-bar SVG of daily output tokens by model family from
exports/daily_by_model.csv (produced by export-usage-csv.sh).

No third-party dependencies — emits plain SVG you can open in any browser.

Usage: python3 scripts/chart-usage.py [exports/daily_by_model.csv] [exports/usage-trend.svg]
"""
import csv
import sys
from collections import defaultdict

IN = sys.argv[1] if len(sys.argv) > 1 else "exports/daily_by_model.csv"
OUT = sys.argv[2] if len(sys.argv) > 2 else "exports/usage-trend.svg"

# model family -> color
COLORS = {
    "opus": "#b45ad6",
    "sonnet": "#0a84ff",
    "haiku": "#28a745",
    "other": "#888888",
}


def family(model: str) -> str:
    m = model.lower()
    for fam in ("opus", "sonnet", "haiku"):
        if fam in m:
            return fam
    return "other"


# date -> family -> output tokens
data: dict[str, dict[str, float]] = defaultdict(lambda: defaultdict(float))
with open(IN, newline="") as f:
    for row in csv.DictReader(f):
        data[row["date"]][family(row["model"])] += float(row["output_tokens"])

dates = sorted(data)
if not dates:
    sys.exit("no data in " + IN)
families = [fam for fam in ("opus", "sonnet", "haiku", "other")
           if any(data[d].get(fam) for d in dates)]

# ── layout ────────────────────────────────────────────────────────────────────
W, H = 1100, 480
ML, MR, MT, MB = 70, 20, 40, 90
plot_w, plot_h = W - ML - MR, H - MT - MB
bar_w = plot_w / len(dates) * 0.8
gap = plot_w / len(dates)

max_total = max(sum(data[d].values()) for d in dates) or 1
# round axis up to a nice number
import math
step = 10 ** math.floor(math.log10(max_total))
axis_max = math.ceil(max_total / step) * step


def y(v):  # value -> svg y
    return MT + plot_h - (v / axis_max) * plot_h


def human(v):
    for unit, div in (("B", 1e9), ("M", 1e6), ("K", 1e3)):
        if v >= div:
            return f"{v/div:.1f}{unit}"
    return str(int(v))


svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" font-family="-apple-system,Helvetica,Arial,sans-serif">']
svg.append(f'<rect width="{W}" height="{H}" fill="white"/>')
svg.append(f'<text x="{ML}" y="24" font-size="16" font-weight="bold">Claude Code — daily output tokens by model</text>')

# y gridlines + labels
ticks = 5
for i in range(ticks + 1):
    val = axis_max * i / ticks
    yy = y(val)
    svg.append(f'<line x1="{ML}" y1="{yy:.1f}" x2="{W-MR}" y2="{yy:.1f}" stroke="#eee"/>')
    svg.append(f'<text x="{ML-8}" y="{yy+4:.1f}" font-size="11" fill="#666" text-anchor="end">{human(val)}</text>')

# bars
for i, d in enumerate(dates):
    x = ML + i * gap + (gap - bar_w) / 2
    y_cursor = y(0)
    for fam in families:
        v = data[d].get(fam, 0)
        if v <= 0:
            continue
        h = (v / axis_max) * plot_h
        y_cursor -= h
        svg.append(f'<rect x="{x:.1f}" y="{y_cursor:.1f}" width="{bar_w:.1f}" height="{h:.1f}" fill="{COLORS[fam]}"/>')
    # x label (rotated) every Nth to avoid crowding
    if len(dates) <= 40 or i % 2 == 0:
        lx = x + bar_w / 2
        svg.append(f'<text x="{lx:.1f}" y="{MT+plot_h+14:.1f}" font-size="9" fill="#666" text-anchor="end" transform="rotate(-60 {lx:.1f} {MT+plot_h+14:.1f})">{d[5:]}</text>')

# legend
lx = ML
ly = H - 24
for fam in families:
    svg.append(f'<rect x="{lx}" y="{ly-10}" width="12" height="12" fill="{COLORS[fam]}"/>')
    svg.append(f'<text x="{lx+18}" y="{ly}" font-size="12" fill="#333">{fam}</text>')
    lx += 90

svg.append("</svg>")

with open(OUT, "w") as f:
    f.write("\n".join(svg))
print(f"wrote {OUT} ({len(dates)} days, families: {', '.join(families)})")
