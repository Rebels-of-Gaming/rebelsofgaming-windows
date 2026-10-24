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
| 6 | Clan-Prozesse | fertig (2.2.2): Bewerben, eigene Bewerbungen, Votes, Abwesenheiten, Geräte/Datenexport/Konto löschen, Regel-Hinweis, Onboarding-Kachel, Statistiken (Ranglisten, Wochen-Awards), Mitgliederkarte im eigenen Fenster; Trial-Verwaltung über „Bewerbungen“ in der Verwaltung (Status Trial, Aufgaben, verlängern, aufnehmen) | siehe `git log` |
| 7 | Server-Steuerung | fertig (2.4.1): Steuer-Fenster mit Power, Ressourcen, Live-Konsole (Historie), Configs (Diff, Versionen, Rollback, Anträge), **Formular-Vorlagen** (Servername, Slots, Passwort, Map-Rotation), **Dateien** (Ordner, Vorschau, Sicherungen kopieren/umbenennen/löschen), Backups, RCON, Sperren, **Whitelist**, Vier-Augen-Freigaben, App-Sperre mit Windows Hello; Vorlagen ohne Dateizugriff nach Lücke W14 | siehe `git log` |
| 8 | Wiki | fertig (2.4.2): Bereiche, Seitenbaum, Suche, Seite nativ mit Links in der App, Offline-Kopien, Bearbeiten mit Vorschau und Konflikterkennung, Versionen mit Vergleich/Wiederherstellen, Beobachten, **neue Seite anlegen**, **Bild einfügen** (Upload + Wiki-Datei), **Kategorien**, **Inhaltsverzeichnis mit Sprung** | siehe `git log` |
| 9 | Verwaltung + Moderation | fertig (2.4.7; seltene Einrichtungsseiten als Direktlinks zur Website): Verwaltung mit Übersicht (Kennzahlen, Systemstatus, Audit-Log), Bewerbungen (Squad-Leitung), Moderation (Meldungen, verwarnen/sperren, Sperren, Mod-Log, Wortfilter); Mitglieder mit Rollen vergeben/entziehen; **Squads** (2.4.3: Profil, Logo/Header, Line-up mit Rollen, archivieren); **News-Redaktion** (2.4.4); **Matches eintragen** (2.4.5); **Teilnehmer und Aufstellungen** an Terminen (2.4.6); **Galerie-Freigaben, Upload-Meldungen, Speicher, Website-Verwaltung als Direktlinks** (2.4.7); Wiki-Freigaben … | siehe `git log` |
| 10 | Feinschliff + Store | in Arbeit (2.5.0 = Store-Kandidat, Wahke 17.10.): Website-Verwaltung in der App (26 Bereiche); 2.4.8: Screenreader-Prüfung in CI (`scripts/check-a11y.py`), Animationen nach Windows-Einstellung, Store-Texte DE/EN/FR (`docs/STORE-TEXTE.md`); offen: Startzeit/ReadyToRun auf der VM messen, Screenshots, WACK, Einreichung nach Wahkes Test | |

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

- **2.4.2 – Wiki komplett:** „Neue Seite“ (Titel, Format Wikitext/Markdown, Elternseite aus dem Baum, Inhalt,
  Zusammenfassung; Prüfpflicht → Vorschlag), Umschalter „Kategorien“ (Kategorien mit Anzahl → Seiten der Kategorie),
  Inhaltsverzeichnis aus `toc` mit Sprung zur Überschrift, „Bild einfügen“ im Editor: Upload mit der Sichtbarkeit des
  Bereichs (`upload_visibility`), Registrierung über `POST /v1/wiki/files` (Body fehlt in der Spec → W13), Einbinden als
  `[[Datei:…|mini]]` bzw. Markdown-Bild.

- **2.4.3 – Squads verwalten:** Knopf „Verwalten“ auf der Squad-Seite (Rechte `squads.manage`/`squad.members` im
  Scope `squad:{id}` plus Token-Scope `squads:write`) öffnet ein eigenes Fenster: Profil (Kurzbeschreibung, Vorstellung,
  Trainingszeiten, Rekrutierung, Clan-Tag/-Name/-Level, Anforderungen – Texte nur in der App-Sprache, andere Sprachen
  bleiben), Logo/Header (Bild öffentlich hochladen, dann `upload_id`; entfernen), Line-up (Rolle aus
  `GET …/roles`, Ersatz, speichern/entfernen; Mitglied über die Suche hinzufügen), Archiv (nur global `squads.manage`).
  PATCH sendet nur geänderte Felder. Die Squad-Seite lädt nach dem Schließen neu, wenn sich etwas geändert hat.
  **Annahme:** Logo/Header-Uploads sind öffentlich (Pflicht laut Changelog „öffentliches Bild aus der Bibliothek“).

- **2.4.4 – News-Redaktion:** „Schreiben“ auf der News-Seite (Recht `news.write`/`news.publish`, Scope `news:write`),
  „Bearbeiten“ am Artikel nur mit `news.publish`. Eigenes Fenster: Entwürfe und geplante News links, Editor rechts mit
  Sprache DE (Pflicht)/EN/FR, Titel, Teaser, Markdown-Text, Kategorie, Sichtbarkeit, Status (Entwurf; veröffentlichen
  und planen nur mit `news.publish`, Zeitpunkt in der Zukunft), Anpinnen, Kommentare, Discord-Post, löschen (`news.manage`
  oder eigener Entwurf), Titelbild über öffentlichen Upload (`upload_id`). Hinweise der Website (`meta.warnings`) werden
  angezeigt. Spec-Fehler bei `SaveNews` → Lücke W15, die App sendet selbst. **Annahme:** keine Vorschau im Editor (die
  API hat für News keinen Render-Endpunkt) – Entwurf speichern und auf der Website ansehen.

- **2.4.5 – Matches eintragen:** „Match eintragen“ auf der Matches-Seite und „Bearbeiten“ am Match (Recht
  `matches.manage` irgendwo + Scope `matches:write`; Squads in der Auswahl nur mit dem Recht im Squad). Eigenes Fenster:
  Squad, Art, Status, Gegner/Tag/Website, Zeitpunkt, Maps als Zeilen („supply 2:1“, in Core geparst und getestet),
  Gesamtergebnis optional, Links als Zeilen („vod URL Bezeichnung“, ohne Typ: Twitch/YouTube → `vod`), Bericht in der
  App-Sprache, veröffentlichen, Line-up aus den Squad-Mitgliedern (Ersatz, Position), Anhänge (bis 5 je Anfrage,
  multipart) und löschen. **Annahme:** „Aus einem Termin“ (`POST /events/{id}/match`) und Turniere bearbeiten bleiben
  der Website vorbehalten, bis jemand sie in der App braucht.

