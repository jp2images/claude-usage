#!/usr/bin/env bash
#
# Regenerate every platform's app icon from ClaudeUsage.icon, the Icon Composer
# document at the repo root. Run it after editing that document.
#
# Outputs:
#   go/Claude Usage.app/Contents/Resources/Assets.car        macOS 26 icon (light/dark/tinted)
#   go/Claude Usage.app/Contents/Resources/ClaudeUsage.icns  macOS icon, pre-26 fallback
#   go/Icon.png                                              input image for `fyne package`
#   avalonia/ClaudeUsage/Assets/ClaudeUsage.ico              Windows .exe and window icon
#   swiftui/ClaudeUsage/Sources/ClaudeUsage/Resources/ClaudeUsage.icns  Dock icon for `swift run`
#
# Requires Xcode (actool, iconutil) and Python 3 with Pillow.
#
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
source_icon="$repo/ClaudeUsage.icon"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# 1. Compile the Icon Composer document. actool applies the layer fills,
#    gradients, shadows and glass material exactly as Icon Composer previews
#    them, then writes the macOS 26 asset catalog plus a legacy .icns.
xcrun actool "$source_icon" \
    --compile "$work" \
    --platform macosx \
    --minimum-deployment-target 26.0 \
    --app-icon ClaudeUsage \
    --include-all-app-icons \
    --output-partial-info-plist "$work/partial.plist" >/dev/null

# 2. macOS (Go/Fyne bundle). Info.plist names both: CFBundleIconName picks the
#    Assets.car icon on macOS 26, CFBundleIconFile the .icns everywhere else.
resources="$repo/go/Claude Usage.app/Contents/Resources"
mkdir -p "$resources"
cp "$work/Assets.car" "$work/ClaudeUsage.icns" "$resources/"

# 3. macOS (SwiftUI). The SPM executable has no bundle, so AppDelegate loads
#    this .icns at launch and sets it as the Dock icon.
swift_resources="$repo/swiftui/ClaudeUsage/Sources/ClaudeUsage/Resources"
mkdir -p "$swift_resources"
cp "$work/ClaudeUsage.icns" "$swift_resources/"

# 4. Raster copies come out of the compiled .icns, so Windows and Fyne show the
#    same artwork macOS does. actool caps the .icns at 256px — macOS 26 reads
#    full-resolution art from Assets.car — and 256px is also the largest size a
#    Windows .ico carries, so the conversion loses nothing.
iconutil -c iconset "$work/ClaudeUsage.icns" -o "$work/ClaudeUsage.iconset"
cp "$work/ClaudeUsage.iconset/icon_128x128@2x.png" "$repo/go/Icon.png"

mkdir -p "$repo/avalonia/ClaudeUsage/Assets"
python3 - "$work/ClaudeUsage.iconset" "$repo/avalonia/ClaudeUsage/Assets/ClaudeUsage.ico" <<'PY'
import sys
from PIL import Image

iconset, out = sys.argv[1], sys.argv[2]

# Sizes Windows asks for, largest first: Pillow drops any size larger than the
# image it saves from. Apple renders 16, 32, 128 and 256 itself; the rest are
# downsampled from 256.
sizes = [256, 128, 64, 48, 32, 24, 16]
rendered = {
    16: "icon_16x16.png",
    32: "icon_16x16@2x.png",
    128: "icon_128x128.png",
    256: "icon_128x128@2x.png",
}

def frame(size):
    if size in rendered:
        return Image.open(f"{iconset}/{rendered[size]}").convert("RGBA")
    return base.resize((size, size), Image.LANCZOS)

base = frame(256)
frames = [frame(size) for size in sizes]
frames[0].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])

written = sorted(Image.open(out).info["sizes"])
print(f"wrote {out} ({', '.join(f'{w}x{h}' for w, h in written)})")
PY

echo "wrote $resources/{Assets.car,ClaudeUsage.icns}"
echo "wrote $swift_resources/ClaudeUsage.icns"
echo "wrote $repo/go/Icon.png"
