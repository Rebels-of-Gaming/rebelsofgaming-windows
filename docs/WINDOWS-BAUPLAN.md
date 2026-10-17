# Windows-Bauplan – Rebels of Gaming für Windows

Laufend gepflegt. Grundlage: `docs/WINDOWS-PFLICHTENHEFT.md`, API-Spec `openapi/openapi.json` (Kopie aus
`/home/wahke/projects/rog-app/openapi/openapi.json`, Website **v0.29.0**, 15.10.2026).

## Stand

| Stufe | Inhalt | Status | Commit |
|---|---|---|---|
| 0 | Fundament | fertig: Core + Tests grün (Linux), **App baut auf dem Windows-Runner `rog-win-1`** (x64 + ARM64, CI-Job „WinUI-App (Windows)“), Store-Paket `.msixupload` entsteht; noch nie gestartet | siehe `git log` (develop) |
| 1 | Konto + Start | fertig (2.0.1): Konto, Abmelden, Gerät, Einstellungen, Live-Ticker, Einzelinstanz, Fensterzustand | siehe `git log` (develop) |
| 2 | Community lesen | fertig (2.4.0): News, Squads, **Spiele**, Mitglieder/Team, Termine (+ Zusage, **Monatsansicht**), Server/TeamSpeak/Discord mit „Verbinden“ und **Auslastung 24 h**, Matches/Hall of Fame/Statistiken/**Turniere**, Squad-**Statistiken**, Info, Suche; offen: Squad-Galerie als Reiter | siehe `git log` (develop) |
| 3 | Echtzeit + Desktop-Integration | fertig (2.4.0): Reverb, Glocke, Toasts, Tray, Badge, Sprungliste, `rog:`, Autostart, **Benachrichtigungs-Einstellungen** (Glocke/E-Mail/Discord je Ereignis, Sammel-Mail, Ruhezeit); WNS nach Lücke W1 | siehe `git log` |
| 4 | Kommunikation | fertig (2.3.x): DMs mit Pop-out, Forum lesen+schreiben, Zusagen, Tastenkürzel, Offline-Warteschlange (DMs nur im Speicher), Melden | siehe `git log` |
| 5 | Medien | fertig (2.3.4): Uploads (Drag & Drop, Strg+V, **Teilen-Ziel**), Galerie mit **Einreichen**, Downloads, Kurzlinks, Profil bearbeiten | siehe `git log` |
| 6 | Clan-Prozesse | fast fertig (2.2.2): Bewerben, eigene Bewerbungen, Votes, Abwesenheiten, Geräte/Datenexport/Konto löschen, Regel-Hinweis, Onboarding-Kachel, Statistiken (Ranglisten, Wochen-Awards), Mitgliederkarte im eigenen Fenster; offen: Trial-Verwaltung für Squad-Leitung (kommt mit Stufe 9) | siehe `git log` |
| 7 | Server-Steuerung | fertig (2.4.1): Steuer-Fenster mit Power, Ressourcen, Live-Konsole (Historie), Configs (Diff, Versionen, Rollback, Anträge), **Formular-Vorlagen** (Servername, Slots, Passwort, Map-Rotation), **Dateien** (Ordner, Vorschau, Sicherungen kopieren/umbenennen/löschen), Backups, RCON, Sperren, **Whitelist**, Vier-Augen-Freigaben, App-Sperre mit Windows Hello; Vorlagen ohne Dateizugriff nach Lücke W14 | siehe `git log` |
| 8 | Wiki | fast fertig (2.3.0): Bereiche, Seitenbaum, Suche, Seite nativ mit Links in der App, Offline-Kopien, Bearbeiten mit Vorschau und Konflikterkennung, Versionen mit Vergleich/Wiederherstellen, Beobachten; offen: neue Seite anlegen, Dateien/Bilder hochladen, Kategorien-Ansicht, Inhaltsverzeichnis mit Sprung | siehe `git log` |
| 9 | Verwaltung + Moderation | in Arbeit (2.3.x): Verwaltung mit Übersicht (Kennzahlen, Systemstatus, Audit-Log), Bewerbungen (Squad-Leitung), Moderation (Meldungen, verwarnen/sperren, Sperren, Mod-Log, Wortfilter); Mitglieder mit Rollen vergeben/entziehen; offen: Squads, Inhalte, Termine/Matches, Server-Verwaltung, Wiki-Freigaben … | siehe `git log` |
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

### Stufe 1 – Konto + Start (2.0.1, 16.10.2026)

- **Core:** `AccountService` (`/v1/me` → `UserProfile` mit Rechten je Bereich und Token-Scopes inkl. Platzhalter
  `bereich:*`, `POST /v1/devices` mit `platform: windows` + App-Version bei jedem Start, Abmelden widerruft den Token
  und löscht ihn lokal immer, Plattform-Version aus `GET /v1`), `LiveTickerService` (`/v1/live`),
  `WindowPlacement` (Fenster auf sichtbaren Bereich einpassen).
- **Generierter Client repariert** (`scripts/postprocess-client.py`): Enum-Werte gingen als `Windows` statt `windows`
  raus (System.Text.Json ignoriert `[EnumMember]`), weitere 2xx-Antworten (19 × `201 Created`) galten als Fehler,
  PHP-`[]` für leere Objekte brach das Lesen ab (`RoGApiClientJson.cs`).
- **App:** Konto-Seite (Profil-Kopf mit Rang in Gold, E-Mail, 2FA-Stand, Gerät, „Profil ansehen“, „Auf der Website
  verwalten“, Abmelden mit Rückfrage; Gastmodus → „Im Browser anmelden“), Einstellungen (Sprache mit Neustart-Hinweis,
  Versionen App/Plattform, Website/Status/Datenschutz, Lizenzen), Startseite mit Live-Ticker (Kacheln mit Symbol und
  Status-Farbe der API, alle 30 s still aktualisiert), Seitenleiste zeigt den Namen, „Verwaltung“ nur mit Verwaltungsrecht,
  **Einzelinstanz** (`Program.cs`, zweiter Start holt das Fenster nach vorn), **Fenstergröße/-position und letzter
  Bereich** werden gemerkt.
- Werkzeuge: `scripts/resw-set.py` (Texte DE/EN/FR setzen), `scripts/bump-version.sh`.
- Offen in Stufe 1: Liste-Detail-Gerüst und gemeinsame UI-Bausteine (kommen mit den ersten Listen in Stufe 2),
  Startseite mit Squads/News (Stufe 2).

### Stufen 3–5 (2.1.0, 16.10.2026)

- **Echtzeit:** `RealtimeHub` (Core) verbindet Reverb mit `live`, `servers` und `private-App.Models.User.{id}`
  (Laravel-Ereignis `Illuminate\Notifications\Events\BroadcastNotificationCreated`, `.inbox.changed`,
  `.messages.unread`), weitere Kanäle (Chat, Forum-Thema) per Abo; App-Seite `RealtimeService` (UI-Thread, Zähler).
- **Desktop:** Glocke mit Zähler (InfoBadge) und Benachrichtigungs-Seite, Windows-Toasts (`AppNotificationManager`,
  Klick öffnet das Ziel), Taskleisten-Badge, **Tray-Symbol** (eigenes `Shell_NotifyIcon`, Menü, Schließen in den
  Infobereich), **Sprungliste**, Protokoll **`rog:`** (Manifest), Autostart startet versteckt im Tray, Einstellungen
  für Toasts/Tray/Autostart, Tastenkürzel Strg+1…9, Strg+, und Strg+K.
- **Kommunikation:** Direktnachrichten (Liste, Chat mit Antworten, Enter/Umschalt+Enter, live, Pop-out-Fenster mit
  „immer im Vordergrund“, „Nachricht senden“ im Profil), Forum (Bereiche, Themen, Beiträge nativ, Reaktionen,
  Umfragen, Zitieren, BBCode-Leiste, Vorschau, Strg+Enter, neues Thema, live neue Beiträge).
- **Medien:** Uploads über pre-signed URL (Token geht nie an den Speicher), Drag & Drop, Strg+V-Screenshots,
  Link automatisch kopieren, Kurzlinks, Galerie mit Vollbild (Pfeile, Zoom), Downloads mit SHA-256-Prüfung und
  „Im Ordner zeigen“, Profil bearbeiten (Texte, Ort, Einwilligungen, Avatar, Banner).
- **Client-Korrekturen:** generierte Anfragen senden keine `null`-Felder mehr (ein PATCH hätte sonst Daten gelöscht).

### Stufen 6–7 (2.1.1–2.2.0, 16.10.2026)

- **Bewerbung:** Dialog aus dem Formular der API (Fragen je Typ, Geburtsdatum, Regelwerke, Trial-Zustimmung, Werber),
  Fehler wie `discord_required` im Dialog; „Meine Bewerbungen“ auf der Konto-Seite (Fortschritt, Ablauf, Aufgaben,
  Nachrichten, zurückziehen). Antworten gehen als Text – die Spec sagt Zahl (Lücke W9), deshalb sendet/liest die App
  diese zwei Endpunkte selbst.
- **Votes:** eigene Seite (`rog://votes`), auf der Startseite „Offene Abstimmungen (n)“ für Stimmberechtigte.
- **Konto:** Geräte & Daten (angemeldete Geräte abmelden, Datenexport anfordern und als ZIP laden, Konto löschen mit
  Passwort/Name als Bestätigung – Store-Pflicht), Abwesenheiten, Hinweis auf unbestätigte Regeln nach dem Login.
- **Server-Steuerung:** „Steuern“ auf Server-Karten nur bei `control` aus der API; eigenes Fenster je Server mit
  Start/Neustart/Stopp/Kill (Kill mit Rückfrage), CPU/RAM/Speicher/Laufzeit, Live-Konsole über den Wings-WebSocket
  (kurzlebiges Token aus `console-token`, `Origin` aus der Antwort, Erneuerung bei `token expiring`), Befehle über die
  API (Audit-Log). Vier-Augen-Pflicht (`can_command: false`) sperrt die Befehlszeile. Befehls-Historie mit ↑/↓.
- **Configs, Backups, RCON, Sperren, Freigaben** als Reiter im Steuer-Fenster – jeder Reiter nur mit dem passenden
  Recht aus `control.abilities`. Speichern erkennt `202` (Antrag bei Vier-Augen-Pflicht); Rollback, Wiederherstellen,
  Löschen, Sperre aufheben und Freigeben/Ablehnen mit Rückfrage. Gäste sperren per Spieler-ID/Name (Lücke W10:
  Body fehlt in der Spec), Mitglieder weiter über die Website (Rangprüfung). RCON-Ausgabe wird nicht gespeichert.
- **Statistiken** als Reiter auf der Matches-Seite (Spiel, Zeitraum, Wertung; eigener Platz in Gold; Wochen-Awards mit
  den Namen der Website), **Onboarding-Kachel** „Dein Start bei den Rebels“ auf der Startseite bis alles erledigt ist.
- **Mitgliederkarte** im eigenen Fenster (Knopf „Karte“ auf der Mitglieder-Seite): eigene schlanke Kachelkarte
  (Web-Mercator in Core, getestet), Kacheln nur über die signierte Vorlage der Plattform (alle 5 min auf Ablauf geprüft),
  Ziehen, Mausrad, +/−, Pfeiltasten, Pins je Stadt mit Mitgliederliste, Namensnennung © OpenStreetMap mit Link.
- **App-Sperre mit Windows Hello** (Einstellungen, nur wenn Hello eingerichtet; Einschalten erst nach erfolgreicher
  Prüfung): beim Start und nach 5 Minuten außerhalb des Vordergrunds; Ausweg „Stattdessen abmelden“.

### Nach 2.4.0 (17.10.2026)

- **2.4.1 – Server-Steuerung komplett:** Reiter „Dateien“ (nur mit `files_read`): Ordner mit Brotkrumen, Vorschau von
  Textdateien bis 512 KB, Sicherungskopie anlegen (Name endet immer auf `.bak`/`.old`, Vorschlag ohne Kollision),
  Sicherungen umbenennen/löschen – genau die Regeln aus Guide 18 (`operable`/`backup` aus der API, Server prüft).
  Reiter „Whitelist“ (mit `bans`): Einträge mit erkanntem Mitglied, Spielername hinzufügen, entfernen.
  **Formular-Vorlagen** im Config-Editor (Knopf „Formular“, wenn die Datei eine hat): Felder je Typ, Prüfung wie die
  Website (Bereich, Auswahl, keine `" ; \`, Map-Namen), leere Passwörter bleiben unverändert, „Passwort entfernen“
  sendet `""`; Anwenden wie Speichern (`after`, Notiz, `202` = Antrag). Lücken W13 (Body der Dateioperationen fehlt in
  der Spec) und W14 (`abilities.templates` fehlt).

### Offline, Melden, Verwaltung (2.3.1–2.3.3, 16.10.2026)

- **Offline-Warteschlange** (`Outbox`, Core): DMs, Forenantworten und Zusagen ohne Netz werden mit festem
  Idempotency-Key gemerkt und beim Start, bei Netzwechsel und jede Minute gesendet; Netz-/Server-/Rate-Fehler →
  später erneut, andere Fehler → verworfen mit Windows-Hinweis. **DMs nur im Speicher** (Pflichtenheft 7).
- **DPAPI** (`IPayloadProtector`, App: `DataProtectionProvider("LOCAL=user")`) für Wiki-Offline-Kopien und die
  Warteschlange.
- **Melden** (Chat-Kontextmenü, Forenbeitrag, News-Kommentar, Profil) mit Grund und Kommentar.
- **Verwaltung:** Übersicht (Kennzahlen, Systemstatus mit Ampel, Audit-Log mit Suche), Bewerbungen für die
  Squad-Leitung (Liste/Detail, Entscheidungen, Aufgaben, Notizen, Erinnern), Moderation.
- **Teilen-Ziel „An RoG hochladen“** (Manifest `windows.shareTarget`, Bilder/Clips/Bitmaps): geteilte Daten landen in
  einem Eingang im lokalen Cache – bei laufender App kopiert sie der zweite Prozess vor dem Weiterreichen –, der
  Uploader lädt sie hoch und räumt auf. **Galerie einreichen** mit Pflicht-Bestätigung der Rechte.
- **Werkzeug:** `scripts/check-app-syntax.sh` **kompiliert den C#-Teil der App unter Linux** (`tools/AppCompileCheck`:
  `EnableWindowsTargeting`, Referenzen des Windows App SDK ohne dessen Build-Targets, alle Analyzer; XAML-generierter
  Code als Platzhalter aus `xaml_stubs.py`) und prüft die `x:Bind`-Pfade. Läuft auch im Linux-Job der CI.
- **XAML-Fallstrick:** Findet der XAML-Compiler einen `x:Bind`-Namen nicht, stürzt er teils beim Formulieren der
  Meldung ab („WMC9999 … ErrorMessages.resources“, ohne Datei/Zeile). Ursache hier: `[RelayCommand]` in einem
  `record` – die Toolkit-Generatoren erzeugen in records nichts. Abhilfe: ViewModels mit Commands/Observable immer als
  `partial class`; `scripts/check-xaml-bindings.py` prüft alle `x:Bind`-Pfade gegen die Quellen (läuft mit
  `check-app-syntax.sh`). Bei Fehlern sichert die CI `input.json`/`output.json` des XAML-Compilers (dort steht die
  Seite und ein Stacktrace). `CheckBox.IsChecked` (TwoWay) bindet vorsichtshalber an `bool?`.

### Stufe 8 – Wiki (2.3.0, 16.10.2026)

- **Core:** `WikiService` (Bereiche, Baum flach mit Tiefe, Seite per ID oder Adresse inkl. Weiterleitungen, Versionen,
  Quelltext einer Version, Wiederherstellen, Vorschau `POST /wiki/render`, Speichern mit `base_revision_id` →
  `WikiEditConflictException` mit aktueller Fassung aus `409 edit_conflict`, Beobachten, Suche – `q` selbst gesendet,
  Lücke W11), `LineDiff` (LCS, zeilenweise), `WikiOfflineStore` (JSON je Seite+Sprache, beim Abmelden gelöscht).
  `ApiProblem` merkt sich den rohen Body für Zusatzfelder (`TryGetExtension`).
- **App:** Wiki-Seite (Liste-Detail): Bereich, Suche, eingerückter Baum; Seite mit Meta, Beobachten, Browser,
  Bearbeiten (Quelltext, Zusammenfassung, Vorschau, Speichern; bei Konflikt Unterschiede + bewusstes Überschreiben),
  Versionen (vergleichen, wiederherstellen). Ohne Netz: Liste und Seiten aus der Offline-Kopie.
- **Links in allen Inhalten** (News, Forum, Info, Wiki) öffnen Ziele der Plattform jetzt in der App (`AppRoute`),
  alles andere im Browser.

### Stufe 2 – Community lesen (16.10.2026)

- **Gemeinsame Bausteine:** `HtmlContentParser` (Core, AngleSharp) → `RichDocument` → `RichContentView` (App, nativ:
  Absätze, Überschriften, Listen, Zitate, Code, Bilder nur von Plattform-Hosts über https, Spoiler als Expander,
  Tabellen; Links nur http/https/mailto), `ListDetailPanel` (≥ 760 epx nebeneinander, sonst mit Zurück/Alt+←/Esc),
  `StateView` (Laden/Fehler mit „Erneut versuchen“/Leer), `LoadableViewModel`, `ServerCard`, `Page<T>`/`ApiValues`,
  `AppRoute` (Website-/Wiki-Links → Bereich in der App).
- **News** (Liste mit Titelbild, Kategorie, Lesezeit, „Mehr laden“; Artikel nativ, Link kopieren, Kommentare lesen),
  **Squads** (Kopfbild, Logo, Rekrutierung, Trainingszeiten, Clan-Infos, Einladungs-/Ref-Links, Server mit „Verbinden“,
  Line-up mit Rollenfarben), **Mitglieder** (Suche Strg+F, nur online, Team-Ansicht mit Zuständigkeiten, Profil mit
  Banner, Status-Punkt, Rang, Abzeichen, Spiele-Accounts), **Termine** (60 Tage nach Tagen, Zusage/Vielleicht/Absage,
  Server des Termins mit „Verbinden“, „In Kalender übernehmen“ als .ics), **Server** mit Reitern Gameserver /
  **TeamSpeak** (Kanalbaum, „Verbinden“ über `ts3server://` mit RoG-Name) / **Discord-Voice**, **Matches** (Ergebnis-
  farben, Maps, Line-up, Links) + **Hall of Fame** (Trophäen mit Medaillenfarbe, Meilensteine, Ehrenmitglieder),
  **Info** (Regeln mit nummerierten Abschnitten und „Gelesen und akzeptiert“, CMS-Seiten, Rechtstexte),
  **Suche** (Strg+K, Treffer öffnen den Bereich in der App), Startseite mit neuesten News und nächsten Terminen.
- Offen in Stufe 2: Spiele-Liste, Turniere als eigene Ansicht, Squad-Reiter Galerie/Stats/Matches, Auslastungsgraph
  der Server, Monats-/Wochenansicht im Kalender.

### Vorgezogen aus Stufe 2 – Server verbinden (16.10.2026)

- **Core** `Features/Servers/`: `ServerService` (`GET /v1/servers`, Cursor-Pagination, eigenes Modell `GameServer`
  statt generierter Typen), `ServerConnector` (Link aus `connect_url` der API, nur die Schemata, die die Website beim
  Speichern erlaubt: `steam`, `et`, `wolf`, `minecraft`, `http(s)`; Programm vorhanden? → starten, sonst Adresse kopieren).
  22 neue Tests.
- **App** `ServersPage`: Karten mit Status-Punkt, Spiel, Map, Spieler/Slots, Adresse (markierbar), Spielerliste
  (nur Mitglieder, von der API), „Verbinden“ + „Adresse kopieren“, InfoBar mit Hinweis je Fall (Steam fehlt → `connect`,
  ET fehlt → `/connect`, Minecraft → Mehrspieler → Direkt verbinden), Lade-/Leer-/Fehlerzustand, F5/Strg+R,
  stille Aktualisierung jede Minute. Auch im Gastmodus (API: `auth.optional`).
- Noch offen für die Server-Seite (Stufe 2): Detailansicht mit Auslastungsgraph (`/servers/{id}/history`), Filter
  nach Spiel/Squad, Profil-Links aus der Spielerliste, TeamSpeak `ts3server://`.

### Stufe 0 – bewusst offen

- **App gebaut, aber nie gestartet.** Erster grüner Build am 15.10.2026 auf `rog-win-1` (einzige XAML-Korrektur:
  Theme-Wörterbücher vor die direkten Einträge, WMC0035; danach CA1001 behoben, `TreatWarningsAsErrors` jetzt auch
  für `RoG.App` an). Store-Modus erzeugt `RoG.App_0.1.0.0_x64_ARM64_bundle.msixupload`. Start/Bedienung ungetestet.
- **Sideload-Pakete:** `build-windows.ps1` legt das Testzertifikat selbst im Speicher des Runner-Dienstkontos an und
  legt `RoG-Test.cer` ins Artefakt (`docs/BUILD-VM.md` Abschnitt 6).
- Reverb noch nicht in der App verdrahtet (nur Core + Tests) → Stufe 3
- Keine Tray-/Toast-/Jumplist-Funktionen → Stufe 3
- ETag-Cache nur im Speicher; persistenter Offline-Cache → Stufe 4
- CI: GitHub Actions mit Linux-Runner (Core + Tests) und Windows-Runner `rog-win-1` (App, MSIX über `mode=Store|Sideload`)

## Datenschutz (Stand Stufe 0)

- Lokal: `settings.json` (Sprache, Umgebung, Autostart, Tray, Gastmodus – keine personenbezogenen Daten),
  `account.json` (Konto-ID, Anzeigename, Scopes, Gerätename – **ohne** Token), Token in der Windows-Anmeldeinformationsverwaltung.
  Alles im Paket-Datenordner bzw. Tresor des Windows-Benutzers; Deinstallation entfernt die Dateien, Abmelden löscht Token + `account.json`
  (Tresor-Eintrag bleibt bei Deinstallation ggf. bestehen → in Stufe 1 beim ersten Start ohne `account.json` aufräumen).
- An die Website geht beim Login der Gerätename mit dem **Windows-Benutzernamen** (sichtbar unter Konto → Apps & Geräte).
- Ab 2.1.1: Datenexport (ZIP) speichert die App nur auf Wunsch im Ordner „Downloads“ des Benutzers. Die Server-Konsole
  verbindet direkt mit dem Pterodactyl-Node der Plattform (eigener Server, kurzlebiges Token aus der API). Die App-Sperre speichert nur Ein/Aus in `settings.json`;
  die Hello-Prüfung macht Windows, die App sieht keine biometrischen Daten.
- Ab 2.2.2: Kartenkacheln kommen nur vom Server der Plattform (signierte Vorlage, nie direkt von OpenStreetMap);
  sie liegen höchstens im Bild-Cache von WinUI während der Laufzeit.
- Ab 2.3.0: gelesene Wiki-Seiten (gerendertes HTML, Titel, Bereich) unter `wiki/` im App-Datenordner, je Sprache,
  **mit DPAPI verschlüsselt** (ab 2.3.3); Abmelden löscht den Ordner, Deinstallation ebenso.
- Ab 2.3.3: Offline-Warteschlange `outbox.dat` (Forenantworten, Zusagen; DPAPI-verschlüsselt) – **Direktnachrichten
  nie auf der Platte**, nur im Speicher bis zum Senden. Abmelden leert die Warteschlange.
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
7. **Version (Start 0.1.0, ab 16.10.2026 2.0.0 wegen der alten Store-App 1.0.4.0) / MSIX X.Y.Z.0.** Store verlangt eine vierte Stelle 0; `Directory.Build.props` (`RoGVersion`) ist die
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
15. **App und MSIX werden mit `dotnet build` gebaut, nicht mit MSBuild.exe.** Den Build Tools 2026 auf `rog-win-1` fehlt
    der .NET-SDK-Resolver (MSB4236, vswhere meldet `isComplete: 0`); die MSIX-Werkzeuge kommen ohnehin aus dem NuGet-Paket
    `Microsoft.Windows.SDK.BuildTools.MSIX`. Visual Studio ist damit für CI nicht nötig. Folge: **kein Symbolpaket**
    (`.appxsym`, braucht `mspdbcmf.exe` aus Visual Studio) – Absturzberichte im Partner Center zeigen dann keine
    Funktionsnamen. Bei Bedarf später Build Tools vervollständigen und `AppxSymbolPackageEnabled` im Skript wieder an.
16. **RuntimeIdentifier aus `Platform`** (`RoG.App.csproj`), damit der Bündel-Build je Plattform ohne `-r` läuft.
17. **Minecraft ohne Direkt-Verbinden.** Minecraft Java hat kein Link-Schema zum Verbinden; die API liefert für
    `query_type=minecraft` deshalb kein `connect_url`. Die App kopiert die Adresse und erklärt den Weg im Spiel.
    Trägt die Verwaltung einen eigenen `minecraft://…`-Link ein (Bedrock: `minecraft://?addExternalServer=Name|host:port`),
    startet die App ihn wie die anderen. `et://` braucht ET: Legacy (registriert das Schema unter Windows); fehlt es,
    wird die Adresse kopiert. Geprüft wird vorher mit `Launcher.QueryUriSupportAsync`.
18. **Keine Umgebungswahl in den Einstellungen.** Ein Produktions-Token würde sonst an Staging gehen; die Umgebung
    lässt sich nur in `settings.json` ändern (Entwicklung).
19. **„Verwaltung“ in der Seitenleiste** erscheint, sobald `/v1/me` ein Recht auf `.manage`, `roles.*`, `moderation.*`
    oder `admin.*` liefert; die einzelnen Bereiche prüfen später ihr genaues Recht + Scope.

20. **Konsolenbefehle über die API statt über den Socket.** `POST /v1/servers/{slug}/commands` landet im Audit-Log und
    prüft Vier-Augen-Pflicht und Scope serverseitig; der Wings-Socket dient nur zum Lesen.
21. **Datenexport landet im Ordner „Downloads“** als `rog-datenexport-<Datum>.zip`, wie Downloads aus Medien.

22. **Mitgliederkarte ohne Karten-Bibliothek.** Die WinUI-`MapControl` braucht Azure Maps (Fremdanbieter, Schlüssel);
    die App zeichnet die Kacheln der Plattform selbst. Pins nach Ort gruppiert wie die API, ohne weitere Cluster.

## Offene Fragen (an Wahke)

1. ~~Store-Name und Publisher~~ – geklärt 16.10.2026: bestehende App „Rebels of Gaming“ wird weiterverwendet
   (`docs/STORE.md` Abschnitt 2). Herausgeber bleibt „wahke“ (Wahke, 17.10.2026).
2. ~~Build-VM~~ – geklärt: eigener Windows-Runner `rog-win-1` (GitHub Actions, self-hosted).
3. ~~Push für Windows~~ – entschieden 17.10.2026: vorerst nur Toasts bei laufender App (auch im Tray); WNS (W1) später.
4. ~~Eigene Client-ID~~ – entschieden 17.10.2026: ja, `rog-windows` (Lücke W6 an die Website). Bis sie dort
   eingetragen ist, meldet sich die App weiter als `rog-app` an; danach nur `clientId` in `Config/environments.json` ändern.
5. **Crash-Reporting:** Wahke prüft die Optionen (keines / selbst gehostet, z. B. GlitchTip / nur lokale Logs). Bis dahin keines.
6. ~~Mindest-Windows-Version~~ – entschieden 17.10.2026: Windows 10 1809 bleibt.
7. ~~Store-Einreichung~~ – entschieden 17.10.2026: nach Wahkes Test des Pakets 2.4.0 (vorher Fast-Forward `main`).
