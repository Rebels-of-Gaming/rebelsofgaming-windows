#!/usr/bin/env bash
# Setzt die App-Version an beiden Stellen (Directory.Build.props RoGVersion + Package.appxmanifest Identity Version).
#   scripts/bump-version.sh patch|minor|major|X.Y.Z
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cur=$(grep -oP '(?<=<RoGVersion>)[0-9.]+(?=</RoGVersion>)' "$ROOT/Directory.Build.props")
IFS=. read -r ma mi pa <<<"$cur"
case "${1:-patch}" in
  patch) new="$ma.$mi.$((pa+1))" ;;
  minor) new="$ma.$((mi+1)).0" ;;
  major) new="$((ma+1)).0.0" ;;
  [0-9]*.[0-9]*.[0-9]*) new="$1" ;;
  *) echo "Aufruf: $0 patch|minor|major|X.Y.Z" >&2; exit 1 ;;
esac
sed -i "s|<RoGVersion>$cur</RoGVersion>|<RoGVersion>$new</RoGVersion>|" "$ROOT/Directory.Build.props"
sed -i -E "s|(<Identity[^>]*Version=\")$cur\.0\"|\1$new.0\"|; s|^(    Version=\")$cur\.0\"|\1$new.0\"|" "$ROOT/src/RoG.App/Package.appxmanifest"
grep -q "Version=\"$new.0\"" "$ROOT/src/RoG.App/Package.appxmanifest" || { echo "Manifest-Version nicht gefunden" >&2; exit 1; }
echo "$cur → $new"
