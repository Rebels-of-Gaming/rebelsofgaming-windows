# Build-VM einrichten (Anleitung für den Owner)

Die WinUI-App (`src/RoG.App`) und das MSIX-Paket lassen sich nur unter Windows bauen. Claude entwickelt auf dev-linux
und schickt den Code per SSH an eine **Windows-VM**, baut und testet dort (`scripts/remote-build.sh`) und holt Logs,
Testergebnisse und Pakete zurück. Diese Anleitung beschreibt, was du einmalig einrichten musst.

**Kurzfassung – das brauche ich am Ende von dir:**

| Angabe | Beispiel |
|---|---|
| Host (Name oder IP der VM, von dev-linux erreichbar) | `10.0.10.50` oder `winbuild.example.lu` |
| SSH-Port | `22` (oder deine Weiterleitung, z. B. `2222`) |
| Benutzer | `builder` |
| Bestätigung, dass der **öffentliche** Schlüssel von dev-linux auf der VM hinterlegt ist | Abschnitt 5 |

Nie Passwörter, nie private Schlüssel schicken. Die Werte kommen in `~/.config/rog-windows/build-vm.env` auf dev-linux
(nicht ins Repo, Vorlage `scripts/build-vm.env.example`).

> **Stand 15.10.2026:** Der Owner hat statt SSH einen **GitHub-Actions-Runner auf Windows** eingerichtet (Organisation
> Rebels-of-Gaming). Gebaut wird damit über `.github/workflows/ci.yml` (Job „WinUI-App“, `runs-on: [self-hosted, Windows]`),
> die Ergebnisse (Logs, Testberichte, MSIX) liegen als Artefakt `windows-artifacts` am Lauf. Die Werkzeuge aus Abschnitt 3
> müssen auf dem Runner installiert sein; SSH (Abschnitte 4–5, `scripts/remote-build.sh`) ist damit optional.

## 1. Wo läuft die VM?

**Hetzner fällt aus:** Hetzner Cloud bietet keine Windows-Abbilder an, und eine selbst installierte Windows-Lizenz auf
Cloud-Servern ist weder offiziell unterstützt noch günstig. Alternativen:

| Variante | Kosten (grob, Stand prüfen) | Bewertung |
|---|---|---|
| **Eigener Rechner/Server mit Hyper-V oder Proxmox** (auf dem Proxmox-Host der CI-Runner?) | 0 € bei vorhandener Windows-Lizenz; sonst Windows 11 Pro ca. 150–260 € einmalig. Zum Ausprobieren: **Windows Server 2025 Evaluation** (180 Tage kostenlos) oder **Windows 11 Enterprise Evaluation** (90 Tage) | **Empfehlung**: günstig, schnell im LAN, keine laufenden Kosten. Nach Ablauf der Evaluation Lizenz kaufen oder neu aufsetzen |
| **Azure** VM (z. B. B4ms, 4 vCPU/16 GB, Windows Server 2025 inkl. Lizenz) | ca. 0,20–0,30 €/h + Datenträger ca. 5–10 €/Monat; mit „Automatisch herunterfahren“ und Start nur bei Bedarf meist < 20 €/Monat | gut, wenn kein eigener Host frei ist; Marketplace-Abbild „Visual Studio 2026 on Windows Server“ spart die Installation |
| **AWS** EC2 (z. B. t3.xlarge Windows Server, Lizenz inklusive) | ähnlich Azure, ca. 0,25–0,35 $/h + EBS | wie Azure |
| Windows-Server-Root-Server bei einem Anbieter mit Lizenz (Miete) | ab ca. 30–60 €/Monat | dauerhaft an, für reine Builds überdimensioniert |

