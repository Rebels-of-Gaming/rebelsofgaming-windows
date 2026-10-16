# Windows-Pflichtenheft – Rebels of Gaming für Windows

Stand 15.10.2026 · Grundlage: Website-Pflichtenheft + Ergänzung API/Wiki/Domains im Website-Repo
(`/home/wahke/projects/rebelsofgaming/docs/`), Mobile-App-Pflichtenheft (`/home/wahke/projects/rog-app/docs/APP-PFLICHTENHEFT.md`)
und die Funktions-Inventur „Website vs. App“ vom 15.10.2026 (`/home/wahke/projects/rog-parity-2026-10-15.md`).
Die Website-API ist die einzige Datenquelle.

## 1. Ziel

Eine native Windows-App, die **den kompletten Funktionsumfang der Website** abbildet – öffentlich, Memberbereich und
Verwaltung (nach Rechten) – mindestens auf dem Stand der Mobile-App, und dabei Dinge besser kann als der Browser:
Infobereich-Symbol (Tray) mit Zähler, Windows-Benachrichtigungen, Autostart, Sprunglisten, mehrere Fenster (Chat und
Server-Konsole herauslösen), Tastenkürzel, Drag & Drop, Screenshots direkt aus der Zwischenablage hochladen,
Ein-Klick-„Verbinden“ zu Gameservern (Steam) und TeamSpeak.

**Bewusst nicht in der App** (wie Mobile-App/Inventur Abschnitt 5): Impersonation, persönliche API-Tokens
(`tokens:manage`), Webhooks, Übersetzungen, Rechtstexte verwalten, große Editoren (News/Seiten/Regeln/Squads/Spiele,
Rechte-Vorlagen, Bewerbungsfragen, Startseiten-Texte), Forum-Bereiche/Reaktionen/Einstellungen,
Integrations-Zugangsdaten, Discord-Serverstruktur, Menüs, Registrierung neuer Konten, Discord/Steam verknüpfen,
Audit-/Log-Download, Metrik-Export → jeweils „Auf der Website öffnen“. Auf dem Desktop sind große Editoren später
denkbar (eigene Stufe nach 1.0, Owner-Entscheidung).

## 2. Plattform und Fenster

- Windows 11 und Windows 10 ab 1809 (Build 17763), **x64 und ARM64**, verteilt als **MSIX über den Microsoft Store**
- WinUI 3 (Windows App SDK), .NET 10, nur Dunkel-Design im RoG-Stil, Mica/Acryl nur dezent (Titelleiste/Seitenleiste)
- **Fenster-Layout** (effektive Pixel): < 600 → Menü-Knopf (LeftMinimal) und einspaltig, 600–839 → Symbolleiste
  (LeftCompact), ≥ 840 → aufgeklappte Seitenleiste mit allen Bereichen + **Liste-Detail nebeneinander** (Forum, DMs,
  Mitglieder, News, Wiki, Server, Verwaltungs-Tabellen). Lesebreite für Text max. 720 epx.
- Fenstergröße/-position und zuletzt offener Bereich werden gemerkt; Mindestgröße 360 × 500 epx ohne horizontales Scrollen
- **Eine Instanz:** zweiter Start holt das vorhandene Fenster nach vorn (Aktivierung wird weitergereicht, z. B. `rog:`-Link)
- Sprache DE/EN/FR (Standard: Windows-Anzeigesprache, sonst DE), Wechsel in den Einstellungen

## 3. Konto, Login, Geräte

- **Gastmodus:** öffentliche Inhalte (News, Squads, Team, Server, TeamSpeak-Viewer, Matches, Kalender öffentlich, Galerie,
  Wiki öffentlich, Regeln, Seiten) ohne Login – „Ohne Anmeldung umsehen“
- **Login im System-Browser** (Discord oder E-Mail + Passwort auf der Website, inkl. 2FA) mit OAuth 2.0 + PKCE (S256)
  und **Loopback-Rücksprung** `http://127.0.0.1:<zufälliger Port>/callback` (RFC 8252 7.3, Guide 35). `state` wird immer
  geprüft, der `code_verifier` verlässt das Gerät nie, Code-Tausch `POST /v1/auth/app/token`. Nie eine WebView.
- Gerätename „Windows-App – PC von <Windows-Benutzer>“ (max. 80 Zeichen), Token in der **Windows-Anmeldeinformationsverwaltung**
- Angefragte Scopes aus `GET /v1/auth/scopes` → `meta.app_scopes` ohne `tokens:manage`, `webhooks:*`,
  `translations:manage`, `legal:manage`; tatsächliche Scopes aus der Token-Antwort blenden Bereiche ein
