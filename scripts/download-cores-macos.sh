#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  echo "Usage: $0 <x64|arm64> [core-versions.json path]" >&2
  exit 2
}

architecture="${1:-}"
[[ "$architecture" == "x64" || "$architecture" == "arm64" ]] || usage

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
versions_file="${2:-$repo_root/core-versions.json}"
[[ -f "$versions_file" ]] || { echo "Core version manifest not found: $versions_file" >&2; exit 1; }

github_headers=(-H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28" -H "User-Agent: FireflyVPN-Desktop")
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
  github_headers+=(-H "Authorization: Bearer $GITHUB_TOKEN")
fi

version_for() {
  python3 - "$versions_file" "$1" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    version = json.load(source).get(sys.argv[2], {}).get("version")
if not isinstance(version, str) or not version.strip():
    raise SystemExit(f"A fixed version is required for {sys.argv[2]}")
print(version.strip())
PY
}

select_asset() {
  local release_json="$1"
  local pattern="$2"
  python3 - "$release_json" "$pattern" <<'PY'
import json
import re
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    release = json.load(source)
if release.get("draft") or release.get("prerelease"):
    raise SystemExit("The selected release must be stable and public")
matcher = re.compile(sys.argv[2])
assets = sorted((asset for asset in release.get("assets", []) if matcher.fullmatch(asset.get("name", ""))), key=lambda asset: asset["name"])
if not assets:
    raise SystemExit(1)
asset = assets[0]
digest = asset.get("digest", "")
if not digest.lower().startswith("sha256:"):
    raise SystemExit(f"GitHub did not provide a SHA-256 digest for {asset.get('name', '<unknown>')}")
print("\t".join((asset["browser_download_url"], digest.split(":", 1)[1].lower(), asset["name"])))
PY
}

download_release() {
  local repository="$1"
  local version="$2"
  local destination="$3"
  curl --fail --location --retry 3 --retry-all-errors --silent --show-error \
    "${github_headers[@]}" \
    "https://api.github.com/repos/$repository/releases/tags/v$version" \
    -o "$destination"

  # GitHub embeds only the first asset page in a release response. sing-box
  # currently ships more than 100 assets, so merge every page before matching
  # the archive for this platform.
  local assets_url page page_file asset_count
  assets_url="$(python3 - "$destination" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    release = json.load(source)
assets_url = release.get("assets_url", "")
if not assets_url:
    raise SystemExit("Release response does not contain assets_url")
release["assets"] = []
with open(sys.argv[1], "w", encoding="utf-8") as target:
    json.dump(release, target)
print(assets_url)
PY
)"

  page=1
  while :; do
    page_file="$temporary_root/assets-${repository//\//_}-$page.json"
    curl --fail --location --retry 3 --retry-all-errors --silent --show-error \
      "${github_headers[@]}" \
      "$assets_url?per_page=100&page=$page" \
      -o "$page_file"
    asset_count="$(python3 - "$destination" "$page_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    release = json.load(source)
with open(sys.argv[2], encoding="utf-8") as source:
    assets = json.load(source)
release["assets"].extend(assets)
with open(sys.argv[1], "w", encoding="utf-8") as target:
    json.dump(release, target)
print(len(assets))
PY
)"
    [[ "$asset_count" -lt 100 ]] && break
    ((page += 1))
  done
}

select_asset_exact() {
  local release_json="$1"
  local asset_name="$2"
  python3 - "$release_json" "$asset_name" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    release = json.load(source)
if release.get("draft") or release.get("prerelease"):
    raise SystemExit("The selected release must be stable and public")
asset = next((item for item in release.get("assets", []) if item.get("name") == sys.argv[2]), None)
if asset is None:
    raise SystemExit(1)
digest = asset.get("digest", "")
if not digest.lower().startswith("sha256:"):
    raise SystemExit(f"GitHub did not provide a SHA-256 digest for {asset.get('name', '<unknown>')}")
print("\t".join((asset["browser_download_url"], digest.split(":", 1)[1].lower(), asset["name"])))
PY
}

extract_archive() {
  local archive="$1"
  local destination="$2"
  mkdir -p "$destination"
  case "$archive" in
    *.zip) unzip -q "$archive" -d "$destination" ;;
    *.tar.gz) tar -xzf "$archive" -C "$destination" ;;
    *.gz) gzip -dc "$archive" > "$destination/$(basename "${archive%.gz}")" ;;
    *) echo "Unsupported core archive: $archive" >&2; exit 1 ;;
  esac
}

