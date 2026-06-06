namespace ClaudeUsage.Services;

/// Front door for reading the Claude desktop app's session cookies. Dispatches
/// to the platform-specific implementation at runtime — this is the one place
/// the cross-platform app genuinely diverges per OS.
public static class ClaudeCookies
{
    public sealed record Cookies(string SessionKey, string OrgId);

    public static Cookies Read()
    {
        if (OperatingSystem.IsWindows())
            return WindowsCookieReader.Read();
        if (OperatingSystem.IsMacOS())
            return MacCookieReader.Read();

        throw new PlatformNotSupportedException(
            "Reading Claude desktop cookies is only implemented for Windows and macOS.");
    }
}