- Abmelden (`POST /v1/auth/logout`) widerruft das Token serverseitig und löscht Token + Konto-Daten lokal
- Konto → Apps & Geräte (`/v1/devices`), Sitzungen, Passkeys ansehen/entfernen, E-Mail/Passwort ändern, Datenexport,
  Konto löschen (`DELETE /v1/me`), TeamSpeak-Identität verknüpfen, Rechtstexte bestätigen, Blockliste – wie Paket A der Inventur
- **App-Sperre mit Windows Hello** (optional; Pflicht-Option für die Verwaltung)
- Erststart: Onboarding (Sprache, Benachrichtigungen, Autostart, Online-Status-Opt-in, offene Regeln bestätigen)

## 4. Funktionsumfang (alle Module, Parität mit Website und Mobile-App)

| Bereich | Umfang in der Windows-App |
|---|---|
| Start / Dashboard | Live-Ticker (online, Voice, auf Servern, Server-Belegung, nächstes Event) aus `/v1/live` + Reverb, Twitch-live-Band, Stat-Kacheln, Onboarding-Fortschritt, Events mit Zusage, offene Votes, neue Forenbeiträge, Geburtstage, Abwesenheit, Spielzeit, Discord-Voice-Kachel |
| News | Liste, Detail, Kommentare, Teilen (Windows-Teilen-Dialog), Link kopieren |
| Squads | Übersicht, Squad-Seite mit Reitern (Vorstellung, Line-up, Galerie, Server, Matches, Regeln, Intern, Stats), Mobile-Squads mit Ingame-Clan, Ref-/Einladungslinks, Kriege |
| Spiele | Liste mit Store-/Ref-Links |
| Mitglieder / Team | Suche, Filter (Online, Server, Voice, Squad, Trial), Profil mit Banner, Rang, Status, Reitern (Übersicht/Squads/Stats/Badges/Accounts/Historie/Rang-Historie), Member-Card teilen, melden/blockieren |
| Eigenes Profil | Bearbeiten (Avatar/Banner per Drag & Drop, Motto, Ort für Karte, verknüpfte Accounts ansehen, Opt-ins) |
| Online-Status | Status-Punkte überall, Presence in Echtzeit (`presence-squad.{id}`) |
| Bewerbung | Schrittleiste, Spielauswahl, Fragenkatalog, Regel-Checkboxen, Status mit Timeline, Votes, Trial-Aufgaben, Nachrichten |
| Forum | Kategorien, Themen, Thread mit BBCode-Darstellung (Zitat, Code, Spoiler, Umfrage, Bilder), Antworten mit BBCode-Leiste, Vorschau, Bilder per Drag & Drop/Zwischenablage, Reaktionen, Beobachten, Suche, Ungelesen |
| Nachrichten (DMs) | Unterhaltungen, Gruppen, Chat in Echtzeit, **Chat als eigenes Fenster** (Pop-out, immer im Vordergrund optional), Enter sendet / Umschalt+Enter neue Zeile, Bilder per Drag & Drop/Strg+V, Suche |
| Benachrichtigungen | Glocke mit Zähler, Liste, gelesen markieren, Einstellungen je Kanal; **Windows-Toasts** solange die App (auch im Tray) läuft; Push nach Lücke WNS |
| Kalender / Events | Monat/Woche/Liste, Farben nach Typ, Detail, Zusage/Absage/Vielleicht, Line-up, iCal-Abo-Link, „In Kalender übernehmen“ (`.ics` öffnen) |
| Matches / Turniere / Hall of Fame | Liste, Detail, Ergebnisse, Line-ups, Historie, Trophäen |
| Server | Karten mit Status, Map, Spieler/Slots, Spielerliste mit Member-Markierung, Auslastungsgraph, **„Verbinden“ über `steam://connect/<host>:<port>`** (bzw. spielspezifische Links), Adresse kopieren |
| TeamSpeak / Discord | TeamSpeak-Viewer, **„Verbinden“ über `ts3server://<host>?port=<port>&nickname=<Name>`**, Discord-Voice-Übersicht, Discord-Einladung öffnen |
| Pterodactyl (mit Recht) | Serverliste mit Live-Ressourcen, Start/Stop/Restart/Kill (mit Bestätigung), **Live-Konsole als eigenes Fenster** (WebSocket-Token aus der API, Verlauf, Befehle mit ↑/↓), Config-Editor (Whitelist, Diff, Versionen, Rollback), Dateien (lesen, kopieren, umbenennen, löschen), Backups, Map-Rotation, RCON/Bans/Whitelist, Vier-Augen-Freigaben |
| Stats | Spieler- und Serverstatistiken, Ranglisten, Awards |
| Galerie | Alben, Vollbild mit Zoom/Pfeiltasten, Upload (Datei-Dialog, **Drag & Drop mehrerer Bilder**) |
| Downloads | Liste, Download mit Fortschritt in den Downloads-Ordner, „Im Ordner anzeigen“ |
| Seiten / CMS / Recht | Über uns, Partner, Kontakt (mit ALTCHA), Impressum, Datenschutz – nativ gerendert |
| Regeln | Clan/Spiel/Squad, Bestätigungspflicht (blockierender Dialog) |
| Uploads | Pre-signed Upload, eigene Uploads/Alben, Direktlink `i.rogclan.de` kopieren; **Strg+V im Uploader lädt Screenshots aus der Zwischenablage**, Drag & Drop aus dem Explorer, **Teilen-Ziel „An RoG hochladen“** |
| Kurzlinks | Anlegen, Liste, Klickzahlen, QR-Code, kopieren |
| Mitgliederkarte | Karte mit Stadt-Genauigkeit über `/map/tiles` (eigene Kacheln), nur Opt-in, Cluster |
| Wiki | Namensräume, Seiten (HTML der API nativ gerendert), Inhaltsverzeichnis, Versionen/Diff, Beobachten, Bearbeiten mit Vorschau (mit Recht), Kategorien, Dateien, **offline verfügbar** |
| Suche | Globale Suche (Strg+K / Strg+F), Treffer nach Bereich |
| Moderation (mit Recht) | Meldungen, Aktionen, Verwarnungen, Bans, Wortfilter, Forum-Moderation |
| Verwaltung (mit Recht) | Alle Bereiche, die die API mit App-Scopes erlaubt (Inventur Abschnitt 4): Mitglieder, Rollen & Rechte-Matrix, Rechte-Check, Squads, Spiele, Bewerbungen, Termine/Line-ups, Matches/Turniere, Hall of Fame, Team-Seite, Inhalte, Galerie-/Upload-/Download-Verwaltung, Wiki-Verwaltung, Gameserver, Integrationen, Bot-Features, Mail-Vorlagen, Audit-Log, Systemstatus, Einstellungen – als Tabellen mit Liste-Detail |
| System | „Rebels of Gaming für Windows vX.Y.Z · Plattform vA.B.C“, API-Erreichbarkeit, Link Statusseite, Lizenzen (Barlow OFL) |

