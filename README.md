# Claude Usage

A small desktop monitor for your Claude usage — current session, weekly limits,
extra-credit balance, service status, and usage history — pulled from the Claude
desktop app's session and Claude Code's local stats.

This repo holds two implementations that share the same data sources and API
shapes but use each platform's native UI toolkit:

| Platform | Toolkit | Location |
|----------|---------|----------|
| Windows, macOS, Linux | C# + Avalonia (.NET 8) | [`avalonia/`](avalonia) |
| macOS | Swift + SwiftUI | [`swiftui/`](swiftui) |

Avalonia is the one to reach for. SwiftUI is a native macOS build of the same
app.

## Why two codebases

A Go/Fyne build came first and has been retired — recover it with
`git checkout go-fyne-final -- go/`. It was blocked by CrowdStrike on a
corporate Windows machine: a statically linked, large, unsigned binary trips
several EDR heuristics at once, and the error surfaced as a misleading
complaint about the architecture. See
[`docs/code-signing-notes.md`](docs/code-signing-notes.md).

A framework-dependent .NET app built with the standard toolchain reads as a
normal managed app and avoids that class of false positive, so Avalonia became
the app that actually runs at work. It is cross-platform, so the same project
runs on macOS for development in Rider. SwiftUI stays because a native macOS
build is worth having on its own.

Releases publish framework-dependent for the same reason. A self-contained
single file would be the same shape that got the Go build blocked.

The only logic that meaningfully diverges per platform is the Claude desktop
**cookie decryption** — Keychain + AES-CBC on macOS against DPAPI + AES-GCM on
Windows. The API client, JSON models, and transcript reader are near-identical
across the two.

## Shared data sources

- **Live plan usage** — read the Claude desktop app's `sessionKey`/`lastActiveOrg`
  cookies, then call `claude.ai/api/organizations/{id}/usage` and `/rate_limits`.
- **Usage history** — walk Claude Code's JSONL transcripts under
  `~/.claude/projects`, deduplicate them, and aggregate tokens, cost, and daily
  activity. This replaced `~/.claude/stats-cache.json`, which Claude Code stopped
  writing; cost is computed from a local pricing table because the cache file
  never populated its `costUSD` field.
- **Service status** — the public `status.claude.com` summary.

All of it requires the **Claude desktop app installed and logged in**.

See each subdirectory's README for build and run instructions.

## App icon

`ClaudeUsage.icon` is the source of truth — an Icon Composer document (layered
SVGs plus the fills, gradients and materials in `icon.json`). Every platform's
icon is generated from it:

```bash
./scripts/build-icons.sh
```

| Output | Used by |
|--------|---------|
| `avalonia/ClaudeUsage/Assets/ClaudeUsage.ico` | Windows `.exe` icon (`ApplicationIcon`) and both Avalonia windows |
| `swiftui/…/Sources/ClaudeUsage/Resources/ClaudeUsage.icns` | Dock icon, set at launch — an SPM executable has no bundle to read it from |

The script compiles the document with Xcode's `actool`, so the raster copies
match what Icon Composer previews. `actool` caps the `.icns` at 256px because
macOS 26 reads full-resolution art from the `Assets.car` catalog; 256px is also
the largest size a Windows `.ico` carries, so the Windows copy loses nothing.

`actool` still emits that catalog, and it holds the light/dark/tinted icon macOS
26 renders. Nothing consumes it today — the retired Go build was the only app
here with a bundle to put it in, and an SPM executable has none — so the script
leaves it in its work directory rather than checking in a file no app reads.

`icon-layers/` holds the flat SVG variants of the same mark, for README images
and favicons.
