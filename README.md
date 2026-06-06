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
| macOS | Swift + SwiftUI | `swiftui/` | Planned |

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