Funktionen, die die API noch nicht hergibt → `docs/API-LUECKEN.md`, Seite mit Leerzustand.

## 5. Windows-spezifisch

| Funktion | Umsetzung |
|---|---|
| **Infobereich-Symbol (Tray)** | Symbol mit Zähler-Überlagerung (ungelesen), Kontextmenü: Öffnen, Nachrichten, Benachrichtigungen, Status, Beenden. Schließen des Fensters minimiert in den Tray (abschaltbar). Umsetzung über `Shell_NotifyIcon` (CsWin32) oder eine gepflegte Bibliothek – Wahl in Stufe 3 (Annahme). |
| **Autostart** | MSIX-`StartupTask` (`RoGStartup`, standardmäßig aus); Einstellung „Mit Windows starten“ ruft `RequestEnableAsync`; startet minimiert im Tray |
| **Windows-Benachrichtigungen** | `AppNotificationManager` (Windows App SDK): Titel/Text aus dem Postfach-Eintrag der API (lokal erzeugt, Inhalt verlässt den Server nicht Richtung Microsoft), Klick öffnet den Bereich, Schnellantwort im Toast für DMs, Gruppierung nach Ereignis; respektiert „Nicht stören“ und die Benachrichtigungs-Einstellungen der API |
| **Push (App geschlossen)** | WNS über `PushNotificationManager` – erst nach Website-Erweiterung (`push_provider: wns`, Lücke W1); inhaltsfrei wie FCM/APNs, nur mit Einwilligung (`push_consent`) |
| **Taskleiste** | Badge mit Zahl ungelesener Einträge (`BadgeUpdateManager`), Fortschritt bei Up-/Downloads |
| **Sprungliste** | Rechtsklick auf das Taskleisten-Symbol: Neue Nachricht, Nachrichten, Kalender, Server, Uploader (Strg+V-Screenshot hochladen), zuletzt geöffnete Unterhaltungen |
| **Mehrere Fenster** | Chat und Server-Konsole (später auch Wiki-Seite, Forum-Thema) per „In neuem Fenster öffnen“ herauslösen; eigene Fenstergröße gemerkt; optional „immer im Vordergrund“ |
| **Tastenkürzel** | Strg+K Suche, Strg+N neue Nachricht, Strg+1…9 Bereiche, Strg+R/F5 neu laden, Strg+Enter senden (Forum), Enter senden (Chat), Alt+←/→ zurück/vor, Strg+W Fenster schließen, Strg+, Einstellungen, F1 Hilfe; Liste in Einstellungen → Tastenkürzel |
| **Drag & Drop** | Bilder/Dateien aus dem Explorer in Uploader, Chat, Forum-Antwort, Galerie, Avatar/Banner, Wiki-Dateien; Download-Dateien aus der App in den Explorer ziehen |
| **Zwischenablage** | Strg+V fügt Screenshots (Bitmap) als Upload ein – im Uploader, Chat und Forum; Direktlink nach Upload automatisch kopieren (Einstellung) |
| **Screenshot-Kürzel (optional)** | globales Tastenkürzel „Bildschirmausschnitt hochladen“ (startet das Windows-Ausschneidetool, lädt das Ergebnis aus der Zwischenablage) – ShareX-Ersatz für Mitglieder; Entscheidung in Stufe 5 |
| **Links zu Spielen** | `steam://connect/<host>:<port>` (Source/Steam-Spiele), `ts3server://<host>?port=<port>&nickname=<Name>`; vorher prüfen, ob ein Programm das Schema behandelt (`Launcher.QueryUriSupportAsync`), sonst Adresse kopieren + Hinweis |
| **Deep Links** | eigenes Schema `rog:` (Protokoll-Aktivierung) für Toasts, Sprungliste und Links; Web-Links auf `rebelsofgaming.com`/`wiki.` öffnen die App über **App-URI-Handler** (braucht `/.well-known/windows-app-web-link` auf der Website → Lücke W2) |
| **Teilen** | Windows-Teilen-Dialog für News, Profile, Kurzlinks; Teilen-Ziel „An RoG hochladen“ (Bilder aus anderen Apps) |
| **Windows Hello** | App-Sperre beim Start/nach Leerlauf (`UserConsentVerifier`), Pflicht-Option für Verwaltung |
| **Energie/Netz** | Reverb nur bei aktiver Sitzung, im Tray mit reduzierter Aktivität; Netzwerkwechsel erkennen (`NetworkInformation`), Offline-Banner |

