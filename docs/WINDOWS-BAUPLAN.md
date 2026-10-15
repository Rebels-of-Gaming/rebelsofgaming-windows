# Windows-Bauplan – Rebels of Gaming für Windows

Laufend gepflegt. Grundlage: `docs/WINDOWS-PFLICHTENHEFT.md`, API-Spec `openapi/openapi.json` (Kopie aus
`/home/wahke/projects/rog-app/openapi/openapi.json`, Website **v0.29.0**, 15.10.2026).

## Stand

| Stufe | Inhalt | Status | Commit |
|---|---|---|---|
| 0 | Fundament | fertig auf dev-linux (Core + Tests grün); **App-Projekt noch nie gebaut** – wartet auf die Build-VM | siehe `git log` (develop) |
| 1 | Konto + Start | offen | |
| 2 | Community lesen | offen | |
| 3 | Echtzeit + Desktop-Integration | offen | |
| 4 | Kommunikation | offen | |
| 5 | Medien | offen | |
| 6 | Clan-Prozesse | offen | |
| 7 | Server-Steuerung | offen | |
| 8 | Wiki | offen | |
| 9 | Verwaltung + Moderation | offen | |
| 10 | Feinschliff + Store | offen | |

### Stufe 0 – was steht (15.10.2026)

- **.NET SDK 10.0.402** ohne root in `~/.dotnet` (`dotnet-install.sh --channel 10.0`), PATH-Hinweis in README/CLAUDE.md
- **Lösung `RoG.sln`** (klassisches .sln-Format): `src/RoG.Core` (`net10.0`), `tests/RoG.Core.Tests` (xUnit),
  `src/RoG.App` (WinUI 3, `net10.0-windows10.0.19041.0`). `Directory.Build.props/.targets` überspringen die App außerhalb
  von Windows (bedingter Import von `build/SkipOnNonWindows.targets`), zentrale Paketversionen in `Directory.Packages.props`.
- **API-Client** aus der Spec mit NSwag 14.7 (`scripts/generate-api.sh`, lokales Tool in `.config/dotnet-tools.json`):
  ~133 000 Zeilen, 443 Pfade, System.Text.Json, nullable Referenztypen, `DateTimeOffset`. Vorverarbeitung
  `scripts/normalize-openapi.py` (ungültige `in: body`-Parameter) und Umbenennung kollidierender Klassen (`System` → `SystemInfo`, `Type` → `TypeInfo`).
- **HTTP-Pipeline** `RoGHttpHandler`: Bearer aus `ITokenStore`, `Accept`, `Accept-Language`, User-Agent `RoG-Windows/<Version>`,
  `Idempotency-Key` für POST/PUT/PATCH/DELETE (bei Wiederholung gleich, Body gepuffert), ETag/`If-None-Match` mit
  transparenter 304-Antwort (Cache je Konto-Hash + Sprache), Fehler nach RFC 9457 → `ApiProblemException`
  (`code`, `errors`, `Retry-After`; OAuth-`error` als Rückfall), 401 → Ereignis `Unauthorized`, 429/503 mit kurzem
  `Retry-After` und Netzwerkfehler werden höchstens zweimal wiederholt.
- **App-Login** `AppLoginFlow` nach Guide 35: PKCE S256, `state` (konstante Zeit), Loopback-Empfänger auf 127.0.0.1 mit
  zufälligem Port (eigene kleine HTTP-Antwortseite im RoG-Design, DE/EN/FR), Fehler-Rücksprünge, Timeout 15 min,
  Code-Tausch `POST /v1/auth/app/token`, Gerätename „Windows-App – PC von …“, Scopes aus `/v1/auth/scopes`.
