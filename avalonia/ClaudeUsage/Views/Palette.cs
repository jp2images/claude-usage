using Avalonia.Media;

namespace ClaudeUsage.Views;

/// Shared colors for the usage bars and muted text. Plain static brushes keep
/// the code-built UI simple (no resource-dictionary lookups).
internal static class Palette
{
    public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#0A84FF"));
    public static readonly IBrush Track = new SolidColorBrush(Color.Parse("#33808080"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#888888"));
    public static readonly IBrush Hairline = new SolidColorBrush(Color.Parse("#33808080"));
}
