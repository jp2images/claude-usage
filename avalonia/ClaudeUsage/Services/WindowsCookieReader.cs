using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClaudeUsage.Services;

/// Windows cookie reader: DPAPI-wrapped master key + AES-256-GCM cookies
/// (the Chromium "v10"/"v11" scheme). Counterpart to the macOS Keychain path.
[SupportedOSPlatform("windows")]
internal static class WindowsCookieReader
{
    public static ClaudeCookies.Cookies Read()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = Path.Combine(appData, "Claude");
        var localStatePath = Path.Combine(baseDir, "Local State");

        // Newer Chromium stores cookies under Network/, older versions at the root.
        var cookiePath = Path.Combine(baseDir, "Network", "Cookies");
        if (!File.Exists(cookiePath))
            cookiePath = Path.Combine(baseDir, "Cookies");

        if (!File.Exists(localStatePath) || !File.Exists(cookiePath))
            throw new InvalidOperationException(
                "Claude desktop data not found — is the Claude desktop app installed and logged in?");

        var aesKey = ReadMasterKey(localStatePath);

        string? sessionKey = null, orgId = null;
        foreach (var (name, value) in CookieStore.ReadClaudeCookies(cookiePath, enc => DecryptValue(enc, aesKey)))
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

    private static byte[] ReadMasterKey(string localStatePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
        var encodedKey = doc.RootElement
            .GetProperty("os_crypt")
            .GetProperty("encrypted_key")
            .GetString()
            ?? throw new InvalidOperationException("os_crypt.encrypted_key missing from Local State");

        var blob = Convert.FromBase64String(encodedKey);

        const string dpapiPrefix = "DPAPI";
        if (blob.Length <= dpapiPrefix.Length ||
            Encoding.ASCII.GetString(blob, 0, dpapiPrefix.Length) != dpapiPrefix)
            throw new InvalidOperationException("unexpected encrypted_key format (missing DPAPI prefix)");

        var wrapped = blob[dpapiPrefix.Length..];
        return ProtectedData.Unprotect(wrapped, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static string DecryptValue(byte[] encrypted, byte[] key)
    {
        // v10 / v11: AES-256-GCM. Layout: [3-byte prefix][12 nonce][cipher][16 tag]
        if (encrypted.Length > 3 &&
            encrypted[0] == (byte)'v' && encrypted[1] == (byte)'1' &&
            (encrypted[2] == (byte)'0' || encrypted[2] == (byte)'1'))
        {
            const int nonceLen = 12, tagLen = 16;
            if (encrypted.Length < 3 + nonceLen + tagLen)
                throw new InvalidOperationException("cookie ciphertext too short");

            var nonce = encrypted.AsSpan(3, nonceLen);
            var cipherLen = encrypted.Length - 3 - nonceLen - tagLen;
            var cipher = encrypted.AsSpan(3 + nonceLen, cipherLen);
            var tag = encrypted.AsSpan(encrypted.Length - tagLen, tagLen);

            var plain = new byte[cipherLen];
            using var gcm = new AesGcm(key, tagLen);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }

        // Legacy (pre-v10) values are wrapped directly with DPAPI.
        var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
