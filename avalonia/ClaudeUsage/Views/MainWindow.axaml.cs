using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeUsage.Models;
using ClaudeUsage.Services;

namespace ClaudeUsage.Views;

/// The compact dashboard window. Counterpart to main.go's buildContent.
public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();

        StatusButton.Click += (_, _) => UrlOpener.Open("https://status.claude.com");
        RefreshButton.Click += async (_, _) => await RefreshAsync();

        _timer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Opened += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
    }

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;

        var statusTask = ServiceStatusClient.FetchAsync();
        try
        {
            UsageApi.Result result;
            try
            {
                result = await UsageApi.LoadAsync();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            BuildContent(result.Usage, result.Limits);
        }
        finally
        {
            await ApplyStatusAsync(statusTask);
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task ApplyStatusAsync(Task<ServiceStatus> statusTask)
    {
        var s = await statusTask;
        StatusDot.Foreground = Formatting.StatusBrush(s.Indicator);
        ToolTip.SetTip(StatusDot, s.Description);
    }

    private void BuildContent(PlanUsage usage, RateLimits limits)
    {
        PlanLabel.Text = Formatting.FriendlyTier(limits.RateLimitTier);
        ContentPanel.Children.Clear();

        if (usage.FiveHour is { } five)
            ContentPanel.Children.Add(
                BarRow("Current Session", Formatting.TimeUntil(five.ResetsAt), five.Utilization, boldLabel: true));
        else
            ContentPanel.Children.Add(new TextBlock { Text = "No session data" });

        ContentPanel.Children.Add(Hairline());
        ContentPanel.Children.Add(SectionLabel("Weekly Limits"));

        foreach (var (label, period) in new (string, UsagePeriod?)[]
                 {
                     ("All Models", usage.SevenDay),
                     ("Sonnet", usage.SevenDaySonnet),
                     ("Claude Design", usage.SevenDayOmelette),
                     ("Opus", usage.SevenDayOpus),
                 })
        {
            if (period is null) continue;
            ContentPanel.Children.Add(BarRow(label, Formatting.ResetDay(period.ResetsAt), period.Utilization));
        }

        ContentPanel.Children.Add(Hairline());
        ContentPanel.Children.Add(BuildExtraRow(usage.ExtraUsage));
        ContentPanel.Children.Add(Hairline());

        var historyButton = new Button
        {
            Content = "Usage History…",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(12, 4),
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        };
        historyButton.Click += (_, _) => new HistoryWindow().Show(this);
        ContentPanel.Children.Add(historyButton);
    }

    private static Control BuildExtraRow(ExtraUsage eu)
    {
        var pct = 0.0;
        var subtext = eu.IsEnabled ? "Enabled — no usage yet" : "Not enabled";
        if (eu.Utilization is { } util)
        {
            pct = util;
            subtext = eu.UsedCredits is { } credits ? $"${credits:F2} spent" : "";
        }
        return BarRow("Extra Usage", subtext, pct);
    }

    // ── Row builders ────────────────────────────────────────────────────────────

    private static Control BarRow(string label, string subtext, double pct, bool boldLabel = false)
    {
        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(0, 4),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
        };

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = boldLabel ? FontWeight.Bold : FontWeight.Normal,
        });
        if (!string.IsNullOrEmpty(subtext))
            left.Children.Add(new TextBlock { Text = subtext, FontSize = 11, Foreground = Palette.Muted });
        Grid.SetColumn(left, 0);

        var right = RightBarColumn(pct);
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Control RightBarColumn(double pct)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = $"{pct:F0}%",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Palette.Muted,
        });
        stack.Children.Add(ThinBar(pct));
        return stack;
    }

    /// A 3px rounded bar. Fill/track split expressed as star-weighted columns so
    /// it scales with width without measure callbacks.
    private static Control ThinBar(double pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        var grid = new Grid
        {
            Height = 3,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
            ColumnDefinitions = new ColumnDefinitions($"{pct.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{(100 - pct).ToString(System.Globalization.CultureInfo.InvariantCulture)}*"),
        };

        var fill = new Border { Background = Palette.Accent, CornerRadius = new Avalonia.CornerRadius(1.5) };
        Grid.SetColumn(fill, 0);
        var track = new Border { Background = Palette.Track, CornerRadius = new Avalonia.CornerRadius(1.5) };
        Grid.SetColumn(track, 1);

        grid.Children.Add(fill);
        grid.Children.Add(track);
        return grid;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Margin = new Avalonia.Thickness(0, 4, 0, 2),
    };

    private static Control Hairline() => new Border
    {
        Height = 1,
        Background = Palette.Hairline,
        Margin = new Avalonia.Thickness(0, 6),
    };

    private void ShowError(string message)
    {
        PlanLabel.Text = "";
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(new TextBlock
        {
            Text = "⚠  " + message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        });
    }
}