## 6. Echtzeit

- Reverb (Pusher-Protokoll 7) mit Verbindungsdaten aus `GET /v1` → `data.realtime` (kein fest eingebauter Key)
- Kanäle laut Guide 08/35: `private-App.Models.User.{id}` (`.notification`, `.inbox.changed`, `.messages.unread`),
  `private-conversations.{id}`, `private-forum.threads.{id}`, `presence-squad.{id}`, öffentlich `servers`, `live`
- Autorisierung `POST /v1/broadcasting/auth` mit Geräte-Token; Neuverbindung mit Backoff, danach per API abgleichen
- Läuft auch im Tray (dafür gibt es Toasts statt Push, solange die App läuft); eingehende Ereignisse aktualisieren den Cache direkt

## 7. Offline und Netzwerk

- Persistenter Cache (zuletzt gesehene Listen/Profile/News sofort sichtbar), ETag-Abgleich; Wiki-Namensräume offline
- Schreibende Aktionen offline in Warteschlange mit Idempotency-Key (DMs, Forenbeiträge, Zusagen), sichtbarer Status
- Cache verschlüsselt mit DPAPI (an den Windows-Benutzer gebunden), DMs nie im persistenten Cache (wie Mobile-App)

## 8. Pterodactyl-Konsole

Wie Mobile-App: Verbindung zum Wings-WebSocket mit kurzlebigem Token aus `POST /v1/servers/{s}/console-token` und dem
`origin` aus der Antwort (`ClientWebSocket` setzt den `Origin`-Header), Token-Erneuerung (`token expiring`/`token expired`).
Konsole als eigenes Fenster mit Verlauf, Befehls-Historie, Suche, ANSI-Farben.

## 9. Store und Auslieferung

