# Versionierung und Pakete

## Eine Nummer, zwei Stellen

| | Wo | Format | Beispiel |
|---|---|---|---|
| **App-Version** (SemVer) | `Directory.Build.props` → `RoGVersion` (einzige Quelle für Assembly-/Dateiversion, User-Agent, Anzeige) | `X.Y.Z` | `0.1.0` |
| **MSIX-Version** | `src/RoG.App/Package.appxmanifest` → `Identity Version` | `X.Y.Z.0` | `0.1.0.0` |

Die vierte Stelle der MSIX-Version muss für den Microsoft Store **0** bleiben (sie ist für den Store reserviert). Anders
als bei der Mobile-App gibt es deshalb **keine getrennte Build-Nummer**: Jede Einreichung braucht eine höhere
`X.Y.Z` als die zuletzt hochgeladene – eine Korrektur während der Prüfung wird also zu einer PATCH-Version.
Ein Test (`AppProjectConsistencyTests`) prüft, dass beide Stellen übereinstimmen.

Die App zeigt „Rebels of Gaming für Windows vX.Y.Z · Plattform vA.B.C“ (Plattform aus `GET /v1` → `data.app.version`)
und meldet `X.Y.Z` als `app_version` an `/v1/devices`.

## Regeln (SemVer)

- **0.x** bis zur ersten öffentlichen Store-Veröffentlichung, **1.0.0** = erste öffentliche Version im Store.
- **MINOR** (`0.1.0 → 0.2.0`): neue Bereiche/Funktionen, in der Regel eine abgeschlossene Bau-Stufe.
- **PATCH** (`0.2.0 → 0.2.1`): nur Fehlerbehebungen – und jede erneute Einreichung derselben Funktionen.
- Ab 1.0: MAJOR nur bei Brüchen, die ein Update erzwingen (z. B. neue Mindest-API der Website, neue Mindest-Windows-Version).
- Testpakete (Sideload von der VM) dürfen dieselbe Version wie der Entwicklungsstand haben; sie gehen nie in den Store.

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

- `0.1.0` (in Arbeit): Stufe 0 – Fundament, noch kein Paket gebaut.
