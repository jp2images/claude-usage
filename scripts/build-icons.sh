#!/usr/bin/env bash
#
# Regenerate every platform's app icon from ClaudeUsage.icon, the Icon Composer
# document at the repo root. Run it after editing that document.
#
# Outputs:
#   avalonia/ClaudeUsage/Assets/ClaudeUsage.ico                         Windows .exe and window icon
#   swiftui/ClaudeUsage/Sources/ClaudeUsage/Resources/ClaudeUsage.icns  Dock icon for `swift run`
#
# Requires Xcode (actool, iconutil) and the .NET SDK (for the .ico writer).
#
set -euo pipefail

command -v dotnet >/dev/null || { echo "error: the .NET SDK is required (brew install dotnet)" >&2; exit 1; }

repo=$(cd "$(dirname "$0")/.." && pwd)
source_icon="$repo/ClaudeUsage.icon"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# 1. Compile the Icon Composer document. actool applies the layer fills,
#    gradients, shadows and glass material exactly as Icon Composer previews
#    them, then writes the macOS 26 asset catalog plus a legacy .icns.
#
#    The catalog (Assets.car) carries the light/dark/tinted icon macOS 26 reads
#    through CFBundleIconName. Nothing consumes it now that the Go/Fyne bundle
#    is gone — the SwiftUI executable is an SPM binary with no bundle to hold
#    it — so it is left in the work directory rather than checked in. See the
#    SwiftUI app-bundle issue; wiring it back is one cp.
xcrun actool "$source_icon" \
    --compile "$work" \
    --platform macosx \
    --minimum-deployment-target 26.0 \
    --app-icon ClaudeUsage \
    --include-all-app-icons \
    --output-partial-info-plist "$work/partial.plist" >/dev/null

# 2. macOS (SwiftUI). The SPM executable has no bundle, so AppDelegate loads
#    this .icns at launch and sets it as the Dock icon.
swift_resources="$repo/swiftui/ClaudeUsage/Sources/ClaudeUsage/Resources"
mkdir -p "$swift_resources"
cp "$work/ClaudeUsage.icns" "$swift_resources/"

# 3. The Windows raster copies come out of the compiled .icns, so the .exe icon
#    shows the same artwork macOS does. actool caps the .icns at 256px — macOS
#    26 reads full-resolution art from Assets.car — and 256px is also the
#    largest size a Windows .ico carries, so the conversion loses nothing.
iconutil -c iconset "$work/ClaudeUsage.icns" -o "$work/ClaudeUsage.iconset"

mkdir -p "$repo/avalonia/ClaudeUsage/Assets"
dotnet run "$repo/scripts/make-ico.cs" -- \
    "$work/ClaudeUsage.iconset" \
    "$repo/avalonia/ClaudeUsage/Assets/ClaudeUsage.ico"

echo "wrote $swift_resources/ClaudeUsage.icns"