- MSIX-Single-Project, Store-Upload `.msixupload` (x64 + ARM64) aus der Build-VM (`scripts/build-windows.ps1 -Mode Store`)
- Identität aus dem Partner Center: Update der bestehenden Store-App „Rebels of Gaming“ (`wahke.RebelsofGaming`, Store-ID 9PDWKHL6T188), Version ab 2.0.0
- Store-Texte DE/EN/FR, Screenshots (1366 × 768 und größer), Datenschutz-URL `https://rebelsofgaming.com/{de|en|fr}/privacy`,
  Altersfreigabe (IARC), Kontolöschung in der App
- Updates nur über den Store (kein eigener Updater), Versionen nach `docs/VERSIONIERUNG.md`
- Crash-Reporting ohne personenbezogene Daten – Wahl offen (wie Mobile-App)

## 10. Bau-Reihenfolge

Jede Stufe auf der Build-VM gebaut und getestet (sobald sie existiert), committet; weiter erst nach Wahkes „weiter“.

0. **Fundament** – .NET-SDK auf dev-linux, Lösung (Core/Tests/App), API-Client aus der Spec, Pipeline (Bearer,
   Accept-Language, RFC 9457, Idempotency, ETag, 401/403/429), Login PKCE + Loopback, Token-Speicher-Abstraktion,
   Reverb-Client, Einstellungen, i18n-Schlüssel, RoG-Theme, Login-/Startseite, Build-Skripte, Doku *(dieser Stand)*
1. **Konto + Start** – erster VM-Build, `/v1/me` + Rechte/Scopes, Geräte (`platform: windows`), Abmelden, Gastmodus,
   Versionsanzeige, Einstellungen-Seite, UI-Bausteine (Karte, Avatar + Status, Listenzeile, Lade-/Leer-/Fehlerzustand,
   InfoBar/Toast), Liste-Detail-Gerüst, Startseite mit echten Daten (Live-Ticker, Squads, News), Single-Instance, Fensterzustand
2. **Community lesen** – News, Squads, Spiele, Mitglieder, Team, Profile, Server mit „Verbinden“ (Steam), TeamSpeak-Viewer
   mit `ts3server://`, Discord-Voice, Matches/Turniere/Hall of Fame, Kalender (lesen), Regeln, Seiten/Recht, Suche, Gastmodus komplett
3. **Echtzeit + Desktop-Integration** – Reverb im Betrieb, Presence, Benachrichtigungen + Glocke, Windows-Toasts,
   Tray-Symbol mit Zähler, Autostart, Taskleisten-Badge, Sprungliste, Deep Links `rog:`, (WNS-Push nach Lücke W1)
4. **Kommunikation** – DMs mit Pop-out-Fenster, Forum lesen + schreiben (BBCode), Event-Zusagen, Offline-Warteschlange, Tastenkürzel
5. **Medien** – Uploads (Drag & Drop, Strg+V-Screenshots, Teilen-Ziel), Galerie, Downloads, Kurzlinks, Profil bearbeiten
6. **Clan-Prozesse** – Bewerbung + Status, Votes, Trial, Onboarding, Regel-/Rechtstext-Bestätigung, Mitgliederkarte, Stats, Abwesenheit, Konto/Sicherheit (Paket A)
7. **Server-Steuerung** – Pterodactyl: Power, Ressourcen, Konsole als eigenes Fenster, Config-Editor, Dateien, Backups, Map-Rotation, RCON; Windows Hello
8. **Wiki** – lesen, offline, bearbeiten, Versionen, Kategorien, Dateien
9. **Verwaltung + Moderation** – alle Bereiche aus der API (Inventur Abschnitt 4), Tabellen mit Liste-Detail
10. **Feinschliff + Store** – Barrierefreiheit (Narrator, Tastatur, Kontrast), Performance (Startzeit, ReadyToRun/Trimming),
    Animationen, Store-Texte/Screenshots, Zertifizierung (Windows App Certification Kit), Einreichung, Version 1.0.0

## 11. Qualität

- Logik nur in `RoG.Core`, dort Unit-Tests (xUnit, unter Linux) – Pipeline, Login, Echtzeit, Rechte, Parser (BBCode/HTML), Formulare
- Übersetzungen: Tests prüfen DE/EN/FR-Vollständigkeit und jede `x:Uid`
- UI-Tests auf der VM (WinAppDriver/Appium oder FlaUI) für Login, News lesen, DM senden, Event zusagen – ab Stufe 4
- Kaltstart < 2 s auf Mittelklasse-PC, Listen flüssig (virtualisiert), Speicher im Tray < 150 MB
- CI später: Linux-Job (build/test, Spec-Generierung ohne Diff) + VM-Job (App, MSIX, WACK)