- **Echtzeit** `ReverbClient`: Pusher-Protokoll 7 über `ClientWebSocket`, Daten aus `GET /v1` → `data.realtime`,
  öffentliche/private/Presence-Kanäle (Autorisierung `POST /v1/broadcasting/auth`), Mitgliederlisten, Ping/Pong und
  eigener Ping nach `activity_timeout`, `pusher:error` 4000–4099 ohne Neuversuch, sonst Neuverbindung mit Backoff
  (1 → 30 s) und erneutem Abonnieren, Ereignis `Reconnected` zum Nachladen.
- **Einstellungen** (`AppSettings`, JSON atomar im App-Datenordner), **Sprachwahl** (DE Standard, EN, FR),
  **Umgebungen** in `Config/environments.json` (production, staging, local), **Übersetzungsschlüssel** `StringKeys` +
  `ILocalizer`.
- **WinUI-App (Gerüst):** `NavigationView` mit allen Bereichen (Tablet-Layout der Mobile-App, Schwellen 600/840 epx),
  eigene Titelleiste, `RoGTheme.xaml` (Tokens, Status-Farben, Textstile, Karten, Knöpfe, WinUI-Akzent = Rot),
  Barlow Condensed 600/700 eingebettet (OFL), Login-Seite („Im Browser anmelden“, „Ohne Anmeldung umsehen“, Warten +
  Abbrechen, Fehler-InfoBar), Startseite (Platzhalter-Ticker), Platzhalterseite für alle anderen Bereiche,
  `PasswordVaultTokenStore`, Browser-Start über `Launcher`, `ResourceLoader`-Übersetzungen,
  `Strings/{de-DE,en-US,fr-FR}/Resources.resw` (66 Schlüssel), MSIX-Manifest (Platzhalter-Identität, Autostart-Task aus,
  nur `internetClient` + `runFullTrust`), Icons/Splash aus `resources/brand/` (scale-200, targetsize).
- **Build-Skripte:** `scripts/build-windows.ps1` (VM: restore, build, test mit TRX, App x64/ARM64, MSIX Sideload/Store,
  `summary.json`), `scripts/remote-build.sh` (dev-linux: Arbeitskopie als tar über SSH, Ausführung per
  `pwsh -EncodedCommand`, Ergebnisse nach `artifacts/remote/<lauf>/`, alte Läufe aufräumen, `--check`).
- **Tests:** 111 xUnit-Tests (PKCE inkl. RFC-7636-Vektor, Loopback über echte Sockets, Login-Ablauf mit Fake-Browser,
  Problem Details, Pipeline inkl. Idempotency/ETag/Retry/401, Reverb mit Fake-Socket inkl. Presence/Reconnect,
  Einstellungen, Sprachauflösung, .resw-Vollständigkeit + `x:Uid`, Manifest-Version/Assets/Capabilities).

### Stufe 0 – bewusst offen

- **App nie gebaut/gestartet** – keine Windows-Maschine. Erwartbar sind beim ersten VM-Build kleinere Korrekturen (XAML,
  Windows App SDK 2.x-APIs, Schriftnamen). `TreatWarningsAsErrors` ist für `RoG.App` deshalb vorerst aus.
- Keine Geräte-Registrierung (`POST /v1/devices`), kein `/v1/me`, kein Abmelden in der Oberfläche → Stufe 1
- Reverb noch nicht in der App verdrahtet (nur Core + Tests) → Stufe 3
- Keine Tray-/Toast-/Jumplist-Funktionen, kein Single-Instance → Stufe 1/3
- ETag-Cache nur im Speicher; persistenter Offline-Cache → Stufe 4
- Keine CI; Build-VM existiert noch nicht (Anleitung `docs/BUILD-VM.md`)

## Datenschutz (Stand Stufe 0)

- Lokal: `settings.json` (Sprache, Umgebung, Autostart, Tray, Gastmodus – keine personenbezogenen Daten),
  `account.json` (Konto-ID, Anzeigename, Scopes, Gerätename – **ohne** Token), Token in der Windows-Anmeldeinformationsverwaltung.
  Alles im Paket-Datenordner bzw. Tresor des Windows-Benutzers; Deinstallation entfernt die Dateien, Abmelden löscht Token + `account.json`
  (Tresor-Eintrag bleibt bei Deinstallation ggf. bestehen → in Stufe 1 beim ersten Start ohne `account.json` aufräumen).
