# Rebels of Gaming für Windows

Native Windows-App (WinUI 3, Windows App SDK, .NET 10) für die Clan-Plattform der **Rebels of Gaming** – Verteilung
über den Microsoft Store (MSIX). Ziel: alles, was die Website kann, plus Tray, Windows-Benachrichtigungen, mehrere
Fenster, Tastenkürzel und Drag & Drop.

- Arbeitsregeln: [`CLAUDE.md`](CLAUDE.md)
- Funktionsumfang und Stufen: [`docs/WINDOWS-PFLICHTENHEFT.md`](docs/WINDOWS-PFLICHTENHEFT.md)
- Stand, Annahmen, offene Fragen: [`docs/WINDOWS-BAUPLAN.md`](docs/WINDOWS-BAUPLAN.md)
- Build-VM einrichten: [`docs/BUILD-VM.md`](docs/BUILD-VM.md) · Store: [`docs/STORE.md`](docs/STORE.md) ·
  Versionen: [`docs/VERSIONIERUNG.md`](docs/VERSIONIERUNG.md) · API-Lücken: [`docs/API-LUECKEN.md`](docs/API-LUECKEN.md)

## Aufbau

| Projekt | Ziel | Baut unter |
|---|---|---|
| `src/RoG.Core` | Logik: API-Client (aus OpenAPI generiert), HTTP-Pipeline, Login (PKCE + Loopback), Echtzeit (Reverb), Einstellungen | Linux + Windows |
| `tests/RoG.Core.Tests` | xUnit-Tests für Core, Übersetzungen und App-Manifest | Linux + Windows |
| `src/RoG.App` | WinUI-3-Oberfläche, MSIX-Paket | nur Windows (Build-VM) |

## Entwicklung auf Linux (dev-linux)

Das .NET SDK liegt ohne root in `~/.dotnet`:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet

# in ~/.bashrc bzw. ~/.profile:
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

```bash
dotnet tool restore                      # NSwag (lokales Tool)
dotnet build RoG.sln                     # Core + Tests (RoG.App wird unter Linux übersprungen)
dotnet test RoG.sln
scripts/generate-api.sh                  # API-Client aus openapi/openapi.json neu erzeugen
scripts/generate-api.sh --from-website   # Spec vorher aus dem Website-Repo exportieren (nur lesend)
```

## Bauen auf Windows (Build-VM)

```bash
scripts/remote-build.sh --check                   # Verbindung/Werkzeuge prüfen
scripts/remote-build.sh                           # Core + Tests + App (x64, ARM64)
scripts/remote-build.sh --mode store --committed  # .msixupload für das Partner Center
```

Direkt auf Windows: `pwsh -File scripts/build-windows.ps1 [-Mode Build|Sideload|Store]`.

## Lizenzen Dritter

- Barlow Condensed – SIL Open Font License 1.1 (`src/RoG.App/Fonts/OFL.txt`)
- Logo und Icons – Rebels of Gaming (aus dem Brand-Kit der Website)