- **2.4.6 – Teilnehmer und Aufstellung:** Termin-Detail zeigt Zusagen/Vielleicht/Absagen/ohne Antwort mit Namen
  (Abwesende markiert, Grund nie) und die Aufstellung (Stammspieler, Ersatz, Positionen, Notiz; Entwürfe nur für die
  Leitung). Mit `meta.can_edit` (Recht `squad.lineup`/`events.manage`): „Aufstellung bearbeiten“ – Kandidaten aus
  bisheriger Aufstellung und Antworten, „Vorschlag übernehmen“ (`…/lineup/suggestion`), Ersatz/Position, als Entwurf
  speichern oder veröffentlichen (Squad wird benachrichtigt), zurückziehen.

- **2.4.7 – Galerie, Uploads, Website-Verwaltung:** Reiter „Galerie“ (`gallery.manage` + Scope `gallery:manage`/
  `gallery:write`): offene Einträge aus allen Alben mit Freigabe (Lücke W16: keine Warteschlange in der API), Vorschau,
  freigeben/ablehnen (löscht). Reiter „Uploads“ (`uploads.manage`, Scope `uploads:manage`): offene Meldungen mit Vorschau,
  behalten/löschen, Speicher gesamt und Top 10. Reiter „Website“: alle Verwaltungsbereiche, die die App nicht nativ hat
  (Rollen/Rechte, Spiele, Team, Abzeichen, Bewerbungs-Einstellungen, Terminarten, Turniere, Hall of Fame, CMS, Regeln,
  Recht, Forum-Kategorien, Downloads, Galerie-Alben, Upload-Limits, Wiki-Bereiche, Gameserver einrichten,
  Integrationen/Bot/Webhooks, Social, Mail-Vorlagen, Übersetzungen, Registrierung, Einstellungen, System) als Direktlinks
  auf die Admin-Domain (neu in `environments.json`: `admin`). Gefiltert nach den Rechten, die die Website-Routen
  festlegen; ohne festes Recht prüft die Website selbst.
  **Annahme:** Diese selten genutzten Einrichtungsseiten bleiben vorerst Website-Seiten (Livewire mit 2FA-Step-up);
  nativ nachziehen, sobald jemand sie in der App braucht. **Annahme:** Staging-Admin-Domain `admin.rebelsofgaming.dev`.

- **2.4.8 – Feinschliff:** `scripts/check-a11y.py` prüft jede XAML-Seite auf Namen für Narrator (Symbol-Schaltflächen,
  Eingabefelder ohne Header/Platzhalter, Bilder) und läuft in `check-app-syntax.sh` und in der Linux-CI; Funde behoben
  (Status-Filter, „Nach dem Speichern“, Logo in der Titelleiste als dekorativ). Sprung im Wiki-Inhaltsverzeichnis
  animiert nur, wenn „Animationseffekte“ in Windows an sind. Store-Texte DE/EN/FR mit Funktionen, Suchbegriffen,
  Neuerungen und Screenshot-Plan in `docs/STORE-TEXTE.md`.
  **Offen (braucht Windows):** Kaltstart messen und ggf. ReadyToRun fürs MSIX (heute nur im Publish-Profil, das
  Paket entsteht per `dotnet build`), Screenshots, Windows App Certification Kit.

- **2.4.9 – Korrekturen aus Wahkes Test (17.10.):** Profile ohne verknüpfte Konten scheiterten komplett, weil PHP ein
  leeres Objekt als `[]` liefert (`profile.accounts`) – der Client liest `[]` jetzt für alle offenen Objekte der Spec
  als leeres Objekt (`EmptyArrayAsOpenObjectConverterFactory`); öffentliche Profile, Squads, News, Termine, Server und
  Forum gegen die Live-API geprüft. Terminzeit über `EventTimes.Range` (ein „t“ im eigenen Muster ist das AM/PM-Kürzel →
  „P–21:00“). Verwaltungslisten luden beim ersten Öffnen doppelt (Status-Filter löste ein zweites Laden aus). Live-Konsole
  zeigt den Verbindungsstatus, `daemon error`/`jwt error`, erkennt Demo-Server und verbindet bis zu dreimal neu (wie
  `server-console.js`). Chat-Anhänge: Bilder mit Vorschau über `GET /v1/attachments/{id}` (Umleitung auf kurzlebigen
  Link, max. 8 MB), andere Dateien öffnen im Browser. ET-Farbcodes im Clan-Tag werden wie auf der Website entfernt.

- **2.5.9 – Gameserver-Bans (Website v0.33.0, live 20.10.2026, Guide 43):** `GameBanService` (Core, 30 Tests):
  `GET /v1/game-bans` mit `filter[server]` (alle dort geltenden Bans), `filter[status]`, `filter[search]`, Cursor;
  anlegen (Geltung `server`/`game`/`global`, Kennungstyp leer = Typ des Spiels, IP nur mit `gamebans.manage`),
  `PATCH` (nur Dauer ab jetzt und Grund), aufheben mit optionalem Grund, Einzelabruf mit `deliveries`; Vorfall
  `POST /v1/servers/{server}/game-bans/lift`; Plugin-Schlüssel `GET|POST|PATCH|DELETE …/plugin-key`. Ban-IDs nur als
  ULID im Pfad, Prüfung der Eingaben wie Guide 43 (Kennung, Typ, Grund ≤ 200, IP grob; genau prüft der Server).
  App: Reiter „Bans & Plugin“ im Server-Steuerungsfenster – sichtbar mit Scope `gamebans:manage` plus `servers.bans`
  (Ability des Servers) oder `gamebans.manage`, bzw. mit Scope `servers:manage` für den Schlüssel; den Schlüssel-Bereich
  zeigt die App nur, wenn der Server den Status liefert (403/404 → ausgeblendet). Globale Geltung, IP, erweiterte
  Plugin-Rechte, höchste Geltung über `server` und der Vorfall nur mit `gamebans.manage`. **Schlüssel:** Klartext nur im
  Dialog direkt nach dem Erstellen (Kopieren auf Wunsch), nie gespeichert, geloggt oder gecacht (ohne ETag-Cache).
  **Annahme:** Standard-Geltung im Formular „Spiel“ für Ban-Verwaltende, sonst „nur dieser Server“. **Offen:** Wer
  nur `gamebans.manage` hat, aber keine Steuerungsrechte am Server, erreicht den Reiter nicht (das Fenster öffnet nur mit
  `control`); eine serverübergreifende Ban-Liste gibt es in der App noch nicht (Website-Admin).
