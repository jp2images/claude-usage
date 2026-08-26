using Avalonia.Media;

namespace ClaudeUsage.Views;

/// Shared colors for the usage bars and muted text. Plain static brushes keep
/// the code-built UI simple (no resource-dictionary lookups).
internal static class Palette
{
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#0A84FF"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FFC107"));
    public static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#DC3545"));
    public static readonly IBrush Track = new SolidColorBrush(Color.Parse("#33808080"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#888888"));
    public static readonly IBrush Hairline = new SolidColorBrush(Color.Parse("#33808080"));

    /// Model-family colors. The hexes match scripts/chart-usage.cs so a family
    /// reads the same here as it does in the trend chart.
    public static readonly IBrush Fable = new SolidColorBrush(Color.Parse("#E8833A"));
    public static readonly IBrush Opus = new SolidColorBrush(Color.Parse("#B45AD6"));
    public static readonly IBrush Sonnet = new SolidColorBrush(Color.Parse("#0A84FF"));
    public static readonly IBrush Haiku = new SolidColorBrush(Color.Parse("#28A745"));
    public static readonly IBrush OtherModel = new SolidColorBrush(Color.Parse("#888888"));

    /// Table shading. Both are translucent greys/accents rather than fixed
    /// light colors, so they read as a tint over either theme's background.
    public static readonly IBrush RowStripe = new SolidColorBrush(Color.Parse("#0F808080"));
    public static readonly IBrush AccentTint = new SolidColorBrush(Color.Parse("#140A84FF"));

    /// Percentages of a limit at which the usage bars switch color.
    public const double WarningPercent = 80;
    public const double DangerPercent = 95;

    /// Accent below 80% used, yellow from 80%, red from 95%.
    public static IBrush BarBrush(double pct) => pct switch
    {
        >= DangerPercent => Danger,
        >= WarningPercent => Warning,
        _ => Accent,
    };

    /// Family brush for a model ID. Order matters: the first match wins, so a
    /// name that appears inside another is checked first. Mythos bills at
    /// Fable's rate and shares its color.
    public static IBrush ModelBrush(string modelId)
    {
        var id = modelId.ToLowerInvariant();
        return true switch
        {
            _ when id.Contains("fable") => Fable,
            _ when id.Contains("mythos") => Fable,
            _ when id.Contains("opus") => Opus,
            _ when id.Contains("sonnet") => Sonnet,
            _ when id.Contains("haiku") => Haiku,
            _ => OtherModel,
        };
    }
}
