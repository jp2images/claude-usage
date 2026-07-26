# Claude Usage

A small desktop monitor for your Claude usage — current session, weekly limits,
extra-credit balance, service status, and usage history — pulled from the Claude
desktop app's session and Claude Code's local stats.

This repo holds **three native implementations** that share the same data sources
and API shapes but use each platform's native UI toolkit:

| Platform | Toolkit | Location | Status |
|----------|---------|----------|--------|
| macOS | Go + Fyne | [`go/`](go) | Original, working |
| Windows + macOS | C# + Avalonia (.NET 8) | [`avalonia/`](avalonia) | Builds & runs on macOS (verified); Windows path to smoke-test |
| macOS | Swift + SwiftUI | [`swiftui/`](swiftui) | Builds & runs on macOS (verified) |

## Why multiple codebases instead of one cross-platform binary

The Go/Fyne build is statically linked, large, and unsigned — which trips EDR
heuristics on locked-down corporate machines (e.g. CrowdStrike flagging it with a
misleading "architecture" error; see
[`docs/code-signing-notes.md`](docs/code-signing-notes.md)). Rather than fight
allowlisting for a fast-changing binary, the work machine runs a native managed
.NET app (Avalonia) built with a trusted toolchain, which sidesteps the false
positives. Avalonia is cross-platform, so that same C# project also runs on macOS
for development in Rider. A separate SwiftUI app is planned as a truly native
macOS experience.

The cost is deliberate: the only logic that meaningfully diverges per platform is
the Claude desktop **cookie decryption** (Keychain + AES-CBC on macOS vs. DPAPI +
AES-GCM on Windows). The API client, JSON models, and stats reader are
near-identical across implementations.

## Shared data sources

- **Live plan usage** — read the Claude desktop app's `sessionKey`/`lastActiveOrg`
  cookies, then call `claude.ai/api/organizations/{id}/usage` and `/rate_limits`.
- **Usage history** — read `~/.claude/stats-cache.json` (written by Claude Code).
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
| `go/Claude Usage.app/Contents/Resources/Assets.car` | macOS 26 — the light/dark/tinted icon, referenced by `CFBundleIconName` |
| `go/Claude Usage.app/Contents/Resources/ClaudeUsage.icns` | macOS before 26, via `CFBundleIconFile` |
| `go/Icon.png` | input image for `fyne package` |
| `avalonia/ClaudeUsage/Assets/ClaudeUsage.ico` | Windows `.exe` icon (`ApplicationIcon`) and both Avalonia windows |
| `swiftui/…/Sources/ClaudeUsage/Resources/ClaudeUsage.icns` | Dock icon, set at launch — an SPM executable has no bundle to read it from |

The script compiles the document with Xcode's `actool`, so the raster copies
match what Icon Composer previews. `actool` caps the `.icns` at 256px because
macOS 26 reads full-resolution art from `Assets.car`; 256px is also the largest
size a Windows `.ico` carries, so the Windows copy loses nothing.

`icon-layers/` holds the flat SVG variants of the same mark, for README images
and favicons.
