using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClaudeUsage.Models;
using ClaudeUsage.Services;

namespace ClaudeUsage.Views;

/// The usage-history window. Counterpart to details.go.
public partial class HistoryWindow : Window
{
    private UsageStats? _stats;

    public HistoryWindow()
    {
        InitializeComponent();
        RefreshButton.Click += (_, _) => Load();
        ExportButton.Click += OnExport;
        Opened += (_, _) => Load();
    }

    private void Load()
    {
        ContentPanel.Children.Clear();

        UsageStats stats;
        try
        {
            stats = TranscriptReader.Load();
            _stats = stats;
        }
        catch (Exception ex)
        {
            var panel = new StackPanel
            {
                Margin = new Avalonia.Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
            };
            panel.Children.Add(new TextBlock
            {
                Text = "Unable to load usage stats",
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = ex.Message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            });
            var retry = new Button
            {
                Content = "Retry",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Avalonia.Thickness(12, 4),
            };
            retry.Click += (_, _) => Load();
            panel.Children.Add(retry);
            ContentPanel.Children.Add(panel);
            return;
        }

        BuildContent(stats);
    }

    /// Exports the dated activity table shown here. (For complete historical
    /// trends across all sessions, use scripts/export-usage-csv.sh.)
    private async void OnExport(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_stats is not { } stats) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "claude-usage-daily-activity.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(DailyActivityCsv(stats));
    }

    private static string DailyActivityCsv(UsageStats stats)
    {
        var sb = new StringBuilder("date,messages,sessions,tool_calls\n");
        foreach (var d in stats.DailyActivity.OrderBy(d => d.Date, StringComparer.Ordinal))
            sb.Append($"{d.Date},{d.MessageCount},{d.SessionCount},{d.ToolCallCount}\n");
        return sb.ToString();
    }

    private void BuildContent(UsageStats stats)
    {
        var totals = Formatting.TotalTokens(stats.ModelUsage);
        var costStr = Formatting.CostLabel(totals);

        // Overview
        ContentPanel.Children.Add(Card("Overview", KeyValueGrid(new (string, string)[]
        {
            ("Messages", Formatting.Number(stats.TotalMessages)),
            ("Sessions", Formatting.Number(stats.TotalSessions)),
            ("Total tokens", Formatting.Tokens(totals.InputTokens + totals.OutputTokens)),
            ("Est. cost", costStr),
            ("Active since", Formatting.LongDate(stats.FirstSessionDate)),
            ("Last activity", Formatting.Date(stats.LastActivityDate)),
        })));

        // Token usage by model
        var models = stats.ModelUsage
            .OrderByDescending(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var rows = new List<string[]>();
        foreach (var (id, u) in models)
        {
            rows.Add(new[]
            {
                Formatting.FriendlyModel(id),
                Formatting.Tokens(u.InputTokens),
                Formatting.Tokens(u.OutputTokens),
                Formatting.Tokens(u.CacheReadInputTokens),
                Formatting.CostLabel(u),
            });
        }
        rows.Add(new[]
        {
            "Total",
            Formatting.Tokens(totals.InputTokens),
            Formatting.Tokens(totals.OutputTokens),
            Formatting.Tokens(totals.CacheReadInputTokens),
            costStr,
        });
        ContentPanel.Children.Add(Card("Token Usage by Model", Table(
            new[] { "Model", "Input", "Output", "Cache Reads", "Cost" },
            rows,
            new[] { false, true, true, true, true },
            boldLastRow: true)));

        // Cache & tools. The TTL split is shown because it drives cost: a cache
        // write bills at 2x the input rate on the 1-hour TTL against 1.25x on
        // the 5-minute one.
        var cachePairs = new List<(string, string)>
        {
            ("Cache reads", Formatting.Tokens(totals.CacheReadInputTokens)),
            ("Cache writes", Formatting.Tokens(totals.CacheCreationInputTokens)),
            ("    5-min TTL", Formatting.Tokens(totals.CacheCreation5mTokens)),
            ("    1-hour TTL", Formatting.Tokens(totals.CacheCreation1hTokens)),
        };
        if (totals.WebSearchRequests > 0)
            cachePairs.Add(("Web searches", Formatting.Number(totals.WebSearchRequests)));
        ContentPanel.Children.Add(Card("Cache & Tools", KeyValueGrid(cachePairs)));

        // Recent activity
        var days = stats.DailyActivity.Take(10).ToList();
        var activityRows = days.Select(d => new[]
        {
            Formatting.Date(d.Date),
            Formatting.Number(d.MessageCount),
            Formatting.Number(d.SessionCount),
            Formatting.Number(d.ToolCallCount),
        }).ToList();
        ContentPanel.Children.Add(Card("Recent Activity", Table(
            new[] { "Date", "Messages", "Sessions", "Tool Calls" },
            activityRows,
            new[] { false, true, true, true },
            boldLastRow: false)));

        // Footer. Ranked by messages, not elapsed time: a session resumed days
        // later spans more wall clock than a busy one without doing more work.
        var busiest = stats.BusiestSession.MessageCount > 0
            ? $"{Formatting.Number(stats.BusiestSession.MessageCount)} messages, {Formatting.Duration(stats.BusiestSession.ActiveMillis)} active"
            : "—";
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Spacing = 4,
        };
        footer.Children.Add(new TextBlock { Text = "Busiest session:", Foreground = Palette.Muted });
        footer.Children.Add(new TextBlock { Text = busiest });
        ContentPanel.Children.Add(footer);
    }

    // ── Building blocks ─────────────────────────────────────────────────────────

    private static Border Card(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold });
        stack.Children.Add(content);

        return new Border
        {
            BorderBrush = Palette.Hairline,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12),
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
            Child = stack,
        };
    }

    private static Grid KeyValueGrid(IReadOnlyList<(string Label, string Value)> pairs)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

        for (var i = 0; i < pairs.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = pairs[i].Label, Foreground = Palette.Muted, Margin = new Avalonia.Thickness(0, 2, 16, 2) };
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);

            var value = new TextBlock { Text = pairs[i].Value, FontWeight = FontWeight.Bold, Margin = new Avalonia.Thickness(0, 2) };
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);

            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return grid;
    }

    private static Grid Table(string[] headers, List<string[]> rows, bool[] trailing, bool boldLastRow)
    {
        var grid = new Grid();
        for (var c = 0; c < headers.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c == 0 ? 1.4 : 1, GridUnitType.Star) });

        void AddRow(int r, string[] cells, bool bold)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < headers.Length; c++)
            {
                var tb = new TextBlock
                {
                    Text = cells[c],
                    FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                    TextAlignment = trailing[c] ? TextAlignment.Right : TextAlignment.Left,
                    Margin = new Avalonia.Thickness(0, 2, 8, 2),
                };
                Grid.SetRow(tb, r);
                Grid.SetColumn(tb, c);
                grid.Children.Add(tb);
            }
        }

        AddRow(0, headers, bold: true);
        for (var i = 0; i < rows.Count; i++)
            AddRow(i + 1, rows[i], bold: boldLastRow && i == rows.Count - 1);

        return grid;
    }
}
