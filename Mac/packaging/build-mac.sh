#!/bin/bash
# Builds the Mac release on a Mac (GitHub Actions runs this; see .github/workflows/mac-release.yml).
#
#   Mac/packaging/build-mac.sh 1.2.1.0          a full release build
#   Mac/packaging/build-mac.sh 1.2.1.0 beta     a beta build (labelled beta, new users start on Beta)
#
# See docs/RELEASING.md for how Mac and Windows releases are kept apart.
#
# Output in artifacts/mac/:
#   StreamDeckIconManager<version>MacSetup.pkg     the installer for new users (Apple silicon and Intel)
#   StreamDeckIconManager<version>Mac-arm64.zip    the app, for the in-app updater (Apple silicon)
#   StreamDeckIconManager<version>Mac-x64.zip      the app, for the in-app updater (Intel)
#
# The app is signed ad hoc (no Apple Developer ID yet), so macOS asks once before running the
# installer; the installer then clears the quarantine flag itself.
set -euo pipefail

VERSION="${1:-}"
KIND="${2:-release}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || [[ "$KIND" != "release" && "$KIND" != "beta" ]]; then
  echo "Usage: $0 <version> [beta], e.g. 1.2.1.0" >&2
  exit 1
fi
BETA=false
[ "$KIND" = "beta" ] && BETA=true

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PACKAGING="$ROOT/Mac/packaging"
OUT="$ROOT/artifacts/mac"
WORK="$ROOT/artifacts/mac-work"
APP="Stream Deck Icon Manager.app"
PKG_ID="com.suttontools.streamdeckiconmanager.pkg"
STAGE_DIR="/Library/Application Support/StreamDeckIconManager/Install"

rm -rf "$OUT" "$WORK"
mkdir -p "$OUT" "$WORK/pkgroot"

for RID in osx-arm64 osx-x64; do
  ARCH="${RID#osx-}"
  echo "-> Publishing $RID"
  dotnet publish "$ROOT/Mac/StreamDeckIconManager.Mac.csproj" -c Release -r "$RID" --self-contained true \
    -p:UseAppHost=true -p:SdimBeta="$BETA" \
    -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION" -p:InformationalVersion="$VERSION" \
    -o "$WORK/$RID/publish"

  echo "-> Building $APP ($ARCH)"
  BUNDLE="$WORK/pkgroot/$ARCH/$APP"
  mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
  ditto "$WORK/$RID/publish" "$BUNDLE/Contents/MacOS"
  find "$BUNDLE/Contents/MacOS" -name '*.pdb' -delete
  cp "$PACKAGING/AppIcon.icns" "$BUNDLE/Contents/Resources/AppIcon.icns"
  sed "s/{VERSION}/$VERSION/g" "$PACKAGING/Info.plist" | tr -d '\r' > "$BUNDLE/Contents/Info.plist"
  printf 'APPL????' > "$BUNDLE/Contents/PkgInfo"
  chmod +x "$BUNDLE/Contents/MacOS/StreamDeckIconManager" "$BUNDLE/Contents/MacOS/tools/gif2webp/"*
  plutil -lint "$BUNDLE/Contents/Info.plist"

  codesign --force --deep --sign - "$BUNDLE"
  codesign --verify --deep --strict "$BUNDLE"

  # The updater's download: just the app.
  ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$OUT/StreamDeckIconManager${VERSION}Mac-${ARCH}.zip"
done

echo "-> Building the installer"
# Both apps go to a staging folder; the postinstall script moves the right one for this Mac into
# /Applications. Apps must not be "relocatable", or Installer would put them wherever an older
# copy with the same bundle ID happens to be.
pkgbuild --analyze --root "$WORK/pkgroot" "$WORK/component.plist"
i=0
while plutil -extract "$i" xml1 -o /dev/null "$WORK/component.plist" 2>/dev/null; do
  plutil -replace "$i.BundleIsRelocatable" -bool NO "$WORK/component.plist"
  plutil -replace "$i.BundleIsVersionChecked" -bool NO "$WORK/component.plist"
  i=$((i + 1))
done
if [ "$i" -lt 2 ]; then
  echo "Expected both apps in the installer, found $i" >&2
  exit 1
fi

SCRIPTS="$WORK/scripts"
mkdir -p "$SCRIPTS"
tr -d '\r' < "$PACKAGING/pkg-scripts/postinstall" > "$SCRIPTS/postinstall"
chmod +x "$SCRIPTS/postinstall"

pkgbuild --root "$WORK/pkgroot" \
  --component-plist "$WORK/component.plist" \
  --install-location "$STAGE_DIR" \
  --scripts "$SCRIPTS" \
  --identifier "$PKG_ID" \
  --version "$VERSION" \
  "$WORK/app.pkg"

sed "s/{VERSION}/$VERSION/g" "$PACKAGING/distribution.xml" | tr -d '\r' > "$WORK/distribution.xml"
mkdir -p "$WORK/resources"
tr -d '\r' < "$PACKAGING/welcome.html" > "$WORK/resources/welcome.html"
productbuild --distribution "$WORK/distribution.xml" \
  --package-path "$WORK" \
  --resources "$WORK/resources" \
  "$OUT/StreamDeckIconManager${VERSION}MacSetup.pkg"

pkgutil --check-signature "$OUT/StreamDeckIconManager${VERSION}MacSetup.pkg" || true
ls -la "$OUT"
