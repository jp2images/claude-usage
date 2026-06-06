using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ClaudeUsage.Models;
using ClaudeUsage.Services;

namespace ClaudeUsage.Views;

/// The usage-history window. Counterpart to details.go.
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        RefreshButton.Click += (_, _) => Load();
        Opened += (_, _) => Load();
    }

    private void Load()
    {
        ContentPanel.Children.Clear();

        StatsCache stats;
        try
        {
            stats = StatsRepository.Load();
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

    private void BuildContent(StatsCache stats)
    {
        var totals = Formatting.TotalTokens(stats.ModelUsage);
        var costStr = totals.CostUSD > 0 ? $"${totals.CostUSD:F4}" : "—";

        // Overview
        ContentPanel.Children.Add(Card("Overview", KeyValueGrid(new (string, string)[]
        {
            ("Messages", Formatting.Number(stats.TotalMessages)),
            ("Sessions", Formatting.Number(stats.TotalSessions)),
            ("Total tokens", Formatting.Tokens(totals.InputTokens + totals.OutputTokens)),
            ("Est. cost", costStr),
            ("Active since", Formatting.LongDate(stats.FirstSessionDate)),
            ("Last updated", Formatting.Date(stats.LastComputedDate)),
        })));

        // Token usage by model
        var models = stats.ModelUsage
            .OrderByDescending(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var rows = new List<string[]>();
        foreach (var (id, u) in models)
        {
            var mCost = u.CostUSD > 0 ? $"${u.CostUSD:F4}" : "—";
            rows.Add(new[]
            {
                Formatting.FriendlyModel(id),
                Formatting.Tokens(u.InputTokens),
                Formatting.Tokens(u.OutputTokens),
                Formatting.Tokens(u.CacheReadInputTokens),
                mCost,
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

        // Cache & tools
        var cachePairs = new List<(string, string)>
        {
            ("Cache reads", Formatting.Tokens(totals.CacheReadInputTokens)),
            ("Cache writes", Formatting.Tokens(totals.CacheCreationInputTokens)),
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

        // Footer
        var longest = stats.LongestSession.MessageCount > 0
            ? $"{Formatting.Number(stats.LongestSession.MessageCount)} messages, {Formatting.Duration(stats.LongestSession.Duration)}"
            : "—";
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Spacing = 4,
        };
        footer.Children.Add(new TextBlock { Text = "Longest session:", Foreground = Palette.Muted });
        footer.Children.Add(new TextBlock { Text = longest });
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
