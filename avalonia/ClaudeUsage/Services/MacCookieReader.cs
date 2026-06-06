using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsage.Services;

/// macOS cookie reader: the Keychain holds "Claude Safe Storage" -> a password
/// run through PBKDF2 (SHA-1, 1003 iterations, "saltysalt") to derive a 16-byte
/// AES key, then cookies are AES-128-CBC with a fixed 16-space IV. This is a
/// straight port of go/api_darwin.go.
[SupportedOSPlatform("macos")]
internal static partial class MacCookieReader
{
    public static ClaudeCookies.Cookies Read()
    {
        var aesKey = DeriveKey(ReadKeychainPassword());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cookiePath = Path.Combine(home, "Library", "Application Support", "Claude", "Cookies");
        if (!File.Exists(cookiePath))
            cookiePath = Path.Combine(home, "Library", "Application Support", "Claude", "Network", "Cookies");
        if (!File.Exists(cookiePath))
            throw new InvalidOperationException(
                "Claude cookie store not found — is the Claude desktop app installed and logged in?");

        string? sessionKey = null, orgId = null;
        foreach (var (name, value) in CookieStore.ReadClaudeCookies(cookiePath, enc => DecryptCookie(enc, aesKey)))
        {
            switch (name)
            {
                case "sessionKey": sessionKey = value; break;
                case "lastActiveOrg": orgId = value; break;
            }
        }

        if (string.IsNullOrEmpty(sessionKey))
            throw new InvalidOperationException("session key not found — log in via the Claude desktop app");
        if (string.IsNullOrEmpty(orgId))
            throw new InvalidOperationException("organization ID not found in cookies");

        return new ClaudeCookies.Cookies(sessionKey, orgId);
    }

    private static string ReadKeychainPassword()
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add("Claude Safe Storage");
        psi.ArgumentList.Add("-a"); psi.ArgumentList.Add("Claude Key");
        psi.ArgumentList.Add("-w");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("could not launch the 'security' tool");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("keychain lookup failed (is Claude desktop installed?)");

        return output.Trim();
    }

    private static byte[] DeriveKey(string password) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            Encoding.UTF8.GetBytes("saltysalt"),
            iterations: 1003,
            HashAlgorithmName.SHA1,
            outputLength: 16);

    private static string DecryptCookie(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < 3 || Encoding.ASCII.GetString(encrypted, 0, 3) != "v10")
            return Encoding.UTF8.GetString(encrypted); // unencrypted

        var ciphertext = encrypted.AsSpan(3).ToArray();
        if (ciphertext.Length == 0 || ciphertext.Length % 16 != 0)
            throw new InvalidOperationException("invalid ciphertext length");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = Encoding.ASCII.GetBytes(new string(' ', 16));
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        // Some builds prefix a nonce; recover the value by pattern, as the Go code does.
        var text = Encoding.UTF8.GetString(plain);
        var skIdx = text.IndexOf("sk-ant-", StringComparison.Ordinal);
        if (skIdx >= 0)
            return text[skIdx..];

        var uuid = UuidRegex().Match(text);
        if (uuid.Success)
            return uuid.Value;

        return text.TrimEnd('\0');
    }

    [GeneratedRegex("[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}")]
    private static partial Regex UuidRegex();
}
