#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  echo "Usage: $0 <x64|arm64> <publish-directory> [core-directory]" >&2
  exit 2
}

architecture="${1:-}"
publish_directory="${2:-}"
[[ "$architecture" == "x64" || "$architecture" == "arm64" ]] || usage
[[ -n "$publish_directory" && -d "$publish_directory" ]] || usage

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
core_directory="${3:-$repo_root/artifacts/core-extracted/v2rayN-macos-$architecture/bin}"
[[ -d "$core_directory" ]] || { echo "Core directory not found: $core_directory" >&2; exit 1; }

version="$(sed -nE 's/.*<Version>([^<]+)<\/Version>.*/\1/p' "$repo_root/Firefly/Directory.Build.props" | head -n 1)"
[[ -n "$version" ]] || { echo "Application version was not found." >&2; exit 1; }

product_name="流萤加速器"
artifacts="$repo_root/artifacts"
bundle_root="$artifacts/package-macos-$architecture"
app_path="$bundle_root/$product_name.app"
macos_path="$app_path/Contents/MacOS"
resources_path="$app_path/Contents/Resources"
zip_path="$artifacts/$product_name-macos-$architecture.zip"
dmg_path="$artifacts/$product_name-macos-$architecture.dmg"

for required_core in 'sing_box/sing-box' 'xray/xray' 'mihomo/mihomo'; do
  [[ -f "$core_directory/$required_core" ]] || { echo "Required core is missing: $core_directory/$required_core" >&2; exit 1; }
done
[[ -f "$publish_directory/Firefly" ]] || { echo "Published macOS executable is missing: $publish_directory/Firefly" >&2; exit 1; }
command -v iconutil >/dev/null || { echo "iconutil is required on macOS." >&2; exit 1; }
command -v codesign >/dev/null || { echo "codesign is required on macOS." >&2; exit 1; }
command -v create-dmg >/dev/null || { echo "create-dmg is required; install it with Homebrew." >&2; exit 1; }

rm -rf "$bundle_root"
rm -f "$zip_path" "$dmg_path"
mkdir -p "$macos_path" "$resources_path"
cp -R "$publish_directory/." "$macos_path/"
mkdir -p "$macos_path/bin"
cp -R "$core_directory/." "$macos_path/bin/"

iconset="$macos_path/Assets/Firefly.iconset"
[[ -d "$iconset" ]] || { echo "Branded iconset is missing from publish output: $iconset" >&2; exit 1; }
iconutil -c icns "$iconset" -o "$resources_path/AppIcon.icns"
# An .iconset directory is only an iconutil input. Leaving it in Contents/MacOS
# makes codesign treat it as a nested bundle on some macOS runners.
rm -rf "$iconset"
rm -f "$macos_path/FireflyVPN.icns" "$macos_path/v2rayN.icns" "$macos_path/v2rayN.png"

cat > "$app_path/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>zh-Hans</string>
  <key>CFBundleDisplayName</key><string>$product_name</string>
  <key>CFBundleExecutable</key><string>Firefly</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>com.fireflyvpn.desktop</string>
  <key>CFBundleName</key><string>$product_name</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>13.6</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
EOF

# Clear metadata copied from the publish output before signing. It can otherwise
# invalidate an ad-hoc signature on the hosted macOS runners.
xattr -cr "$app_path"

chmod +x "$macos_path/Firefly" "$macos_path/bin/sing_box/sing-box" "$macos_path/bin/xray/xray" "$macos_path/bin/mihomo/mihomo"
while IFS= read -r -d '' native_file; do
  # .NET publishes native libraries beside the main executable as well as
  # bundled cores. Do not sign the bundle's main executable until all sibling
  # native libraries are signed: codesign treats Contents/MacOS/Firefly as the
  # bundle entry point and validates those siblings while replacing its
  # existing signature.
  [[ "$native_file" == "$macos_path/Firefly" ]] && continue
  if file -b "$native_file" | grep -q 'Mach-O'; then
    codesign --force --sign - --timestamp=none "$native_file"
  fi
done < <(find "$macos_path" -type f -print0)
codesign --force --sign - --timestamp=none "$macos_path/Firefly"
codesign --force --sign - --timestamp=none "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"

ditto -c -k --sequesterRsrc --keepParent "$app_path" "$zip_path"
create-dmg --overwrite --volname "$product_name Installer" --window-size 700 420 --icon-size 100 \
  --icon "$product_name.app" 160 185 --hide-extension "$product_name.app" --app-drop-link 500 185 \
  "$dmg_path" "$bundle_root"

[[ -f "$zip_path" && -f "$dmg_path" ]] || { echo "macOS package output verification failed." >&2; exit 1; }
echo "Created $zip_path and $dmg_path"