- **2.5.10 – Ban-Liste und Einsprüche (Website v0.34.0, live 20.10.2026, Guide 43, Annahme 488):** `PublicBanService`
  (Core, 22 Tests): `GET /v1/bans` (`filter[status]` active/ended, `filter[search]`, Cursor), `GET /v1/bans/{ban}` mit
  `appeal_info` (available/blocked/cooldown_until/proofs), `POST /v1/bans/{ban}/appeals` (Stellungnahme 20–3000 Zeichen,
  Code nur ohne Nachweis, Leerzeichen raus), `GET /v1/game-ban-appeals`, `POST …/{appeal}/decision` (`reappeal_allowed`
  nur beim Ablehnen), `GET|PATCH /v1/game-bans/settings`, `POST …/settings/appeals-board`; `GameBan` mit `is_public`,
  `appeal_code`, `public_url`, `SetPublicAsync` (PATCH nur `is_public`). Links nur https. App: Seite „Ban-Liste“ ohne
  Eintrag in der Seitenleiste (Knopf auf der Serverseite, `AppSection.Bans` für `/bans/…` und `rog://bans/…`),
  Einspruch-Formular nur mit Scope `gamebans:appeal` (sonst Hinweis „ab- und wieder anmelden“), danach öffnet das
  Forum-Thema in der App; Verwaltung → Reiter „Einsprüche“ mit Scope `gamebans:manage` und `gamebans.manage` bzw.
  `servers.bans` irgendwo, Entscheiden nur bei `can_decide`, Forum-Bereich nur mit `gamebans.manage`. Antwort-/
  Bearbeitungssperre in Einspruch-Themen: das Forum der App folgt schon `can_reply`/`can_edit` und zeigt Fehlercodes
  des Servers. **Korrektur:** Forum-Links `/forum/t/{ulid}/{slug}` gaben den Slug statt der ID weiter.
  **Annahme:** Die Ban-Liste bekommt keinen eigenen Eintrag in der Seitenleiste (Liste ist schon lang), sondern einen
  Knopf auf der Serverseite.

- **2.5.11 – Server-Karten nicht mehr abgeschnitten (Owner-Test 2.5.10):** `UniformGridLayout` misst nur die erste
  Kachel und zwingt alle Karten auf deren Höhe – eine Karte mit Spielerzahl und Ports wurde unten abgeschnitten. Neues
  `CardGridLayout` (App, `NonVirtualizingLayout`) mit Spaltenrechnung `CardGrid` in Core (3 Tests): Spalten nach
  Mindestbreite wie bisher, jede Zeile so hoch wie ihre höchste Karte, aufgeklappte Bereiche wachsen mit. Eingesetzt auf
  der Server-Seite und bei den Squad-Servern; andere Raster mit gleich hohen Kacheln bleiben beim `UniformGridLayout`.
  Das Auslastungs-Diagramm (Punkte für 280 × 60) skaliert per `Viewbox` auf die Kartenbreite (max. 560) statt fest 280 –
  in schmalen Karten ragte es über den Rand.
- **2.5.12 – Abfrage-Typen (Website v0.35.0, live 20.10.2026):** Client aus der Live-Spezifikation (nur Doku-Änderungen,
  `query_type` bleibt ein String). `ProvisionForm.QueryTypes` mit allen zwölf Werten in Website-Reihenfolge, auch im
  Admin-Bereich „Server“ (vorher nur drei – ein Server mit `mc_query` hätte im Formular keinen Wert gezeigt); Labels
  `AdminChoice_*` in DE/EN/FR, `source` heißt „Steam / Source (A2S)“. `mc_query`/`mc_bedrock` gelten als Minecraft
  (Adresse kopieren), übrige neue Typen als `Unknown` (Verbinden nur über `connect_url`); 2 Tests. Neue Live-Fehlercodes
  (`error`) zeigt die App nicht an – nichts zu tun. **Annahme:** keine eigenen Verbinden-Links für FiveM/SA:MP o. Ä.,
  solange die Website keinen `connect_url` liefert.
- **2.5.13 – Copyright (Owner-Wunsch 21.10.2026):** wie im Website-Footer – „© 2020–<Jahr> Rebels of Gaming ·
  Copyright by wahke“, „wahke“ klein und als Link auf `https://wahke.lu` (Link-Ziel als Ressource, keine Domain im
  Code), in Einstellungen → Über; Assembly-`Copyright` entsprechend; Store-Copyright in `docs/STORE-TEXTE.md` angepasst
  (im Partner Center von Hand eintragen).
- **2.5.17 – Minecraft-Nachrichten, -Einstellung und Spiel-Meldungen (Website v0.38.0):** `ChatMessage.Source`/`FromMinecraft`
  (Liste und `.message.sent`) → „· aus Minecraft“ hinter der Uhrzeit. `MinecraftService.ShowWebsiteMessagesAsync`/
  `SetShowWebsiteMessagesAsync` (`GET`/`PATCH /v1/me/minecraft/settings`, unbekannte Werte = aus) → Schalter unter Konto →
  Minecraft (nur mit verknüpftem Account; Fehler setzt ihn zurück). Moderation: `ReportsAsync(source)` mit
  `filter[source]`, `ModReport.Source` und `ModGameReport` aus `details` (beide Spieler mit Edition, Server, Netzwerk-Server,
  Zahl der Verlaufsnachrichten); Filter „Herkunft“ und am Fall Spieler/Server plus Hinweis „Verlauf vom Minecraft-Server,
  nicht geprüft“. „Moderieren“ nur mit verknüpftem Konto (`target`). Tests: 6 neue.
- **2.5.18 – Rebel-Coins (Website v0.39.0/v0.40.0, Guide 46):** `MinecraftService.WalletAsync` (`GET
  /v1/me/minecraft/wallet`, Scope `minecraft:read`) → Konto → Minecraft: Kontostand, Tagesbonus-Serie (beste Serie,
  heute abgeholt) und die letzten 20 Buchungen (Typ, Grund, Server, Gegenüber nur als Art – nie andere Spieler). Nur
  Anzeige, keine Buchungen aus der App. `CoinsVisibilityAsync`/`SetCoinsVisibilityAsync` lesen bzw. ändern nur
  `visibility.coins` per Roh-`PATCH /v1/me/profile` (der generierte Typ hätte alle Sichtbarkeiten mitgeschickt);
  Auswahl „Im Profil sichtbar für“ (Standard nur ich). `MemberProfile.Coins` aus `profile.rebel_coins` → Abschnitt im
  Mitgliederprofil (nur mit Opt-in, sonst ausgeblendet). Plugin-Schlüssel `may_economy` („Rebel-Coins buchen“), braucht
  `gamebans.manage`. Tests: 13 neue. **Annahme:** Wallet-Bereich erscheint, sobald die Wallet lesbar ist – auch ohne
  verknüpften Account (die Wallet bleibt beim Konto).
