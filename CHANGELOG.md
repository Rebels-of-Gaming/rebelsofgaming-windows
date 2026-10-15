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
