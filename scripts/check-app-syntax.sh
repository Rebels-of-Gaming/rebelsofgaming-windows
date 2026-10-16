#!/usr/bin/env bash
# Syntaxprüfung von src/RoG.App unter Linux (Parserfehler vor dem Windows-CI finden). Semantik prüft weiter die CI.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
out=artifacts/tools/AppSyntaxCheck
dotnet build tools/AppSyntaxCheck/AppSyntaxCheck.csproj -c Release -o "$out" -v q -nologo >/dev/null
dotnet "$out/AppSyntaxCheck.dll" src/RoG.App
