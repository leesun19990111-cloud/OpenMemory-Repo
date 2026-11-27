#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"
VERSION=3.0.0-preview.1
OUT="$ROOT/build"
APP="$OUT/OpenMemory.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT/artifacts"
swift test --parallel
for ARCH in arm64 x86_64; do
  swift build -c release --arch "$ARCH" --scratch-path "$ROOT/.build/$ARCH" --product OpenMemory
  BIN="$(swift build -c release --arch "$ARCH" --scratch-path "$ROOT/.build/$ARCH" --show-bin-path)"
  cp "$BIN/OpenMemory" "$OUT/OpenMemory-$ARCH"
done
lipo -create "$OUT/OpenMemory-arm64" "$OUT/OpenMemory-x86_64" -output "$APP/Contents/MacOS/OpenMemory"
cp "$ROOT/Info.plist" "$APP/Contents/Info.plist"
cp "$ROOT/../assets/OpenMemory.png" "$APP/Contents/Resources/OpenMemory.png"
ICONSET="$OUT/OpenMemory.iconset"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" "$ROOT/../assets/OpenMemory.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" "$ROOT/../assets/OpenMemory.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/OpenMemory.icns"
plutil -lint "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
codesign --verify --deep --strict "$APP"
"$APP/Contents/MacOS/OpenMemory" --smoke-test
"$APP/Contents/MacOS/OpenMemory" --render "$OUT/artifacts/OpenMemory-macOS-preview.png"
lipo "$APP/Contents/MacOS/OpenMemory" -verify_arch arm64 x86_64
mkdir -p "$OUT/pkg-root/Applications"
ditto "$APP" "$OUT/pkg-root/Applications/OpenMemory.app"
pkgbuild --root "$OUT/pkg-root" --identifier local.openmemory.mac --version 3.0.0 --install-location / "$OUT/artifacts/OpenMemory-$VERSION-universal.pkg"
mkdir -p "$OUT/dmg-root"
ditto "$APP" "$OUT/dmg-root/OpenMemory.app"
cp "$ROOT/README.md" "$OUT/dmg-root/사용설명서.md"
if [[ ! -L "$OUT/dmg-root/Applications" ]]; then ln -s /Applications "$OUT/dmg-root/Applications"; fi
hdiutil create -volname "OpenMemory" -srcfolder "$OUT/dmg-root" -ov -format UDZO "$OUT/artifacts/OpenMemory-$VERSION-universal.dmg"
cp "$ROOT/README.md" "$OUT/artifacts/macOS-사용설명서.md"
(cd "$OUT/artifacts" && shasum -a 256 *.pkg *.dmg > SHA256SUMS.txt)
echo "Built universal macOS preview: $OUT/artifacts"
