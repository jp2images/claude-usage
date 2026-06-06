using System.IO;
using Microsoft.Data.Sqlite;

namespace ClaudeUsage.Services;

/// Shared Chromium cookie-DB reading. The SQLite schema is identical across
/// platforms; only the per-value decryption differs, so callers pass a decrypt
/// delegate.
internal static class CookieStore
{
    public static IEnumerable<(string Name, string Value)> ReadClaudeCookies(
        string cookiePath, Func<byte[], string> decrypt)
    {
        // The running app holds a lock on the live DB; copy it first so we can
        // open read-only without contending (mirrors the Go immutable=1 open).
        var tempDb = Path.Combine(Path.GetTempPath(), $"claude-cookies-{Guid.NewGuid():N}.db");
        File.Copy(cookiePath, tempDb, overwrite: true);

        var results = new List<(string, string)>();
        try
        {
            using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT name, encrypted_value
                FROM cookies
                WHERE host_key LIKE '%claude.ai%'
                  AND name IN ('sessionKey', 'lastActiveOrg')
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var encrypted = (byte[])reader["encrypted_value"];
                results.Add((name, decrypt(encrypted)));
            }
        }
        finally
        {
            try { File.Delete(tempDb); } catch { /* best effort cleanup */ }
        }

        return results;
    }
}