- **2.5.19 – Rebel-Coins-Verwaltung und Mindest-Plugin-Versionen (Website v0.39.0/v0.40.0):** `MinecraftService`
  `EconomyAsync`/`SaveEconomyAsync` (`GET`/`PUT /v1/minecraft/economy`, sendet alle Felder), `WalletsAsync` (Suche,
  `filter[kind]`, Cursor), `WalletTransactionsAsync`, `AdjustAsync` (`credit`/`debit`, 1 … 1 000 000, Grund 3–200 –
  ungültig wird nicht gesendet). Serientage als Text „7: 100, 30: 500“ (`ParseMilestones`, höchstens 20, Tage 1–3650,
  eindeutig). Neuer Admin-Reiter „Rebel-Coins“ nur mit Scope `minecraft:economy` + Recht `minecraft.economy`: Liste
  links, rechts gewählte Wallet mit Buchungen und Gutschrift/Abzug (Rückfrage), darunter „Tagesbonus und Grenzen“.
  Rangschutz, eigene Wallet (nur Owner) und Tageslimit je Admin prüft der Server. `AppealSettings` um
  `min_plugin_version(s)` erweitert, `SetPluginVersionsAsync` ersetzt alle Admin-Einträge; im Reiter Einsprüche eine
  Zeile je Plugin („rog-proxy 0.6.0“). Tests: 28 neue.
- **2.5.20 – Minecraft-Statistiken, Bestenlisten, interne Wiki-Bereiche (Website v0.41.0, Guide 44/26, Annahme 495):**
  `MinecraftService.MemberStatsAsync` (`GET /v1/members/{member}/minecraft-stats`, `403 minecraft_stats_hidden`) und
  `LeaderboardAsync` (`board`/`period`/`sort`/`course` – nur bekannte Werte werden gesendet, Wertungen je Liste aus
  `SortsFor`, Kurse aus `meta.courses`). `MemberProfile.Minecraft` aus `profile.minecraft` → Abschnitt „Minecraft“ im
  Mitgliederprofil (erst Kurzfassung, dann alle Werte). Neuer Bereich `AppSection.Leaderboards` ohne Eintrag in der
  Seitenleiste (Knöpfe auf Server-Seite, im Profil, unter Konto → Minecraft; Links `/minecraft/leaderboards`,
  `rog://leaderboards`); Klick auf einen Platz öffnet das Profil. Profil-Sichtbarkeit verallgemeinert
  (`ProfileVisibilityAsync` liest `coins` und `minecraft_stats` aus einem Abruf, `SetStatsVisibilityAsync` patcht nur
  das eine Feld). Awards kommen als Badges (`mc-…`) über die vorhandene Badge-Anzeige; Benachrichtigungstypen
  `minecraft.award`/`minecraft.pool_alert` liefert die API samt Beschriftung. Wiki: `WikiNamespace.IsInternal`
  (`team`) → „· Intern“ in der Bereichsauswahl, Bilder privat; Admin-Auswahl um „Intern (Team)“ erweitert.
  Tests: 27 neue.
- **2.5.21 – Minecraft-Netzwerk: Betrieb (Website v0.41.0, Guide 47, Annahme 496):** `MinecraftService`
  `NetworkServersAsync`, `SetServerRoleAsync` (Rollen der Website, bei `minigame` Ordner `[a-z0-9][a-z0-9-]{0,47}`,
  `null` entfernt), `PoolAsync`/`SavePoolAsync` (Werte auf 1–20/1–100/1–240 begrenzt, `servers` ersetzt die Zuordnung)/
  `DeletePoolAsync`/`UpdatePoolServerAsync` (`excluded`, `reset_failures`; leer → kein Aufruf), `CheckReleaseAsync`/
  `ReleaseAsync`, `DeploysAsync`/`DeployAsync`/`StartDeployAsync` (immer `confirm: true`, ohne Auswahl keine
  `servers` – dann entscheidet der Server, Proxy nur bei Auswahl). Neuer Admin-Reiter „Minecraft-Netzwerk“: Server
  (Rolle speichern nur mit `minecraft:deploy` + `minecraft.deploy`), Plugins verteilen (Prüfung alle 2 s abgefragt,
  höchstens 2 Min.; Lauf alle 3 s, höchstens 10 Min.), Minigame-Pool (nur mit `minecraft:pool` + `minecraft.pool`).
  „Verwaltung“ erscheint jetzt auch mit `minecraft.economy`/`minecraft.pool`/`minecraft.deploy` allein. Tests: 26 neue.
- **2.5.24 – W23 live (Website v0.44.0):** `JoinForEditAsync` liest nur noch `GET` (kein leerer `PUT` mehr); fehlen
  `console.hints`, sendet das Formular keine Hinweise (leere Felder würden sie sonst löschen). Client aus v0.44.0.
- **2.5.23 – W23:** `JoinForEditAsync` liest zuerst `GET /v1/minecraft/join` – liefert die Website `console.hints`
  (Verwaltende, ab dem Release nach v0.43.0), reicht das; sonst der leere `PUT` als Übergang. Den Rückfall nach dem
  Release entfernen. Tests: 1 neuer.
- **2.5.22 – Minecraft-Beitritt (Website v0.42.0/v0.43.0, Annahme 498):** `MinecraftService.JoinAsync`
  (`GET /v1/minecraft/join`, ohne Token im Gastmodus), `UpdateJoinAsync` (`PUT`, nur gesetzte Felder, Hinweise je Sprache
  auf 500 Zeichen gekürzt), Prüfung von Adresse/Port/Gamertag wie die Website; `MinecraftJoin.JavaText` hängt den Port
  nur an, wenn er nicht 25565 ist; bei `console.enabled = false` kein Gamertag/Hinweis. Server-Seite: Reiter „Minecraft“
  (Index 1) mit Java/Bedrock/Konsole und Kopier-Knöpfen, Formular nur mit `minecraft:manage` + `minecraft.manage`.
  Annahme: Das Formular lädt die Hinweise aller Sprachen über einen leeren `PUT` (die Website speichert nur echte
  Änderungen, also kein Audit) – `GET` liefert nur die App-Sprache (API-Lücke W23). Web-Link `/minecraft` → Server,
  Reiter Minecraft. TTT-Bestenliste und `objective:`-Sortierungen gab es schon (2.5.20); die Awards `mc-ttt-*` kommen
  als Badges. Tests: 22 neue.
