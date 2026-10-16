# Changelog – Rebels of Gaming für Windows

Versionen nach [SemVer](https://semver.org/lang/de/), Regeln in `docs/VERSIONIERUNG.md`. Jedes an den Microsoft Store
übermittelte Paket bekommt einen Git-Tag `vX.Y.Z`. Änderungen bis zur nächsten Version sammelt `## [Unveröffentlicht]`.

## [Unveröffentlicht]

Stufe 0 – Fundament (Version 0.1.0, noch nicht im Store).

- Lösung `RoG.sln`: plattformunabhängiger Kern `RoG.Core` (baut und testet unter Linux), Tests `RoG.Core.Tests`, WinUI-3-App `RoG.App` (MSIX, baut auf der Windows-VM)
- API-Client aus der OpenAPI-Spec der Website (NSwag, `scripts/generate-api.sh`), HTTP-Pipeline mit Token, Sprache, RFC-9457-Fehlern, Idempotency-Key, ETag, 401/403/429
- App-Login im Browser (Discord oder E-Mail) mit PKCE und Rücksprung an `http://127.0.0.1:<Port>/callback`, Gerätename „Windows-App – PC von …“
- Echtzeit-Client für Reverb (Pusher-Protokoll 7) mit privaten und Presence-Kanälen, Ping/Pong und Neuverbindung
- Einstellungen, Sprachen DE/EN/FR, RoG-Design als ResourceDictionary, Login-Seite, Startseite (Platzhalter), Seitenleiste
- Build-Skripte für die Windows-VM (`scripts/build-windows.ps1`, `scripts/remote-build.sh`) und Anleitungen (`docs/BUILD-VM.md`, `docs/STORE.md`)
- CI auf GitHub Actions: Core + Tests unter Linux, WinUI-App (x64 + ARM64) auf dem Windows-Runner; erster grüner App-Build
- MSIX-Pakete (Store-Upload `.msixupload`, Sideload) werden mit `dotnet build` erzeugt – Visual Studio wird dafür nicht gebraucht
- Korrigiert: Reihenfolge der Theme-Ressourcen (XAML WMC0035), Freigabe von Abbruch-Token und Tresor-Sperre (CA1001); Warnungen gelten jetzt auch in der App als Fehler
- Server-Seite: Gameserver mit Live-Stand, „Verbinden“ über Steam (`steam://connect`) und Enemy Territory (`et://`), sonst Adresse kopieren (Minecraft) mit Hinweis im Spiel
- Store-Identität der bestehenden App „Rebels of Gaming“ (`wahke.RebelsofGaming`); Version 2.0.0, weil im Store schon 1.0.4.0 liegt
- 2.1.0: Benachrichtigungen mit Windows-Toasts, Tray-Symbol, Sprungliste, Autostart, Direktnachrichten mit eigenem Chat-Fenster, Forum, Uploads mit Drag & Drop und Strg+V, Galerie, Downloads, Kurzlinks, Profil bearbeiten, News, Squads, Mitglieder, Termine mit Zusage, TeamSpeak/Discord, Matches, Info, Suche
- 2.2.0: Bewerben bei Squads mit „Meine Bewerbungen“, Abstimmungen über Bewerbungen, Abwesenheiten, Geräte & Datenexport, Konto löschen, Hinweis auf neue Regeln; Server-Steuerung im eigenen Fenster (Start/Stopp/Neustart/Kill, Ressourcen, Live-Konsole mit Verlauf, Configs mit Prüfung und Versionen, Backups, RCON, Sperren, Vier-Augen-Freigaben); App-Sperre mit Windows Hello
- 2.4.0: Offline-Betrieb (verschlüsselter Cache mit letztem Stand, Warteschlange für Nachrichten/Beiträge/Zusagen, Offline-Hinweis), Inhalte melden, Verwaltung (Übersicht, Mitglieder und Rollen, Bewerbungen, Moderation, Termine anlegen/absagen), Wiki-Freigaben, Galerie einreichen, Teilen-Ziel „An RoG hochladen“, Spiele-Liste, Server-Auslastung (24 h), Monatsansicht im Kalender
- 2.3.0: Wiki mit Bereichen, Suche, Bearbeiten (Vorschau, Konflikterkennung), Versionen und Offline-Kopien; Statistiken, Onboarding-Kachel, Mitgliederkarte; Links in Inhalten öffnen Ziele der Plattform direkt in der App
- Konto-Seite mit Abmelden, Einstellungen (Sprache, Versionen, Lizenzen), Startseite mit Live-Ticker, nur eine App-Instanz, Fensterposition wird gemerkt
- Korrigiert: API-Client sendet Auswahlwerte jetzt wie erwartet (`windows`), wertet `201 Created` als Erfolg und verträgt `[]` statt `{}`
- Testpakete (Sideload) signiert der Windows-Runner mit einem selbst angelegten Testzertifikat (`RoG-Test.cer` im Artefakt)
