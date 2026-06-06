# Claude Usage — SwiftUI (native macOS)

A native macOS Claude Usage monitor built with **SwiftUI** and a Swift Package.
The truly-native macOS counterpart to the Go (`../go`) and cross-platform
Avalonia (`../avalonia`) apps.

## Build & run

Requires Xcode / the Swift toolchain (built with Swift 6).

```bash
cd swiftui/ClaudeUsage
swift run            # build and launch
swift build          # build only
```

Or open `Package.swift` in **Xcode** (File ▸ Open) or **Rider** and run from there.

> Run as an SPM executable it promotes its own activation policy (see
> `AppDelegate`) so the window shows and takes focus. For a distributable,
> signed/notarized `.app`, wrap it in an Xcode app target.

## How it gets data

| Source | Where | Code |
|--------|-------|------|
| Live plan usage | Claude desktop cookies → `claude.ai` API | `Cookies.swift`, `API.swift` |
| Usage history | `~/.claude/stats-cache.json` | `Stats.swift` |
| Service status | `status.claude.com` | `Status.swift` |

`Cookies.swift` is the macOS-specific seam: it reads "Claude Safe Storage" from
the Keychain via `/usr/bin/security`, derives the key with PBKDF2 (SHA-1, 1003
iterations, `saltysalt`) using **CommonCrypto**, and decrypts the `v10`
AES-128-CBC cookie values (fixed 16-space IV). Cookie rows are read straight from
the Chromium SQLite store via the system **SQLite3** module. This is a direct
port of `../go/api_darwin.go`.

Prerequisite: the **Claude desktop app installed and logged in** (it need not be
running — data is read at rest; only an expired session requires reopening it).

## Notes for study

- `@main` SwiftUI `App` with `WindowGroup` (main) + a second `Window(id:)` for
  history, opened via `@Environment(\.openWindow)`.
- State via `@StateObject`/`ObservableObject`; the loaders are nonisolated
  `async` so the blocking Keychain/SQLite work runs off the main actor.
- No third-party dependencies — only system frameworks (SwiftUI, Foundation,
  CommonCrypto, SQLite3).
