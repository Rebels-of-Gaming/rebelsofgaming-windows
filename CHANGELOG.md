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
- 2.5.10: Website v0.34.0 – öffentliche Ban-Liste (Knopf „Ban-Liste“ auf der Serverseite, Links `/bans/…`): aktive und kürzlich beendete Bans mit gekürzter Kennung, Suche, Einspruchs-Status; Einspruch einlegen mit Stellungnahme und – ohne verknüpftes Konto – dem Code aus der Kick-Nachricht, daraus wird ein Forum-Thema; Verwaltung → „Einsprüche“ (annehmen hebt den Ban auf, ablehnen optional mit sofortigem neuen Einspruch, Forum-Bereich wählen oder anlegen); im Reiter „Bans & Plugin“ Bans aus der öffentlichen Liste aus-/einblenden, Einspruchs-Code und öffentliche Seite; Forum-Links `/forum/t/{id}/{slug}` öffnen jetzt das richtige Thema in der App
- 2.5.9: Website v0.33.0 – zentrale Gameserver-Bans: neuer Reiter „Bans & Plugin“ in der Server-Steuerung mit den Bans, die auf dem Server gelten (Filter nach Status, Suche, anlegen mit Geltung Server/Spiel/alle Server, Dauer ändern, aufheben, RCON-Zustellungen), dazu der Plugin-Schlüssel des Servers (Status und Heartbeat, Rechte, erstellen/rotieren mit einmaliger Anzeige, widerrufen) und für Ban-Verwaltende das Aufheben aller Plugin-Bans bei einem Vorfall
- 2.5.8: Website v0.32.2 – Server-Karte zeigt alle zugewiesenen Ports (Hauptport gekennzeichnet) bei über die Website angelegten Servern; Social-Symbole in der News-Liste aus der neuen Sammelabfrage (auch für ältere News); Profil- und Konto-Links auf der Website mit Sprachkürzel (vorher Fehler 404)
- 2.5.7: Tools – neuer Bereich „Tools“ (nach Spiel gruppiert) und „Tools“ im Squad; jedes Tool läuft abgeschottet im eigenen Fenster (ohne Netzwerk), speichert über dein Konto, kopiert in die Zwischenablage und öffnet Links erst nach Rückfrage; Gameserver anlegen mit zusätzlichen Ports (Auswahl oder die nächsten freien)
- 2.5.6: Website v0.30.3 – Meldegrund „Kinderschutz / Gefährdung Minderjähriger“ ganz oben in jedem Melden-Dialog; Moderation mit rotem Kennzeichen „Kinderschutz“, Filter nach Grund und Kinderschutz-Fällen zuerst (auch bei Upload-Meldungen); Link „Kinderschutz“ bei den Rechtstexten in den Einstellungen; Wartezeit bei zu vielen Meldungen; API-Client aus der Live-Spezifikation
- 2.5.5: Website v0.30.1 – „Konto löschen“ bietet aktiven Mitgliedern „Clan verlassen“ im selben Dialog an (gleiche Bestätigung, danach Löschen frei; Owner bekommt einen Hinweis); eigene Wiki-Seiten und -Versionen lassen sich nicht mehr melden; API-Client aus der Live-Spezifikation
- 2.5.4: Gameserver über die Website anlegen – „Neuer Gameserver“ auf der Serverseite (Node, freier Port, Spiel-Vorlage mit Variablen und Regeln aus dem Panel, Limits, Website-Felder, automatisches Löschen nach 1–30 Tagen); Server-Karte zeigt Installation und Ablauf, „Verwalten“ mit Status prüfen, Ablauf setzen/verlängern/entfernen und Löschen in der Gefahrenzone (Bestätigung mit dem Servernamen)
- 2.5.3: Social-Media-Status an der Quelle – Karte „Social Media“ in News-Editor, Clanwar-Editor, Termin-Detail und Galerie-Album (Zustand, Kanäle mit Link und Fehler, einzeln oder alle fehlgeschlagenen erneut senden, neu vorbereiten, bewusst erneut posten nur nach Rückfrage); Status-Symbol in der News-Liste
- 2.5.2: Website v0.30.0 – Melden von Wiki-Seiten (mit angezeigter Sprache), Wiki-Versionen und Galerie-Einträgen; Kommentare blockierter Personen ausgeblendet mit „Trotzdem anzeigen“; Moderation mit „Gemeldeten Inhalt öffnen“; Nutzungsbedingungen und Impressum in den Einstellungen; API-Client aus der Live-Spezifikation
- 2.5.1: Live-Konsole brach mit „requires an element of type 'String'“ ab (Berechtigungsstufe des Konsolen-Tokens falsch gelesen); Manifest nur noch für `Windows.Desktop`
- 2.5.0: Website-Verwaltung in der App – 26 Bereiche (u. a. Spiele, Terminarten, Abzeichen, Turniere, Hall of Fame, Partner, Regelwerke, Inaktivität, Registrierung, Clan-Einstellungen, Seiten, Downloads, Forum-Bereiche, Galerie-Alben, Wiki, Mail-Vorlagen, Upload-Limits, Rollen, Gameserver) im eigenen Editor-Fenster; API-Client mit Social Media, Riot und Steam-Bibliothek
- 2.4.9: Korrekturen aus Wahkes Test – Profile ohne verknüpfte Konten ließen sich nicht öffnen (PHP `[]` statt `{}`), Terminzeit „P–21:00“, doppelte Listen in der Verwaltung, Beschriftung der Übersichtskacheln, Termin-Detail mit Auswahlhinweis, ET-Farbcodes im Clan-Tag; Live-Konsole mit Verbindungsstatus, Wings-Fehlern, Demo-Erkennung und automatischem Neuverbinden; Bildvorschau für Chat-Anhänge, „Bewerben“ nicht mehr im eigenen Squad, Startseite ohne veralteten Hinweis, TeamSpeak-Leerzustand, einheitliche Knöpfe und Überschriften
- 2.4.8: Barrierefreiheit (Prüfung der Screenreader-Namen, Animationen nach Windows-Einstellung), Store-Texte
- 2.4.7: Verwaltung – Galerie-Freigaben, Upload-Meldungen und Speicher, Direktlinks zur Website-Verwaltung
- 2.4.6: Termine – Teilnehmerliste und Aufstellungen bauen/veröffentlichen
- 2.4.5: Matches eintragen und bearbeiten – Maps, Line-up, Links, Bericht, Screenshots
- 2.4.4: News-Redaktion – Entwürfe, Übersetzungen, planen und veröffentlichen, Titelbild
- 2.4.3: Squads verwalten – Profil, Logo und Header, Line-up mit Rollen und Ersatz, archivieren
- 2.4.2: Wiki – neue Seiten anlegen, Bilder einfügen, Kategorien, Inhaltsverzeichnis mit Sprung
- 2.4.1: Server-Steuerung mit Dateibrowser (Sicherungen kopieren/umbenennen/löschen), Whitelist und Formular-Vorlagen (Servername, Slots, Passwort, Map-Rotation)
- 2.3.0: Wiki mit Bereichen, Suche, Bearbeiten (Vorschau, Konflikterkennung), Versionen und Offline-Kopien; Statistiken, Onboarding-Kachel, Mitgliederkarte; Links in Inhalten öffnen Ziele der Plattform direkt in der App
- Konto-Seite mit Abmelden, Einstellungen (Sprache, Versionen, Lizenzen), Startseite mit Live-Ticker, nur eine App-Instanz, Fensterposition wird gemerkt
- Korrigiert: API-Client sendet Auswahlwerte jetzt wie erwartet (`windows`), wertet `201 Created` als Erfolg und verträgt `[]` statt `{}`
- Testpakete (Sideload) signiert der Windows-Runner mit einem selbst angelegten Testzertifikat (`RoG-Test.cer` im Artefakt)
