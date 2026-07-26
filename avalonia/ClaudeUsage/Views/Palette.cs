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
}