- An die Website geht beim Login der Gerätename mit dem **Windows-Benutzernamen** (sichtbar unter Konto → Apps & Geräte).
- Keine Übermittlung an Dritte, keine Telemetrie (`DOTNET_CLI_TELEMETRY_OPTOUT` auch in den Skripten). Schriften eingebettet.

## Verzeichnisstruktur

```
RoG.sln, global.json, Directory.Build.props/.targets, Directory.Packages.props, .config/dotnet-tools.json (NSwag)
build/SkipOnNonWindows.targets
openapi/openapi.json, openapi/nswag.json
scripts/generate-api.sh, normalize-openapi.py, build-windows.ps1, remote-build.sh, build-vm.env.example
src/RoG.Core/{Api,Api/Generated,Auth,Realtime,Settings,Localization,Config}
src/RoG.App/{App.xaml,MainWindow.xaml,Views,ViewModels,Services,Themes,Strings,Assets,Fonts,Properties,Package.appxmanifest,app.manifest}
tests/RoG.Core.Tests/{Api,Auth,Realtime,Settings,Localization,Support}
docs/
```

## Annahmen

1. **NSwag statt Kiota** für den API-Client. Kiota 1.36 (Microsoft.OpenApi mit 3.1-Unterstützung) bricht an der aktuellen
   Spec ab: Parameter `in: body` bei `DELETE /me`, doppelte Pfadsignatur `/pages/{slug}` vs. `/pages/{page}` und das leere
   Schema `PreviewRecurrence` (`allOf` ohne Inhalt) – jeder Fix offenbart den nächsten. NSwag 14.7 erzeugt aus derselben
   Spec mit einer kleinen Vorverarbeitung kompilierbaren Code, versteht `type: [x, "null"]` (3.1) und liefert eine
   Datei ohne Laufzeit-Abhängigkeit (System.Text.Json). Nachteile: Methodennamen aus der `operationId`
   (`V1_devices_indexAsync`), anonyme Typen wie `Body143`/`Response448` (instabil bei Spec-Änderungen) – deshalb
   nutzt die App den generierten Client über eigene Core-Services, nie direkt aus ViewModels. Spec-Fehler stehen als
   Lücken W3–W5 in `docs/API-LUECKEN.md`; sind sie behoben, Kiota erneut prüfen.
2. **Loopback mit `TcpListener` statt `HttpListener`.** `HttpListener` nutzt unter Windows http.sys; Präfixe außer
   `localhost` können eine URL-ACL (Admin) brauchen, die Website erlaubt aber nur IP-Literale (`127.0.0.1`). Ein Socket
   auf 127.0.0.1 braucht keine Rechte, verhält sich unter Windows und Linux gleich und ist so testbar. Nur IPv4.
3. **Client-ID `rog-app`** (gleiche wie Mobile-App, Guide 35 erlaubt Loopback für jede `client_id`). Eigene ID
   `rog-windows` (eigener Anzeigename im Browser-Hinweis) wäre eine Website-Konfiguration (`APPS_CLIENT_IDS`) – Lücke W6, niedrig.
4. **Gerätename aus dem Windows-Benutzernamen** (`Environment.UserName`), weil vor dem Login kein RoG-Name bekannt ist.
   Ändern lässt er sich auf der Website; die App sendet beim Geräte-Update keinen Namen (wie Mobile-App Annahme 15).
5. **Scopes:** `meta.app_scopes` ohne `tokens:manage`, `webhooks:*`, `translations:manage`, `legal:manage`, `bot`
   (Bereiche gibt es in der App nicht; Sicherheitshinweis Guide 35). Rückfall offline: Mitglieder-Scopes.