- **2.5.16 – Freunde (Website v0.38.0, live 22.10.2026, Guide 45, Annahme 492):** `FriendsService` (Core, 15 Tests):
  `GET /v1/me/friends` (Status → `Presence`, `null` = nicht freigegeben, `meta.total`/`meta.limit`),
  `GET /v1/me/friends/requests` (`filter[direction]`), `POST …/requests {user}` (Gegenanfrage → `status: accepted`),
  `POST …/{id}/accept|decline`, `DELETE …/requests/{id}`, `DELETE /v1/me/friends/{user}`. Eingabe „Name oder Profil-Link“
  wird normalisiert (`@`, `…/u/slug`) und vorab geprüft. Neuer Bereich `AppSection.Friends` (Seitenleiste nur angemeldet,
  Links `…/friends` und `rog://friends`, damit auch die Benachrichtigungen `friends.request`/`friends.accepted`):
  Anfragen an mich (Annehmen/Ablehnen mit Hinweis auf die 7-Tage-Sperre), Freunde (online zuerst, Status-Punkt, „Freunde
  seit“, Nachricht, Entfernen mit Rückfrage), gesendete Anfragen (Zurückziehen), Zähler „x von 100“. Echtzeit
  `.friends.changed` → `RealtimeHub.FriendsChanged` → Seite lädt still neu. Profil eines Mitglieds: „Als Freund
  hinzufügen“ (Scope `friends:write`). Ohne Scopes (alte Geräte-Tokens) Hinweis zur neuen Anmeldung. Eigene Fehlertexte
  für `blocked`, `email_unverified`, `already_friends`, `already_requested`, `request_cooldown`, `user_unavailable`,
  `friend_limit_reached`, `outgoing_limit_reached`, `request_not_found`, `not_friends`.
- **2.5.15 – Minecraft-Gruppen (Website v0.37.0, live 22.10.2026, Guide 44 „Gruppen (LuckPerms)“):** `MinecraftService`
  (partiell, `MinecraftGroups.cs`, 13 Tests): `GET /v1/minecraft/group-mappings` (Mappings höchste Priorität zuerst,
  `meta.managed_groups`, `meta.retired_groups`), `PUT …/{role}` (`group` normalisiert und vorab auf `[a-z0-9_-]{1,36}`
  geprüft, Priorität auf 0–1000 begrenzt), `DELETE …/{role}`, `DELETE /v1/minecraft/retired-groups/{group}`.
  Verwaltung → Minecraft oben „Minecraft-Gruppen“: Rolle (Ränge und Zusatzrollen aus `/v1/roles`, Squad-Rollen
  ausgeblendet), Gruppe, Priorität, Speichern; Liste mit Bearbeiten/Entfernen (Rückfrage); entfernte Gruppen mit
  „entfernt am“/„freigeben ab“ und „Nicht mehr verwalten“ (vor `releasable_at` deaktiviert). Rangschutz, geschützte
  Gruppen und Owner-Pflicht entscheidet nur der Server (`403`, `422 role_not_mappable`, `409 group_release_too_early`
  mit eigenen Texten). Plugin-Schlüssel-Recht `may_link` heißt wie auf der Website „Netzwerk-Proxy (Konten verknüpfen,
  Login, Sitzungen)“ mit neuem Hinweistext.
- **2.5.14 – Minecraft-Kontoverknüpfung (Website v0.36.0, live 21.10.2026, Guide 44, Annahme 490):** `MinecraftService`
  (Core, 28 Tests inkl. Routen, dazu 1 Test für `may_link`): `GET /v1/me/minecraft/accounts`, `POST /v1/me/minecraft/link` zweistufig (ohne
  `confirm` Vorschau, mit `confirm: true` verknüpfen; eigener Idempotency-Key je Schritt), `DELETE …/accounts/{id}`,
  Verwaltung `GET/DELETE /v1/minecraft/accounts` (Suche, Edition, Server, Cursor). Codes wie der Server normalisiert
  (Groß/klein, Leerzeichen, Bindestriche egal) und vorab geprüft (6 Zeichen A–Z/2–9 ohne O/I) – ein Tippfehler zählt so
  nicht als Fehlversuch im Raten-Limit. Konto → „Minecraft“ (Expander): Erklärung, Code-Feld, „Code prüfen“ → Dialog
  „Code für Notch (Java), angefordert vor 2 Min. – bist das du?“ mit „Ja, verknüpfen“/„Nein“ (Nein ist Standard),
  Liste mit Edition/Plattform, verknüpft am, zuletzt gesehen, UUID, „Trennen“ mit Rückfrage. Ohne die neuen Scopes
  (alte Geräte-Tokens) Hinweis „einmal ab- und wieder anmelden“. Deep-Link `…/account/minecraft?code=…` (auch mit
  Sprachpräfix und `rog://account/minecraft?code=…`) → Konto, Bereich aufgeklappt, Code **nur vorbefüllt** (ungültige
  Codes werden verworfen), nie automatisch eingelöst. Verwaltung → „Minecraft“ (Scope `minecraft:manage` + Recht
  `minecraft.manage`): Liste mit Konto, Server der Verknüpfung, Suche Name/UUID, Edition, trennen, Profil öffnen.
  Plugin-Schlüssel: Recht `may_link` („Minecraft-Konten verknüpfen“, nur mit `gamebans.manage`, Hinweis wie Website).
  Nebenbei: Menüpunkt „Verwaltung“ auch mit `servers.bans` (Einsprüche; vorher unsichtbar). **Offen:** die Liste „über
  diesen Server verknüpfte Accounts“ im Reiter „Bans & Plugin“ (Website-Admin) – die Verwaltungsliste kann bereits nach
  Server filtern (`filter[server]`), die Oberfläche dafür fehlt noch.