**Größe:** 4 vCPU, 8 GB RAM (besser 16 GB), 128 GB SSD (Visual Studio + Windows SDK + NuGet-Cache brauchen 40–60 GB).
**Betriebssystem:** Windows 11 Pro/Enterprise (24H2 oder neuer) **oder** Windows Server 2025 (Desktop-Darstellung).
Für das Ausprobieren der Oberfläche (per RDP) ist Windows 11 näher an den Nutzern.

## 2. Windows installieren und Benutzer `builder`

1. Windows installieren, alle Updates einspielen, automatische Updates an lassen. Proxmox: VirtIO-Treiber, CPU-Typ `host`.
2. Zeitzone/Sprache egal (die Skripte sind sprachneutral); Energieoptionen: nie in den Ruhezustand.
3. Lokalen Benutzer **`builder`** anlegen (Standardbenutzer reicht; die Einrichtung unten machst du mit deinem Admin-Konto):

```powershell
# PowerShell als Administrator
$pw = Read-Host -AsSecureString "Passwort für builder (nur für RDP/Notfall)"
New-LocalUser -Name builder -Password $pw -FullName "RoG Build" -PasswordNeverExpires
Add-LocalGroupMember -Group "Remote Desktop Users" -Member builder   # nur falls du per RDP Oberflächen testen willst
```

## 3. Werkzeuge installieren

Am einfachsten mit **winget** (in Windows 11/Server 2025 enthalten) als Administrator:

```powershell
winget install --id Microsoft.PowerShell -e --source winget         # PowerShell 7 (pwsh) – die Skripte laufen damit
winget install --id Git.Git -e --source winget
winget install --id Microsoft.DotNet.SDK.10 -e --source winget      # .NET 10 SDK (LTS)
```

**Visual Studio** (MSBuild, Windows SDK, MSIX-Werkzeuge, WinUI): Für .NET 10 braucht es **Visual Studio 2026**
(MSBuild 18) – Visual Studio 2022 baut .NET-10-Projekte nicht zuverlässig. Zwei Wege:

- **Visual Studio Community 2026** (kostenlos für Einzelpersonen; mit Oberfläche, praktisch zum Debuggen per RDP) **oder**
- **Build Tools für Visual Studio 2026** (ohne Oberfläche, kleiner).

Im Installer diese Workloads/Komponenten wählen:

