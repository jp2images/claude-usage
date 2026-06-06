using System.Diagnostics;

namespace ClaudeUsage.Services;

/// Opens a URL in the default browser on each desktop OS.
public static class UrlOpener
{
    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch
        {
            // Opening a browser is best-effort; never crash the app over it.
        }
    }
}