- **2.5.8 – Website v0.32.2 (live 20.10.2026):** Client aus der Live-Spezifikation (Tool-Endpunkte jetzt vollständig).
  Server-Karte: aufklappbare „Ports (n)“ aus `provisioning.allocations` (Hauptport zuerst mit „Hauptport“, Alias statt
  IP, wenn gesetzt; `null` = Panel nicht erreichbar → nichts anzeigen), `ServerAllocation` in Core mit Tests. News-Liste:
  Social-Symbole über `GET /v1/social/sources/news?ids[]=…` (W22, je Seite der Liste, Blöcke zu 100, `none` = kein
  Symbol); die eigene Ableitung aus `/social/posts` (`StateOf`, nur die letzten 100 Posts) ist entfernt. Website-Links
  (Profil `/u/{slug}`, `/settings/account`) über `RoGEnvironment.WebPage` mit `/{de|en|fr}/` – ohne Präfix 404.

- **2.5.7 – Tool-Baukasten und zusätzliche Ports (Website v0.31.0/v0.32.0, Auftrag Wahke 19.10. über die
  Website-Sitzung):** `ToolService` (Liste mit `filter[game]`/`filter[squad]`/`filter[status]=all` für Verwaltende,
  Dokument, Daten, Assets – direkt über `/v1/`, weil Tool-Werte beliebiges JSON sind, ohne ETag-Cache) und
  `ToolBridge` (Core, 27 Tests): Protokoll aus Guide 42 und `tool-frame.js` – nur `{rog:"bridge",v:1}`, Methodenliste,
  128 KB, Textlängen, 8 gleichzeitig, Dateien 4 gleichzeitig/250 in der Warteschlange/3 MB, nur Namen aus der
  Liste, Antwort `{name,mime,size,base64}`; `copy` 1×/s; `openLink` nur http(s) ohne Zugangsdaten, 1/s, 10/min,
  nach 5 Fehlversuchen 60 s gesperrt. **Annahme:** Statt der Nutzer-Aktivierung (gibt es in WebView2 nicht) fragt
  die App vor jedem Link mit Ziel-Host nach; abgelehnt → `error.code = cancelled`. Pfadteile aus nur Punkten lehnt
  Core ab (Uri würde `..` auflösen). App: Navigationspunkt „Tools“ (nach Spiel gruppiert, Entwürfe für Verwaltende),
  „Tools“ im Squad (aufklappbar), Links `/tools/{slug}` und `rog://tools/{slug}`; jedes Tool im eigenen Fenster
  (`ToolWindow`): `NavigateToString` (`about:blank`), jede weitere Navigation/Frame-Navigation abgebrochen, keine
  neuen Fenster, Downloads, Berechtigungen, Skript-Dialoge, DevTools, Kontextmenüs, Browser-Tastenkürzel;
  `resize` wird nicht gebraucht (das Fenster scrollt). Dokumente über 1,9 Mio. Zeichen (Grenze von
  `NavigateToString`) → Hinweis „im Browser öffnen“. **Nicht gebaut:** Tool-Verwaltung (anlegen, Code, Versionen,
  Assets) bleibt auf der Website. Gameserver anlegen: „Zusätzliche Ports“ (Mehrfachauswahl, `additional_allocations`
  als Zahlen) und „Weitere automatisch“ (`additional_auto`); zusammen höchstens 10, Hauptport nicht doppelt; ohne
  Zusatzports gehen die Felder nicht raus. `tools/AppCompileCheck` referenziert die WebView2-Projektion selbst.
  Melden (v0.30.4) gegengeprüft: 429 `too_many_reports` mit Wartezeit, Wiedereröffnung (200) als Erfolg.

- **2.5.6 – Kinderschutz (Website v0.30.3, Auftrag Wahke über die Website-Sitzung 18.10.):** `child_safety` als erster
  Grund in `ModerationService.ReportReasons` (alle Melden-Dialoge); Moderation: `ModReport.Priority`/`IsChildSafety`,
  Filter `filter[reason]` (Auswahl „Alle Gründe“/„Kinderschutz“), rotes Kennzeichen (`RoGAlertBadgeStyle`), offene
  Kinderschutz-Fälle stabil nach oben (falls der Server nicht sortiert). Upload-Meldungen der Verwaltung lesen
  `category` und zeigen dasselbe Kennzeichen. Link `/{locale}/child-safety` in den Einstellungen. Vorbereitet für
  v0.30.4: 429 `too_many_attempts` zeigt die Wartezeit aus `Retry-After`, eine wieder geöffnete Meldung (200 statt 201)
  gilt als Erfolg. **Achtung beim Neu-Generieren:** `V1_moderation_reports_indexAsync` bekam `filterreason` vor
  `cursor` – positionelle Argumente verschoben sich unbemerkt; Aufruf jetzt mit benannten Argumenten.
  **Nicht umgesetzt:** Uploads melden (`POST /v1/uploads/{id}/report`, Checkbox Kinderschutz) – die App hat keinen
  Melde-Dialog für einzelne Uploads; Galerie-Einträge laufen über `/v1/reports` (`gallery_item`) und haben den Grund.

- **2.5.5 – Website v0.30.1:** `AccountDeletionFlow` (Core, Tests) mit den Zuständen Bestätigen → Austritt nötig
  (`leave_clan_first`) → ausgetreten/`not_a_member` → Löschen; `owner_cannot_leave`/`owner_cannot_delete` sperren,
  404/405 (ältere Website) → Hinweis „an die Clanleitung wenden“, 422 `confirmation` und 429 mit Wartezeit im Dialog.
  `AccountDataService.LeaveClanAsync` (`POST /v1/me/leave-clan`). Im Dialog „Clan verlassen“ mit zweitem Klick als
  Rückfrage (kein verschachtelter Dialog möglich). Wiki: `WikiArticle.LastEditorId` aus `last_editor`, „Melden“ nur bei
  fremder Fassung; Versionen über `author.id`. **Annahme:** Ein fehlendes `last_editor` (ältere Website) ist im
  generierten Client nicht von `null` zu unterscheiden → „Melden“ bleibt sichtbar; der Rückfall über die Revisionsliste
  entfällt, weil v0.30.1 live ist und die Versionsliste eigene Versionen ohnehin ausnimmt.

- **2.5.4 – Gameserver anlegen und löschen:** `ServerProvisioningService` + `ProvisionForm` + `EggRules` (Core, Tests):
  Katalog, Formular je Node/Egg aus dem allgemeinen Editor (`AdminFieldsView`, Felder mit festen Texten für
  Panel-Werte), Egg-Regeln wie Laravel (Whitelist der Website), Body mit `node_id`/`nest_id`/`egg_id`, Ablauf,
  Status prüfen, Löschen nur bei `deletable` und exakt eingetipptem Namen. Recht `servers.create` + Scope
  `servers:manage` (Website vergibt beides nur mit 2FA). **Annahme:** Vorgaben 4 GB RAM, 20 GB Speicher, 200 % CPU,
  2 Backups, 1 weiterer Port, Sichtbarkeit „versteckt“ – alles im Formular änderbar.

