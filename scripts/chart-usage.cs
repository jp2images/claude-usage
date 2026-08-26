#!/usr/bin/env dotnet
// Render a stacked-bar SVG of daily output tokens by model family from
// exports/daily_by_model.csv (produced by export-usage-csv.sh).
//
// Usage: dotnet run scripts/chart-usage.cs [in.csv] [out.svg]

using System.Globalization;
using System.Text;

var input = args.Length > 0 ? args[0] : "exports/daily_by_model.csv";
var output = args.Length > 1 ? args[1] : "exports/usage-trend.svg";

// Model family -> colour. Iteration order fixes the stacking order, and the
// legend is derived from this too, so a family added here can't be counted
// into the data while silently missing from the chart.
var colors = new (string Family, string Hex)[]
{
    ("fable", "#e8833a"),
    ("opus", "#b45ad6"),
    ("sonnet", "#0a84ff"),
    ("haiku", "#28a745"),
    ("other", "#888888"),
};

// Checked in order, so a model matching two names lands in the first.
string[] familyNames = ["fable", "mythos", "opus", "sonnet", "haiku"];

// Not static, so it reads the one list above rather than repeating it.
string FamilyOf(string model)
{
    var m = model.ToLowerInvariant();
    foreach (var fam in familyNames)
    {
        if (m.Contains(fam, StringComparison.Ordinal))
        {
            // Mythos shares Fable's rate and colour.
            return fam == "mythos" ? "fable" : fam;
        }
    }
    return "other";
}

// date -> family -> output tokens
var data = new SortedDictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

foreach (var row in ReadCsv(input))
{
    if (!row.TryGetValue("date", out var date) ||
        !row.TryGetValue("model", out var model) ||
        !row.TryGetValue("output_tokens", out var tokensText))
    {
        continue;
    }
    if (!double.TryParse(tokensText, NumberStyles.Float, CultureInfo.InvariantCulture, out var tokens))
    {
        continue;
    }

    if (!data.TryGetValue(date, out var byFamily))
    {
        byFamily = [];
        data[date] = byFamily;
    }
    var family = FamilyOf(model);
    byFamily[family] = byFamily.GetValueOrDefault(family) + tokens;
}

var dates = data.Keys.ToList();
if (dates.Count == 0)
{
    Console.Error.WriteLine($"no data in {input}");
    return 1;
}

var families = colors
    .Where(c => dates.Any(d => data[d].GetValueOrDefault(c.Family) > 0))
    .ToList();

// ── layout ───────────────────────────────────────────────────────────────────
const int W = 1100, H = 480;
const int ML = 70, MR = 20, MT = 40, MB = 90;
const double PlotW = W - ML - MR, PlotH = H - MT - MB;

var gap = PlotW / dates.Count;
var barW = gap * 0.8;

var maxTotal = dates.Max(d => data[d].Values.Sum());
if (maxTotal <= 0) maxTotal = 1;

// Round the axis up to a nice number.
var step = Math.Pow(10, Math.Floor(Math.Log10(maxTotal)));
var axisMax = Math.Ceiling(maxTotal / step) * step;

double Y(double v) => MT + PlotH - (v / axisMax) * PlotH;

static string Human(double v)
{
    foreach (var (unit, div) in ((string, double)[])[("B", 1e9), ("M", 1e6), ("K", 1e3)])
    {
        if (v >= div) return F(v / div, "0.0") + unit;
    }
    return ((long)v).ToString(CultureInfo.InvariantCulture);
}

// Every number written into the SVG goes through here. A locale that uses a
// comma as the decimal separator would otherwise emit coordinates the renderer
// silently rejects.
static string F(double v, string format = "0.0") =>
    v.ToString(format, CultureInfo.InvariantCulture);

var svg = new StringBuilder();
svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" font-family="-apple-system,Helvetica,Arial,sans-serif">""");
svg.AppendLine($"""<rect width="{W}" height="{H}" fill="white"/>""");
svg.AppendLine($"""<text x="{ML}" y="24" font-size="16" font-weight="bold">Claude Code — daily output tokens by model</text>""");

const int Ticks = 5;
for (var i = 0; i <= Ticks; i++)
{
    var val = axisMax * i / Ticks;
    var yy = Y(val);
    svg.AppendLine($"""<line x1="{ML}" y1="{F(yy)}" x2="{W - MR}" y2="{F(yy)}" stroke="#eee"/>""");
    svg.AppendLine($"""<text x="{ML - 8}" y="{F(yy + 4)}" font-size="11" fill="#666" text-anchor="end">{Human(val)}</text>""");
}

for (var i = 0; i < dates.Count; i++)
{
    var date = dates[i];
    var x = ML + i * gap + (gap - barW) / 2;
    var cursor = Y(0);

    foreach (var (family, hex) in families)
    {
        var v = data[date].GetValueOrDefault(family);
        if (v <= 0) continue;
        var h = (v / axisMax) * PlotH;
        cursor -= h;
        svg.AppendLine($"""<rect x="{F(x)}" y="{F(cursor)}" width="{F(barW)}" height="{F(h)}" fill="{hex}"/>""");
    }

    // Rotated x label, thinned out once the bars get crowded.
    if (dates.Count <= 40 || i % 2 == 0)
    {
        var lx = x + barW / 2;
        var ly = MT + PlotH + 14;
        var label = date.Length > 5 ? date[5..] : date;
        svg.AppendLine($"""<text x="{F(lx)}" y="{F(ly)}" font-size="9" fill="#666" text-anchor="end" transform="rotate(-60 {F(lx)} {F(ly)})">{label}</text>""");
    }
}

var legendX = ML;
const int LegendY = H - 24;
foreach (var (family, hex) in families)
{
    svg.AppendLine($"""<rect x="{legendX}" y="{LegendY - 10}" width="12" height="12" fill="{hex}"/>""");
    svg.AppendLine($"""<text x="{legendX + 18}" y="{LegendY}" font-size="12" fill="#333">{family}</text>""");
    legendX += 90;
}

svg.Append("</svg>");

File.WriteAllText(output, svg.ToString());
Console.WriteLine($"wrote {output} ({dates.Count} days, families: {string.Join(", ", families.Select(f => f.Family))})");
return 0;

// Minimal RFC 4180 reader: the export quotes its string columns, so bare
// splitting on commas would break on any quoted field.
static IEnumerable<Dictionary<string, string>> ReadCsv(string path)
{
    using var reader = new StreamReader(path);
    var header = reader.ReadLine();
    if (header is null) yield break;

    var columns = SplitCsvLine(header);
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (line.Length == 0) continue;
        var fields = SplitCsvLine(line);
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count && i < fields.Count; i++)
        {
            row[columns[i]] = fields[i];
        }
        yield return row;
    }
}

static List<string> SplitCsvLine(string line)
{
    var fields = new List<string>();
    var field = new StringBuilder();
    var quoted = false;

    for (var i = 0; i < line.Length; i++)
    {
        var c = line[i];
        if (quoted)
        {
            if (c == '"')
            {
                // A doubled quote inside a quoted field is a literal quote.
                if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else field.Append(c);
        }
        else if (c == '"') quoted = true;
        else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
        else field.Append(c);
    }

    fields.Add(field.ToString());
    return fields;
}