6. **Token-Speicher:** Token im `PasswordVault`, Metadaten ohne Token als JSON. Der Tresor ist auf ca. 2,5 KB je Passwort
   begrenzt – deshalb nicht das ganze `StoredToken` als JSON hinein.
7. **Version 0.1.0 / MSIX 0.1.0.0.** Store verlangt eine vierte Stelle 0; `Directory.Build.props` (`RoGVersion`) ist die
   Quelle, das Manifest wird von Hand nachgezogen (Test prüft Gleichheit). Details `docs/VERSIONIERUNG.md`.
8. **Windows App SDK 2.5.1, Microsoft.Windows.SDK.BuildTools 10.0.28000.2705, CommunityToolkit.Mvvm 8.4.2** (neueste
   stabile Versionen laut NuGet am 15.10.2026). `TargetFramework net10.0-windows10.0.19041.0`, `TargetPlatformMinVersion`
   10.0.17763.0, `MaxVersionTested` 10.0.26100.0, Plattformen x64 + ARM64 (kein x86).
9. **Schriftnamen** in XAML: `#Barlow Condensed` (Bold) und `#Barlow Condensed SemiBold` (Name-ID 1 der TTF-Dateien) –
   auf der VM prüfen; falls WinUI nach der typografischen Familie (ID 16) auflöst, beide auf `#Barlow Condensed` + `FontWeight`.
10. **Icons** aus `resources/brand/icon/rog-icon-1024.png` (Abzeichen, transparent) in den Größen der WinUI-Vorlage
    (scale-200, targetsize-24/48/256 unplated) erzeugt (ImageSharp, einmalig, Werkzeug nicht im Repo); Kachel/Splash
    mit Hintergrund `#1C1F26`. `AppIcon.ico` = `favicon/favicon.ico` (16/32/48 px) – größere Größen kommen mit dem Feinschliff.
11. **Nur Dunkel-Design** (`RequestedTheme="Dark"`), WinUI-Akzentfarbe auf RoG-Rot umgebogen; Primärknopf `action-red #D62C43`
    mit Off-White (Kontrast ≥ 4,5:1).
12. **Sprachwechsel wirkt nach Neustart** (`ApplicationLanguages.PrimaryLanguageOverride` vor dem ersten Laden der Ressourcen).
13. **Kein Push in Stufe 0–2.** Guide 35 nennt für Windows `fcm`; Firebase Cloud Messaging hat für Windows-Desktop-Apps
    kein SDK. Native Lösung ist WNS → Lücke W1. Bis dahin Toasts nur, solange die App läuft (Tray).
14. **Remote-Build per tar über SSH** (nicht `git bundle`): enthält auch nicht committete Änderungen (Arbeitskopie,
    `--committed` für HEAD), braucht auf der VM kein Git-Remote; Windows bringt `tar.exe` mit. PowerShell-Befehle per
    `-EncodedCommand`, damit die Standard-Shell des OpenSSH-Servers egal ist.

## Offene Fragen (an Wahke)

1. **Store-Name und Publisher:** Name im Partner Center reservieren („Rebels of Gaming“?), Konto als Einzelperson
   (Privatperson in Luxemburg, Anzeigename = Publisher Display Name) – danach `Identity Name`/`Publisher` ins Manifest.
2. **Build-VM:** Wo läuft sie (eigener PC mit Hyper-V, Azure, AWS …)? Siehe `docs/BUILD-VM.md` Abschnitt 1.
3. **Push für Windows:** WNS auf der Website ergänzen (Lücke W1) oder vorerst nur Toasts bei laufender App?
4. **Eigene Client-ID** `rog-windows` (Lücke W6) – gewünscht?
5. **Crash-Reporting** (wie Mobile-App offen): keines, selbst gehostet (z. B. GlitchTip/Sentry) oder nur lokale Logs?
6. **Mindest-Windows-Version:** Windows 10 1809 (Standard der Windows App SDK) oder nur Windows 11?
