# Claude Usage — Avalonia (cross-platform C#)

A native Claude Usage monitor built with **Avalonia UI on .NET 8**. One C#
codebase that builds and runs on **Windows and macOS** (and Linux), developed in
**Rider** or Visual Studio.

## Why Avalonia (vs. WPF)

WPF only builds on `net*-windows` and won't compile on a Mac. Avalonia targets
plain `net8.0`, so the same project builds unchanged on both machines — you can
develop and test on macOS in Rider and ship the identical code to the Windows
work box, where a normal managed .NET app sidesteps the EDR false positives that
blocked the Go binary. See [`../docs/code-signing-notes.md`](../docs/code-signing-notes.md).

## Build & run

Requires the [.NET 8+ SDK](https://dotnet.microsoft.com/download).

```bash
cd avalonia/ClaudeUsage
dotnet run
```

No per-platform flags. A runtime identifier (`-r win-x64`, `-r osx-arm64`) is only
needed when you **publish** a self-contained artifact:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## How it gets data

| Source | Where | Code |
|--------|-------|------|
| Live plan usage | Claude desktop cookies → `claude.ai` API | `Services/ClaudeCookies.cs` + `Windows/MacCookieReader.cs`, `Services/UsageApi.cs` |
| Usage history | `~/.claude/stats-cache.json` | `Services/StatsRepository.cs` |
| Service status | `status.claude.com` | `Services/ServiceStatusClient.cs` |

### The one OS-divergent piece
`ClaudeCookies.Read()` dispatches at runtime:

- **Windows** (`WindowsCookieReader`): DPAPI-unwrap the master key from
  `%APPDATA%\Claude\Local State`, then AES-256-**GCM** (`v10`/`v11` cookies).
- **macOS** (`MacCookieReader`): read "Claude Safe Storage" from the Keychain via
  the `security` tool, PBKDF2 (SHA-1, 1003 iters, `saltysalt`) → 16-byte key, then
  AES-128-**CBC** with a fixed 16-space IV.

The shared SQLite read lives in `Services/CookieStore.cs`; everything else (API
client, models, stats, formatting) is platform-agnostic.

Prerequisite: the **Claude desktop app must be installed and logged in**.

## Status

Builds clean and runs on macOS (verified). The Windows cookie path is implemented
but should be smoke-tested on Windows — the Electron cookie locations
(`%APPDATA%\Claude\...`, `Network\Cookies` vs root) are the first thing to check
if the session read fails; that logic is isolated to `WindowsCookieReader.cs`.
