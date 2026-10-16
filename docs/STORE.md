# Microsoft Store (Partner Center, MSIX)

Die Windows-App wird ausschließlich über den **Microsoft Store** verteilt (Owner-Entscheidung 15.10.2026). Updates
kommen nur über den Store, es gibt keinen eigenen Updater. Versionen: `docs/VERSIONIERUNG.md`.

## 1. Konto im Partner Center (einmalig, Owner)

1. <https://partner.microsoft.com/dashboard> → Anmelden mit einem Microsoft-Konto (am besten ein eigenes für RoG,
   mit 2FA) → Registrierung als **Einzelperson** (Privatperson in Luxemburg; laut Microsoft seit 2025 ohne
   Registrierungsgebühr für Einzelentwickler – beim Anmelden prüfen) oder als Unternehmen (braucht Firmennachweis).
2. **Anzeigename des Herausgebers** wählen (erscheint im Store als „Herausgeber“), z. B. „Rebels of Gaming“ – Achtung:
   bei Einzelpersonen wird der Konto-Inhaber verifiziert, der Anzeigename darf aber der Clan-Name sein.
3. Kontaktdaten aktuell halten (Microsoft schreibt bei Zertifizierungsproblemen dorthin).

## 2. App-Namen reservieren und Identität übernehmen

Partner Center → Apps und Spiele → **Neues Produkt → MSIX- oder PWA-App** → Namen reservieren
(Vorschlag: **„Rebels of Gaming“**, DE/EN/FR derselbe Name; reserviert bleibt er 3 Monate ohne Einreichung).

