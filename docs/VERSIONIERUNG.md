# Versionierung und Pakete

## Eine Nummer, zwei Stellen

| | Wo | Format | Beispiel |
|---|---|---|---|
| **App-Version** (SemVer) | `Directory.Build.props` → `RoGVersion` (einzige Quelle für Assembly-/Dateiversion, User-Agent, Anzeige) | `X.Y.Z` | `2.0.0` |
| **MSIX-Version** | `src/RoG.App/Package.appxmanifest` → `Identity Version` | `X.Y.Z.0` | `2.0.0.0` |

Die vierte Stelle der MSIX-Version muss für den Microsoft Store **0** bleiben (sie ist für den Store reserviert). Anders
als bei der Mobile-App gibt es deshalb **keine getrennte Build-Nummer**: Jede Einreichung braucht eine höhere
`X.Y.Z` als die zuletzt hochgeladene – eine Korrektur während der Prüfung wird also zu einer PATCH-Version.
Ein Test (`AppProjectConsistencyTests`) prüft, dass beide Stellen übereinstimmen.

Die App zeigt „Rebels of Gaming für Windows vX.Y.Z · Plattform vA.B.C“ (Plattform aus `GET /v1` → `data.app.version`)
und meldet `X.Y.Z` als `app_version` an `/v1/devices`.

## Regeln (SemVer)

- **Start bei 2.0.0.** Unter derselben Store-Identität (`wahke.RebelsofGaming`, Store-ID 9PDWKHL6T188) liegt schon die
  alte App mit **1.0.4.0** (UWP, x86/x64). Der Store nimmt nur höhere Versionen an; 2.x = die neue native WinUI-App.
- **Jedes Update, das Wahke bekommt** (Testpaket per Sideload oder Store-Einreichung), hat eine **höhere Version** als
  das vorige – sonst lässt Windows das Paket nicht über die installierte Version installieren. Claude erhöht die Version
  im selben Commit wie die Änderung bzw. direkt vor dem Paket-Lauf.
- **MINOR** (`2.0.x → 2.1.0`): eine Bau-Stufe abgeschlossen bzw. größerer neuer Bereich.
- **PATCH** (`2.0.0 → 2.0.1`): jedes weitere Update dazwischen (neue Teilfunktion, Fehlerbehebung, erneute Einreichung).
- MAJOR nur bei Brüchen, die ein Update erzwingen (neue Mindest-API der Website, neue Mindest-Windows-Version).

## Ablauf je Store-Einreichung

1. `CHANGELOG.md`: Abschnitt `[Unveröffentlicht]` in `## [X.Y.Z] – JJJJ-MM-TT` umbenennen, neuen leeren
   `## [Unveröffentlicht]` darüber anlegen (ein Test verlangt ihn).
2. Version setzen: `RoGVersion` in `Directory.Build.props` **und** `Identity Version="X.Y.Z.0"` im Manifest.
3. `dotnet build RoG.sln && dotnet test RoG.sln`, dann committen: `chore(release): X.Y.Z`.
4. Paket nur aus sauberem Stand bauen: `scripts/remote-build.sh --mode store --committed`.
5. `.msixupload` im Partner Center einreichen (`docs/STORE.md`).
6. Tag auf den eingereichten Commit: `git tag -a vX.Y.Z -m "Store X.Y.Z"`.
7. `main` = Store-Releases: nach Freigabe im Store und Freigabe des Owners `main` per Fast-Forward auf den Release-Commit.

## Historie

- `0.1.0`: Stufe 0 – Fundament, Testpakete mit Platzhalter-Identität (nie im Store).
- `2.0.0` (16.10.2026): echte Store-Identität, Server-Seite mit „Verbinden“ – Testpaket per Sideload.
