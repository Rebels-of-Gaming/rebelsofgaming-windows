#!/usr/bin/env bash
# Baut die Windows-App auf der Build-VM: Quellstand per SSH hochladen, scripts/build-windows.ps1 ausführen,
# Logs/Testergebnisse/Pakete zurückholen.
#
#   scripts/remote-build.sh --check                # Verbindung und Werkzeuge auf der VM prüfen
#   scripts/remote-build.sh                        # bauen + testen (Mode Build)
#   scripts/remote-build.sh --mode store           # zusätzlich .msixupload für das Partner Center
#   scripts/remote-build.sh --mode sideload        # signiertes Testpaket (Thumbprint aus ROG_SIGNING_THUMBPRINT)
#   scripts/remote-build.sh --committed            # nur den committeten Stand (HEAD) senden statt Arbeitskopie
#
# Zugang aus ~/.config/rog-windows/build-vm.env (nie im Repo), Vorlage: scripts/build-vm.env.example.
# Ergebnis: artifacts/remote/<lauf>/build.log und artifacts/remote/<lauf>/artifacts/ (summary.json, logs/, test-results/, msix/)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${ROG_BUILD_VM_CONFIG:-$HOME/.config/rog-windows/build-vm.env}"

MODE="build"
CONFIGURATION="Release"
SKIP_TESTS=""
COMMITTED=""
CHECK=""

usage() { sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) MODE="${2,,}"; shift 2 ;;
    --debug) CONFIGURATION="Debug"; shift ;;
    --skip-tests) SKIP_TESTS="-SkipTests"; shift ;;
    --committed) COMMITTED=1; shift ;;
    --check) CHECK=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$MODE" in
  build) PS_MODE="Build" ;;
  sideload) PS_MODE="Sideload" ;;
  store) PS_MODE="Store" ;;
  *) echo "--mode muss build, sideload oder store sein" >&2; exit 2 ;;
esac

if [[ ! -f "$CONFIG" ]]; then
  echo "Konfiguration fehlt: $CONFIG" >&2
  echo "Vorlage kopieren:  mkdir -p ~/.config/rog-windows && cp scripts/build-vm.env.example $CONFIG && chmod 600 $CONFIG" >&2
  exit 2
fi
# shellcheck source=/dev/null
source "$CONFIG"

: "${ROG_VM_HOST:?ROG_VM_HOST fehlt in $CONFIG}"
ROG_VM_PORT="${ROG_VM_PORT:-22}"
ROG_VM_USER="${ROG_VM_USER:-builder}"
ROG_VM_KEY="${ROG_VM_KEY:-$HOME/.ssh/rog-windows-builder}"
ROG_VM_DIR="${ROG_VM_DIR:-rog-build}"   # relativ zum Profil des Benutzers auf der VM (C:\Users\builder\rog-build)
ROG_VM_KEEP_RUNS="${ROG_VM_KEEP_RUNS:-5}"

SSH_OPTS=(-p "$ROG_VM_PORT" -i "$ROG_VM_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 -o ServerAliveInterval=30)
SCP_OPTS=(-P "$ROG_VM_PORT" -i "$ROG_VM_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15)
TARGET="$ROG_VM_USER@$ROG_VM_HOST"

remote_ps() {
  # PowerShell-7-Befehl auf der VM ausführen. -EncodedCommand (Base64, UTF-16LE) umgeht jedes Quoting – egal ob die
  # Standard-Shell des OpenSSH-Servers cmd.exe oder PowerShell ist.
  local encoded
  encoded="$(printf '%s' "$1" | iconv -f UTF-8 -t UTF-16LE | base64 -w0)"
  # shellcheck disable=SC2029  # bewusst clientseitig expandiert
  ssh "${SSH_OPTS[@]}" "$TARGET" "pwsh -NoProfile -NonInteractive -EncodedCommand $encoded"
}

if [[ -n "$CHECK" ]]; then
  echo "Verbinde mit $TARGET:$ROG_VM_PORT …"
  # shellcheck disable=SC2016  # $-Ausdrücke sind PowerShell, nicht Bash
  remote_ps '"PowerShell " + $PSVersionTable.PSVersion.ToString()
(Get-CimInstance Win32_OperatingSystem).Caption
".NET SDK " + (dotnet --version)
git --version
$v = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $v) { & $v -latest -products * -property displayName } else { "Visual Studio/Build Tools: nicht gefunden" }
if (Get-Command tar -ErrorAction SilentlyContinue) { "tar vorhanden" } else { "tar fehlt" }'

  exit 0
fi

SHA="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo nogit)"
RUN="$(date -u +%Y%m%d-%H%M%S)-$SHA"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ARCHIVE="$WORK/src.tar.gz"

if [[ -n "$COMMITTED" ]]; then
  echo "Packe committeten Stand $SHA …"
  git -C "$ROOT" archive --format=tar.gz -o "$ARCHIVE" HEAD
else
  DIRTY="$(git -C "$ROOT" status --porcelain | wc -l | tr -d ' ')"
  echo "Packe Arbeitskopie ($SHA, $DIRTY geänderte Dateien) …"
  # Alle versionierten und neuen, nicht ignorierten Dateien (keine bin/obj/artifacts, keine Geheimnisse aus .gitignore)
  (cd "$ROOT" && git ls-files -z --cached --others --exclude-standard | xargs -0 -I{} sh -c '[ -e "{}" ] && printf "%s\0" "{}"' | tar --null -czf "$ARCHIVE" -T -)
fi
echo "Archiv: $(du -h "$ARCHIVE" | cut -f1)"

REMOTE_RUN="$ROG_VM_DIR/runs/$RUN"
echo "Lade nach $TARGET:$REMOTE_RUN …"
remote_ps "New-Item -ItemType Directory -Force -Path '$REMOTE_RUN' | Out-Null"
scp "${SCP_OPTS[@]}" -q "$ARCHIVE" "$TARGET:$REMOTE_RUN/src.tar.gz"

echo "Baue auf der VM (Mode $PS_MODE, $CONFIGURATION) …"
LOCAL_OUT="$ROOT/artifacts/remote/$RUN"
mkdir -p "$LOCAL_OUT"
set +e
remote_ps "Set-Location '$REMOTE_RUN'; tar -xzf src.tar.gz; Remove-Item src.tar.gz; & ./scripts/build-windows.ps1 -Mode $PS_MODE -Configuration $CONFIGURATION $SKIP_TESTS; exit \$LASTEXITCODE" 2>&1 | tee "$LOCAL_OUT/build.log"
STATUS=${PIPESTATUS[0]}
set -e

echo "Hole Ergebnisse nach artifacts/remote/$RUN …"
scp "${SCP_OPTS[@]}" -q -r "$TARGET:$REMOTE_RUN/artifacts" "$LOCAL_OUT/" || echo "Warnung: keine Artefakte gefunden" >&2

# Alte Läufe auf der VM aufräumen (die letzten ROG_VM_KEEP_RUNS bleiben)
remote_ps "Get-ChildItem '$ROG_VM_DIR/runs' -Directory | Sort-Object Name -Descending | Select-Object -Skip $ROG_VM_KEEP_RUNS | Remove-Item -Recurse -Force" || true

if [[ -f "$LOCAL_OUT/artifacts/summary.json" ]]; then
  echo "--- summary.json ---"
  cat "$LOCAL_OUT/artifacts/summary.json"
fi
exit "$STATUS"