- **2.5.3 – Social-Media-Status an der Quelle:** `SocialSourceService` (Core, Tests): Status, Kanal erneut senden,
  alle fehlgeschlagenen erneut senden (meldet nicht nutzbare Kanäle), neu vorbereiten (`repost` nur nach Rückfrage, wenn
  schon überall gepostet). `SocialStatusCard` in News-Editor, Clanwar-Editor, Termin-Detail, Galerie-Album; News-Liste
  mit Symbol aus einer Abfrage der Posts (W22, ab 2.5.8 Sammelabfrage der Website). Sichtbar nur mit `social.publish` + `social:read`, Aktionen mit
  `social:write`. Freigabe der Entwürfe bleibt in der Social-Verwaltung der Website (Link in der Karte).

- **2.5.2 – Website v0.30.0 (Melden, Blockieren, Rechtstexte):** Client aus `https://api.rebelsofgaming.com/openapi.json`
  (Typ-Umbenennungen per Abgleich von Methoden und Eigenschaften). Meldetypen `wiki_page` (+ `locale`), `wiki_revision`,
  Melden-Knopf in Galerie-Großansicht und Wiki; News-Kommentare mit `author_blocked` → Platzhalter + `show_blocked`;
  Moderations-Meldungen mit `url`/`locale` → „Gemeldeten Inhalt öffnen“ (App-Route, sonst Browser); Einstellungen mit
  Nutzungsbedingungen und Impressum. Eigene Wiki-Inhalte vom Melden ausnehmen folgt mit v0.30.1 (`last_editor_is_me`).

- **2.5.1 – Konsole, Gerätefamilie:** Konsolen-Token: `level` ist im generierten Client ein Enum mit String-Konverter nur an der
  Eigenschaft – `SerializeToElement(enum).GetString()` warf, die Konsole verband nie. Jetzt direkte Abbildung, Test
  `ServerControlServiceTests`. Manifest nur `Windows.Desktop` (Hinweis Wahke zur Store-Einreichung).

- **2.5.0 – Website-Verwaltung in der App (Store-Kandidat):** allgemeiner Editor (`AdminArea`/`AdminForm`/
  `AdminResourceService` in Core, `AdminEditorWindow` in der App): Liste mit Suche, Formular aus der Felddefinition,
  Anlegen/Speichern/Löschen, Einstellungen ohne Liste, Verweise als Auswahl, „Auf der Website öffnen“ für den Rest.
  26 Bereiche (`AdminAreas`): Spiele, Terminarten, Abzeichen, Team-Seite, Turniere, Hall of Fame (Trophäen, Chronik),
  Partner, Regelwerke, Inaktivität (Regeln, Ausnahmen), Bewerbungen (Fragen, Probeaufgaben), Registrierung (Einladungen,
  Einstellungen), Clan-Einstellungen, Seiten, Downloads, Download-Kategorien, Forum-Bereiche, Galerie-Alben,
  Wiki-Bereiche/-Vorlagen, Mail-Vorlagen, Upload-Limits, Rollen, Gameserver einrichten. Verträge aus den API-Controllern
  der Website gelesen (Stand 17.10.). Übersetzungen: Die Website führt Sprachen zusammen (`App\Support\Translations`),
  der Editor sendet nur geänderte Sprachen; liefert die API nur die Anfrage-Sprache (W17), zeigt er nur diese.
  Gesendet wird nur Geändertes (PUT-Bereiche wie Upload-Limits und Mail-Vorlagen: alles).
  **Weiter im Browser:** Rechte-Matrix, Rechte prüfen, Startseite, Menüs, Integrationen, Discord-Bot, Social Media,
  System, Bewerbungs-Richtlinien; dazu Webhooks, Übersetzungen, Rechtstexte – deren Scopes fragt die App nach dem
  Sicherheitsentscheid vom 15.10. bewusst nicht an. **Annahme:** Ohne Gegenfrage bleibt das so.
  **Annahme:** Verwaltungs-Scopes gibt die Website nur mit Recht **und** aktiver TOTP-2FA; wer sie neu braucht, meldet
  sich einmal ab und an.

### Geplant – wartet auf Website v0.30.0 (angekündigt 18.10.2026)

Von der Website-Sitzung angekündigt, Owner möchte beides in der App. **Erst bauen, wenn die Website meldet, dass die
Version live ist** – dann `scripts/generate-api.sh --from-website` gegen die Live-Spezifikation; die Endpunkte unten sind
der geplante Stand und können sich noch ändern. Neue Scopes (`social:read`/`social:write`, Server anlegen) kommen ohne
App-Änderung, weil der Login `meta.app_scopes` von `/v1/auth/scopes` übernimmt (`AppScopes`); wer sie braucht, meldet
sich einmal neu an (2FA).

1. **Social-Media-Status an der Quelle** (News, Termine, Clanwars, Galerie-Einträge) – **erledigt in 2.5.3**
   - Core: `SocialSourceService` – `GET /v1/social/sources/{source}/{id}` (Gesamtstatus none · draft · scheduled ·
     publishing · published · partial · failed · cancelled; je Kanal Status, Zeit, Link, Fehlergrund),
     `POST …/targets/{target}/retry` (failed/skipped/cancelled), `POST /v1/social/posts/{post}/retry-failed`,
     `POST /v1/social/sources/{source}/{id}/redraft`. Logik „welche Aktion ist erlaubt“ in Core mit Tests;
     bereits gepostete Kanäle nie erneut senden, nur über „neu vorbereiten“ (neuer Entwurf, Freigabe wie bisher).
   - App: Karte „Social Media“ in `NewsEditorWindow`, Termin-Detail, `MatchEditorWindow` und Galerie (Media-Seite);
     Status-Symbol in der News-Liste. Sichtbar nur mit Scope `social:read`, Aktionen nur mit `social:write`.