| Workload / Komponente | Wofür |
|---|---|
| **.NET-Desktopentwicklung** (Build Tools: „.NET-Desktop-Buildtools“) | C#, MSBuild, .NET-Desktop |
| **Entwicklung von Windows-Anwendungen** bzw. „WinUI-Anwendungsentwicklung“ (enthält *Windows App SDK C#-Vorlagen*) | WinUI 3 / Windows App SDK |
| Einzelne Komponente **Windows 11 SDK (10.0.26100)** | Windows SDK, `makeappx`, `signtool` |
| Einzelne Komponente **MSIX-Paketerstellungstools** | optional – `build-windows.ps1` paketiert mit `dotnet build` über das NuGet-Paket; nur für Visual Studio |

Per Kommandozeile (Paket-ID mit `winget search BuildTools` prüfen – sie heißt je nach Version
`Microsoft.VisualStudio.BuildTools` oder `Microsoft.VisualStudio.2026.BuildTools`):

```powershell
winget install --id Microsoft.VisualStudio.BuildTools -e --source winget --override `
  "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.VisualStudio.Workload.UniversalBuildTools --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --add Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging --includeRecommended"
```

Fehlt nach der Installation etwas, meldet es `scripts/remote-build.sh --check` bzw. der erste Build – dann im
„Visual Studio Installer“ → Ändern nachinstallieren.

**Entwicklermodus** (zum Installieren unsignierter/selbst signierter Testpakete):
Einstellungen → System → Für Entwickler → **Entwicklermodus: Ein**, oder als Administrator:

```powershell
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v AllowDevelopmentWithoutDevLicense /d 1
```

Danach einmal neu starten.

## 4. OpenSSH-Server aktivieren (nur Schlüssel-Login)

Als Administrator in PowerShell:

```powershell
# Server installieren und starten
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd

# PowerShell 7 als Standard-Shell für SSH (die Skripte funktionieren auch mit cmd, so ist es aber bequemer)
New-ItemProperty -Path "HKLM:\SOFTWARE\OpenSSH" -Name DefaultShell -Value "C:\Program Files\PowerShell\7\pwsh.exe" -PropertyType String -Force

# Firewall: SSH nur von dev-linux erlauben (IP von dev-linux eintragen)
Set-NetFirewallRule -Name "OpenSSH-Server-In-TCP" -RemoteAddress <IP-von-dev-linux>
```

Passwort-Login abschalten: `C:\ProgramData\ssh\sshd_config` mit Editor (als Admin) öffnen und setzen:

```
PubkeyAuthentication yes
PasswordAuthentication no
AllowUsers builder
```

Danach `Restart-Service sshd`. (Falls du dich selbst auch per SSH anmelden willst, deinen Benutzer bei `AllowUsers` ergänzen.)

## 5. Schlüssel von dev-linux hinterlegen

Auf **dev-linux** gibt es (Stand 15.10.2026) noch keinen eigenen Schlüssel (`~/.ssh/id_ed25519` fehlt). Lege einen
eigenen Schlüssel nur für die Build-VM an:

```bash
# auf dev-linux
ssh-keygen -t ed25519 -f ~/.ssh/rog-windows-builder -C "dev-linux -> rog-windows builder"
cat ~/.ssh/rog-windows-builder.pub          # diese EINE Zeile kopieren (öffentlicher Schlüssel)
```

(Gibt es später doch einen Standardschlüssel, zeigt `cat ~/.ssh/id_ed25519.pub` dessen öffentlichen Teil – dann in
`build-vm.env` `ROG_VM_KEY=$HOME/.ssh/id_ed25519` setzen.)

Auf der **VM** – weil `builder` kein Administrator ist, gehört der Schlüssel in sein Profil:

```powershell
# als Administrator
$ssh = "C:\Users\builder\.ssh"
New-Item -ItemType Directory -Force -Path $ssh | Out-Null
Set-Content -Path "$ssh\authorized_keys" -Value "ssh-ed25519 AAAA… dev-linux -> rog-windows builder" -Encoding ascii
icacls $ssh /inheritance:r /grant "builder:(OI)(CI)F" /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F"
```

Ist `builder` doch Administrator, liest OpenSSH stattdessen `C:\ProgramData\ssh\administrators_authorized_keys`
(Rechte: nur `SYSTEM` und `Administrators`):

```powershell
Add-Content -Path C:\ProgramData\ssh\administrators_authorized_keys -Value "ssh-ed25519 AAAA… dev-linux -> rog-windows builder" -Encoding ascii
icacls C:\ProgramData\ssh\administrators_authorized_keys /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F"
```

## 6. Optional: Testzertifikat für installierbare Testpakete

Für den Store braucht es **kein** Zertifikat (der Store signiert). Zum Installieren eines Testpakets auf der VM oder
deinem PC (`--mode sideload`) als Benutzer `builder` in PowerShell:

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=BAB3F2CF-D90F-4F06-A6BF-E1AE3CC52136" -KeyUsage DigitalSignature `
  -FriendlyName "RoG Windows Test" -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
[Environment]::SetEnvironmentVariable("ROG_SIGNING_THUMBPRINT", $cert.Thumbprint, "User")
Export-Certificate -Cert $cert -FilePath "$HOME\rog-test.cer"     # .cer (öffentlich) für Testrechner
```

Das Subject muss genau dem `Publisher` in `src/RoG.App/Package.appxmanifest` entsprechen (Store-Identität, siehe
`docs/STORE.md`). Auf einem Testrechner
die `.cer` als Administrator nach „Lokaler Computer → Vertrauenswürdige Personen“ importieren, dann das `.msix`
doppelklicken. Zertifikate (`.pfx`) nie ins Repo.

**Ohne Handarbeit (Standard):** Ist `ROG_SIGNING_THUMBPRINT` nicht gesetzt, sucht `build-windows.ps1 -Mode Sideload`
im Speicher `CurrentUser\My` des ausführenden Kontos ein Zertifikat „RoG Windows Test“ mit `Subject` = `Publisher` aus
dem Manifest und legt es beim ersten Lauf selbst an (2 Jahre gültig). Der öffentliche Teil liegt danach als
`msix/RoG-Test.cer` neben dem Paket. Das gilt auch für den **GitHub-Actions-Runner (`rog-win-1`)**, dessen Dienst als
`NETWORK SERVICE` läuft: Das Zertifikat entsteht dann in dessen Speicher und bleibt dort für spätere Läufe.
Ändert sich der `Publisher` (Store-Reservierung), entsteht automatisch ein neues – Testrechner brauchen dann die neue `.cer`.

Starten: `gh workflow run ci.yml --ref develop -f mode=Sideload` (oder auf GitHub unter Actions → CI → „Run workflow“,
Modus `Sideload`). Das signierte `.msixbundle` und `RoG-Test.cer` liegen im Artefakt `windows-artifacts` unter `msix/`.

**Installieren auf einem Testrechner:** `RoG-Test.cer` als Administrator importieren –
`Import-Certificate -FilePath RoG-Test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople` (oder Doppelklick →
„Lokaler Computer“ → „Vertrauenswürdige Personen“) – dann das `.msixbundle` doppelklicken.

## 7. Prüfen und erster Build (macht Claude)

```bash
# auf dev-linux, einmalig
mkdir -p ~/.config/rog-windows
cp scripts/build-vm.env.example ~/.config/rog-windows/build-vm.env
chmod 600 ~/.config/rog-windows/build-vm.env      # Host/Port/User eintragen

scripts/remote-build.sh --check     # PowerShell-, .NET-, Git-, Visual-Studio-Version, tar vorhanden?
scripts/remote-build.sh             # Core + Tests + App (x64, ARM64) bauen
scripts/remote-build.sh --mode store --committed   # .msixupload für das Partner Center
```

Ergebnisse liegen danach unter `artifacts/remote/<lauf>/` (Konsolenausgabe `build.log`, `artifacts/summary.json`,
Binärlogs, TRX-Testergebnisse, Pakete). Auf der VM bleiben die letzten 5 Läufe unter `C:\Users\builder\rog-build\runs\`.

## 8. Oberfläche ansehen

SSH-Sitzungen haben keinen Desktop – die App lässt sich dort bauen, aber nicht anzeigen. Zum Ansehen:

- per **RDP** als `builder` anmelden, Testpaket aus `rog-build\runs\<lauf>\artifacts\msix\` installieren
  (`Add-AppxPackage`, Entwicklermodus an) und starten, oder in Visual Studio Community direkt `F5`;
- für automatische UI-Tests (ab Stufe 4) bekommt `builder` eine automatische Anmeldung und einen Testlauf per geplanter
  Aufgabe in der Desktop-Sitzung – das richtet Claude dann ein und beschreibt es hier.

## 9. Sicherheit und Betrieb

- Die VM ist nur Build-Maschine: keine privaten Daten, keine Browser-Anmeldungen, keine RoG-Zugangsdaten.
- SSH nur mit Schlüssel und nur von dev-linux (Firewall), RDP nur bei Bedarf und nicht ins Internet öffnen (VPN/LAN).
- Windows Update automatisch; vor großen Änderungen einen Snapshot (Proxmox/Hyper-V) anlegen.
- Cloud-VM: „Automatisch herunterfahren“ setzen und nur zum Bauen starten – sag Claude, wie sie gestartet wird
  (z. B. per `az vm start`), dann kann das in `remote-build.sh` mit hinein.
