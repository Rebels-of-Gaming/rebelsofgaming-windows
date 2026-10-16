#!/usr/bin/env bash
# Wartet auf den Windows-Job des neuesten CI-Laufs (oder Lauf-ID als $1), lädt das Artefakt und zeigt die
# Build-Fehler aus den Binärlogs (dotnet msbuild kann .binlog auch unter Linux abspielen).
# GitHub gibt Job-Logs erst frei, wenn der ganze Lauf fertig ist – das Artefakt ist früher da.
set -euo pipefail
export PATH="$HOME/.dotnet:$PATH"
cd "$(dirname "${BASH_SOURCE[0]}")/.."

RUN="${1:-$(gh run list -L 1 --workflow ci.yml --json databaseId --jq '.[0].databaseId')}"
echo "Lauf $RUN – warte auf den Windows-Job …"
while :; do
  STATE=$(gh run view "$RUN" --json jobs --jq '.jobs[] | select(.name | startswith("WinUI")) | "\(.status) \(.conclusion)"')
  [[ "$STATE" == completed* ]] && break
  sleep 20
done
echo "Windows-Job: $STATE"

DIR="${TMPDIR:-/tmp}/rog-ci-$RUN"
rm -rf "$DIR" && mkdir -p "$DIR"
gh run download "$RUN" -n windows-artifacts -D "$DIR" >/dev/null
grep -E '"name"|"exit"|"error"' "$DIR/summary.json" | tr -d '\r' | paste - - | sed 's/  */ /g' || true

for log in "$DIR"/logs/*.binlog; do
  dotnet msbuild "$log" -noconlog -flp:"errorsonly;logfile=$log.errors.txt" >/dev/null 2>&1 || true
  if [[ -s "$log.errors.txt" ]]; then
    echo "--- $(basename "$log")"
    sed -E 's/^ *[0-9:>]*//; s/ \[[^]]*\]$//; s/^.*\\(src\\)/\1/' "$log.errors.txt" | sort -u
  fi
done
[[ "$STATE" == "completed success" ]]