**Erledigt (16.10.2026):** Es gibt schon die App „Rebels of Gaming“ (Store-ID **9PDWKHL6T188**,
<https://apps.microsoft.com/detail/9PDWKHL6T188>, alte UWP-Version 1.0.4.0 für x86/x64). Die neue WinUI-App wird als
**Update dieser App** eingereicht (gleiche Identität, Version ab 2.0.0) statt einer neuen App – Name und Nutzer bleiben.
Werte im Manifest:

| Feld | Wert |
|---|---|
| `Identity Name` | `wahke.RebelsofGaming` |
| `Identity Publisher` | `CN=BAB3F2CF-D90F-4F06-A6BF-E1AE3CC52136` |
| `PublisherDisplayName` | `wahke` (Anzeigename des Kontos; ändert Wahke ihn im Partner Center, hier nachziehen) |
| Paketfamilienname (PFN) | `wahke.RebelsofGaming_dnk64872v0wr6` (für Lücke W2) |

Folgen: Die neue App läuft nur noch auf Windows-Desktop (x64/ARM64, ab Windows 10 1809); x86-Geräte und ältere
Windows-Versionen behalten die alte 1.0.4.0. Die Paket-SID (WNS) steht im Partner Center unter Produktidentität bzw.
WNS/MPNS und wird nur bei Lücke W1 an die Website gegeben.

Ursprüngliche Anleitung – unter **Produktverwaltung → Produktidentität** ablesen:

| Feld im Partner Center | Ziel im Repo |
|---|---|
| `Package/Identity/Name` (z. B. `12345RebelsofGaming.RebelsofGaming`) | `src/RoG.App/Package.appxmanifest` → `Identity Name` (jetzt Platzhalter `RebelsofGaming.App`) |
| `Package/Identity/Publisher` (z. B. `CN=1A2B3C4D-…`) | `Identity Publisher` (jetzt `CN=PLATZHALTER`) und Testzertifikat (`docs/BUILD-VM.md` Abschnitt 6) |
| `Package/Properties/PublisherDisplayName` | `Properties/PublisherDisplayName` |
| Paketfamilienname (PFN) | Website `.env` für App-URI-Handler (Lücke W2) |
| Paket-SID (`ms-app://S-1-15-2-…`) + Anwendungsgeheimnis (WNS) | **nur** in die `.env` der Website, falls WNS-Push kommt (Lücke W1) – das Geheimnis nie ins App-Repo |

Ändert sich die Identität nach der ersten Veröffentlichung, ist es eine neue App – deshalb erst nach der Reservierung
echte Pakete hochladen.

## 3. Paket bauen

Auf der Build-VM (von dev-linux aus):

```bash
scripts/remote-build.sh --mode store --committed
# → artifacts/remote/<lauf>/artifacts/msix/RoG.App_X.Y.Z.0_x64_arm64_bundle.msixupload
```

- `.msixupload` enthält das Bundle für x64 + ARM64 und die Symbole; der Store signiert es.
- **Windows App SDK**: Das Paket hängt vom Framework-Paket `Microsoft.WindowsAppRuntime` ab; der Store installiert es
  automatisch mit. .NET ist im Paket enthalten (self-contained).
- Vor dem Hochladen auf der VM das **Windows App Certification Kit** laufen lassen (ab Stufe 10 Teil von
  `build-windows.ps1`): `appcert.exe test -appxpackagepath <paket> -reportoutputpath wack.xml`.

## 4. Einreichung (je Version)

Partner Center → App → **Einreichung starten**:

| Abschnitt | Inhalt |
|---|---|
| Preise und Verfügbarkeit | kostenlos; Märkte: alle (oder EU), Sichtbarkeit zuerst **privat/Paket-Flight** für Tester, später öffentlich |
| Eigenschaften | Kategorie **Soziale Netzwerke** (Alternative: Unterhaltung); Datenschutz-URL `https://rebelsofgaming.com/de/privacy` (EN/FR je Sprache); Website `https://rebelsofgaming.com`; Support-Kontakt `https://rebelsofgaming.com/de/contact`; Systemanforderungen: Internet |
| Altersfreigaben | IARC-Fragebogen: Nutzerinteraktion (Chat, Forum, DMs), Nutzer teilen Inhalte, keine Käufe, keine Standortfreigabe (Mitgliederkarte nur Stadt, Opt-in) → voraussichtlich USK 12 / PEGI 12 wegen ungefilterter Kommunikation |
| Pakete | `.msixupload` hochladen; Gerätefamilie Desktop |
| Store-Einträge (DE, EN, FR) | Beschreibung, Neuerungen, Funktionen, Screenshots (mind. 1, empfohlen 4–6, 1920 × 1080 oder 1366 × 768, dunkles Design), Logos (aus dem Paket), Suchbegriffe; Texte an den Store-Texten der Mobile-App ausrichten (`/home/wahke/projects/rog-app/store.config.json`), Du-Form |
| Hinweise für die Zertifizierung | Anmeldung über „Im Browser anmelden“ (Website: Discord **oder E-Mail und Passwort**, 2FA); **Demo-Konto mit E-Mail/Passwort** ohne Discord (wie Mobile-App, Zugangsdaten nur im Partner Center, nie im Repo); Gastmodus „Ohne Anmeldung umsehen“; Konto löschen unter Konto; `runFullTrust` = WinUI-3-Desktop-App; Autostart nur nach Einschalten in den Einstellungen |

**Datenschutz-Angaben:** Die App verarbeitet Konto- und Community-Daten nur über die eigene Plattform (keine Drittanbieter,
keine Werbung, kein Tracking). Die Datenschutzerklärung der Website braucht einen Abschnitt zur Windows-App (analog
Abschnitt 11 „App für iOS und Android“: Gerätename, Token in der Windows-Anmeldeinformationsverwaltung, ggf. WNS) – als
Aufgabe an die Website-Sitzung, sobald die App in die Einreichung geht.

**Kontolöschung:** in der App (Konto → Konto löschen, `DELETE /v1/me`) und öffentliche Anleitung
`https://rebelsofgaming.com/{de|en|fr}/account-deletion`.

## 5. Nach der Freigabe

- Zertifizierung dauert typischerweise einige Stunden bis 3 Werktage; Status im Partner Center.
- Nach Freigabe: Git-Tag `vX.Y.Z` auf den eingereichten Commit, `main` per Fast-Forward (nur nach Owner-Freigabe).
- **Paket-Flights** für Tester: Partner Center → Paket-Flights → Gruppe mit Microsoft-Konten der Tester; Flight-Pakete
  brauchen eine höhere Version als die öffentliche.
- Store-Link der App in die Website eintragen (Admin → Einstellungen, wie `app_store_url`/`play_store_url` – ein Feld für
  Windows fehlt noch; bei Bedarf als Lücke melden).
