# CLAUDE.md – Rebels of Gaming für Windows (WinUI 3, Microsoft Store)

Dauerhafte Arbeitsregeln für dieses Repository. Gilt für jede Sitzung.

## Projekt

Native Windows-App für **Rebels of Gaming (RoG)**, Multigaming-Clan seit 2020. Owner: Wahke.
Die App ist ein vollwertiger Client der Laravel-Plattform und soll **alles können, was die Website kann** – plus das,
was ein Desktop besser kann (Tray, Toasts, mehrere Fenster, Tastenkürzel, Drag & Drop, Zwischenablage).

- Funktionsumfang und Bau-Reihenfolge: `docs/WINDOWS-PFLICHTENHEFT.md`
- Fortschritt, Annahmen, offene Fragen: `docs/WINDOWS-BAUPLAN.md`
- Fehlende/unpassende API-Endpunkte: `docs/API-LUECKEN.md`
- Build-VM (Windows, per SSH): `docs/BUILD-VM.md` · Store: `docs/STORE.md` · Versionen: `docs/VERSIONIERUNG.md`

Owner-Entscheidungen (15.10.2026): **C# / WinUI 3 (Windows App SDK)**, Verteilung **Microsoft Store (MSIX)**,
Bauen/Testen der Oberfläche auf einer **eigenen Windows-VM per SSH**. Entwickelt wird auf dev-linux (Debian 13).

## Andere Repositories (nur lesen!)

| Repo | Pfad | Wofür |
|---|---|---|
| Website | `/home/wahke/projects/rebelsofgaming/` | einzige Wahrheit über die API: `docs/api/` (Guides, v. a. `35-apps.md`), `docs/PFLICHTENHEFT.md`, `docs/ERGAENZUNG-API-WIKI-DOMAINS.md`, `docs/DESIGN.md`, `app/Modules/*/routes/api.php`, `resources/brand/`, `resources/fonts/` |
| Mobile-App | `/home/wahke/projects/rog-app/` | Vorbild für Funktionsumfang, Stufen, Annahmen, Texte (`docs/APP-*.md`, `src/i18n/`), Spec-Kopie `openapi/openapi.json` |

**Regel: In den anderen Repos wird nichts geändert, nichts committet, kein `git checkout`/`stash`, keine git-Befehle.**
Dort arbeiten andere Claude-Code-Sitzungen. Erlaubt ist nur Lesen, auch `php artisan scramble:export --stdout --env=testing`
im Website-Repo (schreibt nichts).

Fehlt ein Endpunkt oder braucht es Server-Änderungen (z. B. WNS-Push, Loopback-Pfad, Protokoll-Links), kommt das in
`docs/API-LUECKEN.md` (was, warum, Vorschlag, Screen, Priorität). Bis dahin in der App sauber mit Leerzustand/„kommt
bald“ abfangen – nie Daten erfinden, nie HTML scrapen.

## Stack

- **.NET 10 (LTS)**, C# latest, Nullable an, Warnungen = Fehler (außer in `RoG.App` bis zum ersten VM-Build)
- **WinUI 3 / Windows App SDK**, `net10.0-windows10.0.19041.0`, min. Windows 10 1809 (17763), x64 + ARM64
- **CommunityToolkit.Mvvm** (ObservableObject, `[ObservableProperty]` als partielle Eigenschaften, `[RelayCommand]`)
- **MSIX** (Single-Project), Store-Upload `.msixupload`
- API-Client **aus der OpenAPI-Spec generiert** (NSwag, `scripts/generate-api.sh`) – keine handgeschriebenen DTOs, außer
  im Login (stabiler Vertrag aus Guide 35)
- Tests: **xUnit** (unter Linux), später UI-Tests auf der VM
- Paketversionen zentral in `Directory.Packages.props`

## Architektur

```
src/RoG.Core/            # net10.0 – baut und testet unter Linux. ALLE Logik hierhin.
  Api/                   # generierter Client (Generated/), RoGHttpHandler (Pipeline), ApiProblem (RFC 9457), ETag
  Auth/                  # PKCE, Loopback-Rücksprung, AppLoginFlow, ITokenStore, Scopes, Gerätename
  Realtime/              # Reverb/Pusher-Client über ClientWebSocket, Kanäle, Presence
  Settings/              # AppSettings, Speicher, Sprachwahl
  Localization/          # StringKeys (Code-Schlüssel der .resw), ILocalizer
  Config/                # environments.json (einzige Stelle mit Domains)
src/RoG.App/             # WinUI 3 – nur Oberfläche und Windows-Verdrahtung (PasswordVault, Launcher, Toasts, Tray)
  Views/ ViewModels/ Services/ Themes/RoGTheme.xaml Strings/<Sprache>/Resources.resw Assets/ Fonts/
tests/RoG.Core.Tests/    # xUnit; prüft auch .resw-Vollständigkeit und Manifest-Konsistenz der App
scripts/                 # generate-api.sh, build-windows.ps1 (VM), remote-build.sh (dev-linux → VM)
```