assert_macos_architecture() {
  local path="$1"
  local details
  details="$(file -b "$path")"
  case "$architecture" in
    x64) [[ "$details" == *"x86_64"* ]] ;;
    arm64) [[ "$details" == *"arm64"* || "$details" == *"arm64e"* ]] ;;
  esac || { echo "Architecture mismatch for $path: $details" >&2; exit 1; }
}

stage_core() {
  local release_json="$1"
  local destination="$2"
  local source_pattern="$3"
  local destination_name="$4"
  shift 4
  local asset_info=""
  local pattern
  for pattern in "$@"; do
    if [[ "$pattern" == exact:* ]]; then
      asset_info="$(select_asset_exact "$release_json" "${pattern#exact:}")" || continue
    elif asset_info="$(select_asset "$release_json" "$pattern")"; then
      break
    fi
    [[ -n "$asset_info" ]] && break
  done
  [[ -n "$asset_info" ]] || { echo "No official macOS $architecture core asset matched: $*" >&2; exit 1; }

  local url checksum asset_name
  IFS=$'\t' read -r url checksum asset_name <<< "$asset_info"
  local archive="$temporary_root/$asset_name"
  curl --fail --location --retry 3 --retry-all-errors --silent --show-error "$url" -o "$archive"
  [[ "$(shasum -a 256 "$archive" | awk '{print tolower($1)}')" == "$checksum" ]] || {
    echo "SHA-256 verification failed for $asset_name" >&2
    exit 1
  }

  local extract="$temporary_root/extract-${asset_name//[^A-Za-z0-9]/_}"
  extract_archive "$archive" "$extract"
  local executable
  executable="$(find "$extract" -type f -name "$source_pattern" -print -quit)"
  [[ -n "$executable" ]] || { echo "$source_pattern was not found in $asset_name" >&2; exit 1; }
  assert_macos_architecture "$executable"

  mkdir -p "$destination"
  cp "$executable" "$destination/$destination_name"
  chmod +x "$destination/$destination_name"
  while IFS= read -r -d '' dylib; do
    cp "$dylib" "$destination/$(basename "$dylib")"
  done < <(find "$extract" -type f -name '*.dylib' -print0)
}

singbox_version="$(version_for 'sing-box')"
xray_version="$(version_for 'xray')"
mihomo_version="$(version_for 'mihomo')"
singbox_arch="amd64"
xray_asset="Xray-macos-64.zip"
if [[ "$architecture" == "arm64" ]]; then
  singbox_arch="arm64"
  xray_asset="Xray-macos-arm64-v8a.zip"
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/fireflyvpn-macos-cores.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT
download_release 'SagerNet/sing-box' "$singbox_version" "$temporary_root/sing-box.json"
download_release 'XTLS/Xray-core' "$xray_version" "$temporary_root/xray.json"
download_release 'MetaCubeX/mihomo' "$mihomo_version" "$temporary_root/mihomo.json"

bin_root="$repo_root/artifacts/core-extracted/v2rayN-macos-$architecture/bin"
stage_core "$temporary_root/sing-box.json" "$bin_root/sing_box" 'sing-box' 'sing-box' "exact:sing-box-${singbox_version}-darwin-${singbox_arch}.tar.gz"
stage_core "$temporary_root/xray.json" "$bin_root/xray" 'xray' 'xray' "exact:${xray_asset}"
stage_core "$temporary_root/mihomo.json" "$bin_root/mihomo" 'mihomo*' 'mihomo' \
  "exact:mihomo-darwin-${singbox_arch}-compatible-v${mihomo_version}.gz" \
  "exact:mihomo-darwin-${singbox_arch}-v${mihomo_version}.gz" \
  "mihomo-darwin-${singbox_arch}(?:-[A-Za-z0-9]+)*-v${mihomo_version}\\.gz"

for required_core in "$bin_root/sing_box/sing-box" "$bin_root/xray/xray" "$bin_root/mihomo/mihomo"; do
  [[ -x "$required_core" ]] || { echo "Required macOS core was not staged: $required_core" >&2; exit 1; }
done
echo "Installed fixed sing-box $singbox_version, Xray $xray_version, and Mihomo $mihomo_version for macOS $architecture."