2. **Gameserver anlegen und löschen** (Pterodactyl Application API über die Website) – **erledigt in 2.5.4**
   - Core: `ServerProvisioningService` – `GET /v1/servers/provisioning/catalog` (Locations/Nodes, freie Allocations,
     Nests/Eggs mit Docker-Images, Startbefehl, Variablen), `POST /v1/servers/provision` (Idempotency-Key über
     `RoGHttpHandler`), `PATCH /v1/servers/{server}/expiry`, `DELETE /v1/servers/{server}/provision`;
     `provisioning` {status installing · ready · failed, provisioned_at, expires_at, deletable} an der Server-Ressource.
     Formular-Logik (Egg-Variablen mit Regeln, Limits, Website-Felder Spiel/Squad/Sichtbarkeit/Query/Passwort) in Core
     mit Tests.
   - App: „Neuer Gameserver“ in der Serververwaltung (eigenes Fenster, Formular dynamisch nach Egg), Status-Badge,
     Ablauf mit Warnung und „Verlängern“, Löschen in einer Gefahrenzone mit Eingabe des Servernamens, nur wenn
     `deletable`. Recht `servers.create` aus `/v1/me` (nur mit 2FA), nie über Rollennamen.
3. **Mit v0.30.0, kleinere Punkte** (Ankündigung 18.10.) – **erledigt in 2.5.2**
   - Melden: neue Typen `wiki_page` (Seiten-ULID, optional `locale` der angezeigten Fassung) und `wiki_revision`
     (Versions-ULID) in `ModerationService.ReportAsync`; Melden-Knopf für Galerie-Einträge (`gallery_item`, in Core
     schon da, in der Media-Seite fehlt er), Wiki-Seite und Wiki-Version. Eigene Wiki-Inhalte (`last_editor_is_me`,
     erst ab v0.30.1) nicht melden lassen.
   - Blockierte Personen: News-Kommentare mit `author_blocked: true` / `body: null` als „Kommentar einer blockierten
     Person“ mit „trotzdem anzeigen“ (`?show_blocked=1`); fehlende Galerie-Einträge brauchen nichts.
   - Moderation: `url` der Meldung als „Inhalt öffnen“ (App-Route, sonst Browser).
   - Nutzungsbedingungen `/{locale}/terms` neben Datenschutz/Impressum in Einstellungen und Konto verlinken.

### Website v0.30.1 (live 18.10.2026) – erledigt in 2.5.5

- **Clan verlassen vor der Kontolöschung** (Pflicht für die Store-Regeln „Konto in der App löschen“): `DELETE /v1/me`
  liefert bei aktiven Mitgliedern 409 `leave_clan_first` (neutrale Meldung der API). Dann im selben Dialog „Clan
  verlassen“ anbieten: `POST /v1/me/leave-clan`, Body `{confirmation}` (dasselbe Passwort-/Namensfeld), Scope
  `profile:write`. 200 `{data: {is_member, status, rank}}` → Löschen freigeben; 409 `owner_cannot_leave` → Hinweis;
  409 `not_a_member` → wie „schon ausgetreten“, Löschen erlauben; 422 Feld `confirmation`; 429 `too_many_attempts`
  mit `Retry-After`; 403 Re-Auth wie beim Löschen; 404/405 (ältere Website) → bisheriger Hinweis „an die Clanleitung
  wenden“. Ablauf wie in der Mobile-App, Dialog-Zustände in Core mit Tests.
- **Eigene Wiki-Inhalte nicht melden:** Wiki-Seiten haben `last_editor: {id, name} | null` (kein `last_editor_is_me`).
  `last_editor.id` = eigene User-ID → kein „Melden“; `null` oder jemand anderes → „Melden“; Feld fehlt (ältere
  Website) → aus der Revisionsliste ableiten (neueste freigegebene Version von mir → kein „Melden“).

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
- Ab 2.5.9: Gameserver-Bans (Kennungen, Namen, Gründe, IP-Netze nur für Ban-Verwaltende) zeigt die App nur an,
  speichert sie nicht (kein Cache); der Plugin-Schlüssel erscheint einmalig im Dialog und landet nur auf Wunsch in der
  Zwischenablage.
- Ab 2.5.10: Die öffentliche Ban-Liste und Einsprüche zeigt die App nur an (kein Cache); eine Stellungnahme geht nur an
  die Plattform und wird dort ein öffentlich lesbares Forum-Thema – darauf weist das Formular hin.
- Ab 2.5.18: Rebel-Coins (Kontostand, Serie, Buchungen) zeigt die App nur an (kein Cache); die Sichtbarkeit im Profil liegt nur auf dem Server.
- Ab 2.5.22: Die Minecraft-Beitrittsdaten zeigt die App nur an (kein Cache); geändert werden sie nur auf dem Server.
- Ab 2.5.21: Netzwerk-Verwaltung (Heartbeat, Pool, Deploy) nur Anzeige und Aufrufe, nichts lokal gespeichert; die auslösende Person eines Deploys speichert die Website.
- Ab 2.5.20: Minecraft-Statistiken und Bestenlisten zeigt die App nur an (kein Cache, Abruf ohne ETag); die Freigabe `minecraft_stats` liegt nur auf dem Server.
- Ab 2.5.17: Die Einstellung „Website-Nachrichten im Spiel anzeigen“ liegt nur auf dem Server (keine lokale Kopie); Spieler-Angaben aus Spiel-Meldungen zeigt die App nur an.
- Ab 2.5.16: Freundesliste und Anfragen zeigt die App nur an (kein Cache, keine lokale Speicherung).
- Ab 2.5.14: Minecraft-Accounts (Name, UUID, Edition, Plattform) zeigt die App nur an (kein Cache); der Link-Code geht
  nur an die Plattform und wird nach dem Verknüpfen aus dem Feld gelöscht.
- Ab 2.3.0: gelesene Wiki-Seiten (gerendertes HTML, Titel, Bereich) unter `wiki/` im App-Datenordner, je Sprache,
  **mit DPAPI verschlüsselt** (ab 2.3.3); Abmelden löscht den Ordner, Deinstallation ebenso.
- Ab 2.3.3: Offline-Warteschlange `outbox.dat` (Forenantworten, Zusagen; DPAPI-verschlüsselt) – **Direktnachrichten
  nie auf der Platte**, nur im Speicher bis zum Senden. Abmelden leert die Warteschlange.
- Ab 2.5.7: Tools (Tool-Baukasten) laufen in einer WebView2 im **InPrivate-Modus** (keine Cookies, kein Speicher
  über das Fenster hinaus) ohne Netzwerk (CSP der Website plus 403 für jede http(s)-Anfrage in der App). Was ein Tool
  speichert, liegt im Konto auf der Website (`/v1/tools/{slug}/data/*`, im Datenexport enthalten), nicht lokal.
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