- **API-first wie die Website:** Logik nur in `RoG.Core` (Services/Use-Cases), ViewModels rufen Core, Views binden ViewModels.
  Nichts, was sich testen lässt, gehört in `RoG.App`.
- Module wie auf der Website (Auth, Profile, Members, Squads, Forum, Messages, Notifications, Events, Servers,
  Pterodactyl, Wiki, Uploads …) – je Modul ein Ordner in Core (`Features/<Modul>/`) und in der App (`Views/<Modul>/`).
- **Rechte nie über Rollennamen** – nur `permissions` aus `/v1/me` plus Token-Scopes. Die Serverantwort entscheidet.
- Externe Ausfälle (API, Reverb) dürfen keine Seite kaputt machen: Cache zeigen, Hinweis „offline“, erneut versuchen.
- `Directory.Build.targets` überspringt `RoG.App` außerhalb von Windows – `dotnet build`/`dotnet test` auf dev-linux
  bauen nur Core und Tests (Warnung NU1503 „Wiederherstellung übersprungen“ ist gewollt).

## API-Konventionen (wie Website/Mobile-App)

- Basis aus `Config/environments.json` (`production`, `staging`, `local`), nie Domains im Code
- Bearer-Token, `Accept: application/json`, `Accept-Language` = App-Sprache, User-Agent `RoG-Windows/<Version>`
- Fehler RFC 9457 → `ApiProblemException` mit `Problem.Code`; Anzeige über `Error_<code>` (ILocalizer.GetError)
- Schreibende Requests mit `Idempotency-Key` (je Aktion, bei Wiederholung gleich) – macht `RoGHttpHandler`
- ETag/`If-None-Match` für GET (Cache je Konto + Sprache), 401 → Token verwerfen + Login, 403 → Hinweis, 429 → `Retry-After`
- Cursor-Pagination, `filter[]`, `sort`, `include`; Uploads per pre-signed URL, nie über die API-Payload
- Echtzeit: Verbindungsdaten aus `GET /v1` → `data.realtime`, Autorisierung `POST /v1/broadcasting/auth`
- Login: System-Browser + PKCE + Loopback `http://127.0.0.1:<Port>/callback` (Guide 35), nie WebView

## Design

Gleiche Identität wie die Website, nativ umgesetzt (Fluent-Steuerelemente, RoG-Farben).

