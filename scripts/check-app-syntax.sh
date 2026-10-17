#!/usr/bin/env bash
# Prüft src/RoG.App unter Linux, bevor die Windows-CI läuft:
#  1. C#-Kompilierung samt Analyzern gegen die Referenzen des Windows App SDK (tools/AppCompileCheck,
#     XAML-generierter Code durch Platzhalter ersetzt) – findet fehlende usings, Typfehler, CA-Regeln …
#  2. x:Bind-Pfade gegen die Quellen (scripts/check-xaml-bindings.py) – der XAML-Compiler stürzt sonst teils ohne Meldung ab.
#  3. Namen für Screenreader (scripts/check-a11y.py): Symbol-Schaltflächen, Eingabefelder, Bilder.
# XAML selbst (Markup, Ressourcen) prüft weiter nur die CI.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! out=$(dotnet build tools/AppCompileCheck/AppCompileCheck.csproj -c Release -nologo -v q 2>&1); then
  echo "$out" | grep -E " error " | sed 's/ \[\/.*//; s/ (https[^)]*)//' | sort -u
  echo "App-Kompilierung fehlgeschlagen"
  exit 1
fi
echo "App kompiliert"
python3 -I scripts/check-xaml-bindings.py
python3 -I scripts/check-a11y.py
