#!/usr/bin/env bash
# Erzeugt den API-Client src/RoG.Core/Api/Generated/RoGApiClient.g.cs aus openapi/openapi.json.
#
#   scripts/generate-api.sh                 # vorhandene openapi/openapi.json verwenden
#   scripts/generate-api.sh --from-website  # Spec vorher aus dem Website-Repo exportieren (nur lesend)
#
# Website-Repo (nur lesen!): ROG_WEBSITE_DIR, Standard /home/wahke/projects/rebelsofgaming
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [[ "${1:-}" == "--from-website" ]]; then
  WEB="${ROG_WEBSITE_DIR:-/home/wahke/projects/rebelsofgaming}"
  echo "Exportiere Spec aus $WEB (scramble:export, nur lesend) …"
  (cd "$WEB" && php artisan scramble:export --stdout --env=testing) > "$ROOT/openapi/openapi.json.tmp"
  python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$ROOT/openapi/openapi.json.tmp"
  mv "$ROOT/openapi/openapi.json.tmp" "$ROOT/openapi/openapi.json"
fi

cd "$ROOT"
dotnet tool restore >/dev/null
python3 scripts/normalize-openapi.py
(cd openapi && dotnet nswag run nswag.json)
rm -f openapi/openapi.normalized.json

# Inline-Objekte wie `system` oder `type` werden zu Klassen `System`/`Type` und verdecken System-Typen
# (CS0576 bzw. Verwechslungsgefahr) → Endung `Info`.
OUT=src/RoG.Core/Api/Generated/RoGApiClient.g.cs
for NAME in System Task Object String Type Uri Exception; do
  sed -i -E "s/public partial class ${NAME}\$/public partial class ${NAME}Info/; s/public ${NAME}(\\??) ([A-Za-z_]+) \\{/public ${NAME}Info\\1 \\2 {/" "$OUT"
done
if grep -qE 'public partial class (System|Task|Object|String|Type|Uri|Exception)$' "$OUT"; then
  echo "Fehler: generierte Klasse kollidiert mit einem System-Typ – Umbenennung in scripts/generate-api.sh ergänzen." >&2
  exit 1
fi
# 201 & Co. als Erfolg, Enum-Werte wie in der API (`windows` statt `Windows`) – Details im Skript
python3 scripts/postprocess-client.py "$OUT"
echo "Fertig: src/RoG.Core/Api/Generated/RoGApiClient.g.cs ($(wc -l < src/RoG.Core/Api/Generated/RoGApiClient.g.cs) Zeilen)"