- Tokens in `Themes/RoGTheme.xaml`: `bg #1C1F26 · bg2 #242832 · bg3 #2D3240 · line #3A4050 · ink #F1EFEA ·
  muted #A8ACB6 · red #E0324A · action-red #D62C43 · gold #D4AE3C · green #3DDC84` (+ blue #4F8CFF für Voice)
- 90 % dunkel / 8 % Off-White / 2 % Akzent. Rot = Aktion/Status, Gold = Rang/Auszeichnung. Nur Dunkel-Design.
- Display: **Barlow Condensed** 600/700 in Versalien (eingebettet, SIL OFL, `Fonts/OFL.txt`), Text: Segoe UI Variable
- Status-Punkte: grün online · blau Voice · rot auf RoG-Server · gold im Spiel · grau offline
- Seiten nutzen nur Stile/Brushes aus `RoGTheme.xaml` – keine Einweg-Farben in Seiten
- Fenster: Seitenleiste (NavigationView) wie das Tablet-Layout der Mobile-App; schmal (< 600 epx) Menü-Knopf,
  mittel nur Symbole, breit (≥ 840 epx) aufgeklappt; Liste-Detail nebeneinander, Lesebreite max. 720 epx
- Jede Liste hat Lade-, Leer- und Fehlerzustand, jede Aktion ein Feedback (InfoBar/Toast)
- Barrierefreiheit: `AutomationProperties` überall, Tastatur vollständig bedienbar, sichtbarer Fokus, Kontrast AA,
  Animationen respektieren „Animationseffekte“ von Windows, Textskalierung bis 225 % ohne Bruch

## Sicherheit

- Geräte-Token nur in der **Windows-Anmeldeinformationsverwaltung** (`PasswordVault`), nie in Einstellungen, Logs, Caches
- Gerätename beim Login „Windows-App – PC von …“ (`DeviceName.ForWindows`)
- Keine API-Keys (Pterodactyl, Discord …) in der App; Pterodactyl-Konsole nur über kurzlebige Tokens aus der API
- Admin-Funktionen nur, wenn `/v1/me` + Scopes sie erlauben; 2FA-Vorgaben der API respektieren
- **Keine Secrets im Repo:** keine Zertifikate (`*.pfx`), keine Zugangsdaten zur Build-VM (die liegen in
  `~/.config/rog-windows/build-vm.env`), keine Store-Zugänge. Logs/Crash-Reports ohne Tokens und Inhalte.

## Datenschutz (wie Website, DSGVO, Betreiber Privatperson in Luxemburg)

- Kein Tracking, keine Analytics, keine fremden Schriften/Skripte; Bilder nur über die eigenen Server/CDN der Plattform
- Lokal gespeichert: Einstellungen (JSON im App-Datenordner), Token (PasswordVault), Konto-Metadaten (ohne Token),
  später Offline-Cache – alles verschwindet mit der Deinstallation; Abmelden löscht Token und Konto-Daten
- Push über WNS/FCM nur mit Einwilligung (`push_consent`, Art. 49 DSGVO, inhaltsfreie Pushes) – siehe Pflichtenheft
- Jede neue lokale Speicherung oder Übermittlung an Dritte: im Bauplan unter „Datenschutz“ nennen; Website-seitige
  Änderungen über `docs/API-LUECKEN.md`

## Sprache

- UI-Texte nur über Ressourcen (`x:Uid` bzw. `StringKeys`), **Deutsch zuerst**, EN/FR immer mitpflegen –
  `ReswCompletenessTests` prüfen fehlende/leere Schlüssel und jede `x:Uid`
- Inhalte kommen übersetzt von der API (`Accept-Language`), Fallback DE; Sprachwahl in den Einstellungen
  (Standard: Windows-Sprache, sonst DE), Wechsel nach Neustart
- Code, Bezeichner, Commits auf **Englisch**; Kommentare dürfen Deutsch sein
- **Berichte, Rückfragen und Zusammenfassungen an den Owner immer auf Deutsch.**

## Arbeitsweise und Git

- Bau-Reihenfolge aus `docs/WINDOWS-PFLICHTENHEFT.md`. Jede Stufe lauffähig, getestet, committet – weiter erst nach
  Wahkes „weiter“.
- Ein Feature ist fertig, wenn: Seite in DE/EN/FR, Lade-/Leer-/Fehlerzustand, Logik in Core mit Tests, Build auf der VM
  grün (sobald sie existiert), `docs/WINDOWS-BAUPLAN.md` aktualisiert.
- `develop` = laufende Entwicklung, `main` = Store-Releases (Fast-Forward nach Freigabe des Owners).
  Kein Remote/Push ohne Auftrag. Kleine, thematische Commits, Nachricht Englisch (`feat(core): …`, `docs: …`),
  letzte Zeile `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Vor jedem Commit:** `dotnet build RoG.sln` und `dotnet test RoG.sln` grün (dev-linux). Ab Existenz der VM bei
  Änderungen an `src/RoG.App` zusätzlich `scripts/remote-build.sh`.
- Subagenten: kein `git checkout`/`stash`/`reset` – nur die Hauptsitzung committet.
- Bei Unklarheit: sinnvollste Annahme, in `docs/WINDOWS-BAUPLAN.md` unter „Annahmen“ notieren, weiterbauen. Vorher fragen
  nur bei schwer Umkehrbarem (Store-Name, Publisher/Identität, Push-Anbieter, Zertifikate, Kosten).
- Nach jeder API-Änderung der Website: `scripts/generate-api.sh --from-website`, Build/Tests reparieren.

## Befehle

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"   # .NET SDK ohne root in ~/.dotnet (siehe README)
dotnet build RoG.sln && dotnet test RoG.sln             # Core + Tests unter Linux (App wird übersprungen)
scripts/check-app-syntax.sh                             # Syntax + x:Bind-Pfade von src/RoG.App unter Linux prüfen (vor jedem Push)
scripts/generate-api.sh [--from-website]                # OpenAPI → src/RoG.Core/Api/Generated/RoGApiClient.g.cs
scripts/remote-build.sh --check                         # Build-VM erreichbar? Werkzeuge da?
scripts/remote-build.sh [--mode build|sideload|store]   # auf der VM bauen/testen/paketieren, Ergebnis in artifacts/remote/
```
