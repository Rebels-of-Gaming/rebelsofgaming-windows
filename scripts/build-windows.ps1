#Requires -Version 5.1
<#
.SYNOPSIS
  Baut und testet Rebels of Gaming für Windows auf der Build-VM und erzeugt auf Wunsch das MSIX-Paket.

.DESCRIPTION
  Läuft auf der Windows-VM (direkt oder über scripts/remote-build.sh von dev-linux):
    1. Werkzeuge prüfen (.NET SDK, MSBuild aus Visual Studio/Build Tools, Windows SDK)
    2. dotnet restore + build + test (RoG.Core, RoG.Core.Tests)
    3. RoG.App bauen (WinUI 3) – x64 und ARM64
    4. je nach -Mode: kein Paket (Build), Testpaket signiert (Sideload) oder Store-Upload (.msixupload, unsigniert)
  Ergebnisse unter artifacts\: logs\ (Binärlogs), test-results\ (TRX), msix\ (Pakete), summary.json.

.PARAMETER Mode
  Build    – nur bauen und testen (Standard)
  Sideload – MSIX für die lokale Installation, signiert mit -CertificateThumbprint (Zertifikat im Speicher CurrentUser\My)
  Store    – .msixupload für das Partner Center (Signatur übernimmt der Store)

.EXAMPLE
  pwsh -File scripts/build-windows.ps1
  pwsh -File scripts/build-windows.ps1 -Mode Store
  pwsh -File scripts/build-windows.ps1 -Mode Sideload -CertificateThumbprint 0123ABCD...
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Sideload', 'Store')]
    [string] $Mode = 'Build',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string[]] $Platforms = @('x64', 'ARM64'),
    [switch] $SkipTests,
    [string] $CertificateThumbprint = $env:ROG_SIGNING_THUMBPRINT
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts'
$Logs = Join-Path $Artifacts 'logs'
$TestResults = Join-Path $Artifacts 'test-results'
$MsixDir = Join-Path $Artifacts 'msix'
$Solution = Join-Path $Root 'RoG.sln'
$AppProject = Join-Path $Root 'src\RoG.App\RoG.App.csproj'
$started = Get-Date
$summary = [ordered]@{ mode = $Mode; configuration = $Configuration; platforms = $Platforms; steps = @(); ok = $false }

New-Item -ItemType Directory -Force -Path $Logs, $TestResults, $MsixDir | Out-Null

function Write-Step([string] $text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Invoke-Checked([string] $name, [scriptblock] $block) {
    Write-Step $name
    $t = Get-Date
    & $block
    $code = $LASTEXITCODE
    $summary.steps += [ordered]@{ name = $name; exit = $code; seconds = [math]::Round(((Get-Date) - $t).TotalSeconds, 1) }
    if ($code -ne 0) {
        throw "$name fehlgeschlagen (Exit-Code $code)"
    }
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    # .NET 10 braucht MSBuild 18 (Visual Studio/Build Tools 2026) – ältere Installationen (2022) daneben ignorieren
    $path = & $vswhere -all -prerelease -latest -products * -version '[18.0,19.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $path) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
    if ($path -and (Test-Path $path)) { return $path }
    return $null
}

try {
    Push-Location $Root

    Write-Step 'Werkzeuge'
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw '.NET SDK fehlt (siehe docs/BUILD-VM.md, Abschnitt 3).' }
    $sdk = (& dotnet --version)
    Write-Host ".NET SDK: $sdk"
    $msbuild = Find-MSBuild
    if ($msbuild) { Write-Host "MSBuild:  $msbuild (nur Info – gebaut und paketiert wird mit dotnet)" } else { Write-Host 'MSBuild:  nicht gefunden (nicht nötig – gebaut und paketiert wird mit dotnet).' }
    $winSdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue | Where-Object Name -Match '^10\.' | Sort-Object Name | Select-Object -Last 1
    if ($winSdk) { Write-Host "Windows SDK: $($winSdk.Name)" } else { Write-Host 'Windows SDK: nicht gefunden (MSIX-Werkzeuge kommen dann nur aus dem NuGet-Paket Microsoft.Windows.SDK.BuildTools).' -ForegroundColor Yellow }
    $summary.dotnet = $sdk
    $summary.msbuild = $msbuild

    if ($Mode -eq 'Sideload' -and -not $CertificateThumbprint) {
        throw 'Sideload braucht -CertificateThumbprint bzw. ROG_SIGNING_THUMBPRINT (Testzertifikat, docs/BUILD-VM.md Abschnitt 6).'
    }

    Invoke-Checked 'Restore' { dotnet restore $Solution -p:Platform=x64 "-bl:$Logs\restore.binlog" }

    Invoke-Checked 'Build Core + Tests' {
        dotnet build (Join-Path $Root 'tests\RoG.Core.Tests\RoG.Core.Tests.csproj') -c $Configuration --no-restore "-bl:$Logs\core.binlog"
    }

    if (-not $SkipTests) {
        Invoke-Checked 'Tests' {
            dotnet test (Join-Path $Root 'tests\RoG.Core.Tests\RoG.Core.Tests.csproj') -c $Configuration --no-build `
                --logger "trx;LogFileName=RoG.Core.Tests.trx" --results-directory $TestResults
        }
    }

    foreach ($platform in $Platforms) {
        $rid = if ($platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
        if ($Mode -eq 'Build') {
            Invoke-Checked "App bauen ($platform)" {
                dotnet build $AppProject -c $Configuration -p:Platform=$platform -r $rid "-bl:$Logs\app-$platform.binlog"
            }
        }
    }

    if ($Mode -ne 'Build') {
        # Paketieren mit dem .NET SDK (dotnet build) statt MSBuild.exe: Den Build Tools 2026 auf dem Runner fehlt der
        # .NET-SDK-Resolver (MSB4236 „Microsoft.NET.Sdk nicht gefunden“). Die MSIX-Werkzeuge kommen aus dem NuGet-Paket
        # Microsoft.Windows.SDK.BuildTools(.MSIX) (EnableMsixTooling), Visual Studio wird dafür nicht gebraucht.
        $common = @(
            'build', $AppProject, '-c', $Configuration, '-p:Platform=x64',
            '-p:GenerateAppxPackageOnBuild=true', '-p:AppxBundle=Always', "-p:AppxBundlePlatforms=$($Platforms -join '|')",
            "-p:AppxPackageDir=$MsixDir\",
            # Symbolpaket (.appxsym) braucht mspdbcmf.exe aus Visual Studio (FastLink-PDB-Umwandlung) – ohne VS aus.
            '-p:AppxSymbolPackageEnabled=false'
        )
        if ($Mode -eq 'Store') {
            Invoke-Checked 'MSIX für den Store' {
                dotnet @common '-p:UapAppxPackageBuildMode=StoreUpload' '-p:AppxPackageSigningEnabled=false' "-bl:$Logs\msix-store.binlog"
            }
        }
        else {
            Invoke-Checked 'MSIX zum Testen (signiert)' {
                dotnet @common '-p:UapAppxPackageBuildMode=SideloadOnly' '-p:AppxPackageSigningEnabled=true' `
                    "-p:PackageCertificateThumbprint=$CertificateThumbprint" "-bl:$Logs\msix-sideload.binlog"
            }
        }
        $summary.packages = @(Get-ChildItem $MsixDir -Recurse -Include *.msix, *.msixbundle, *.msixupload | ForEach-Object { $_.FullName.Substring($Root.Length + 1) })
    }

    $summary.ok = $true
}
catch {
    $summary.error = $_.Exception.Message
    Write-Host ''
    Write-Host "FEHLER: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Pop-Location
    $summary.seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    $summary | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $Artifacts 'summary.json')
    Write-Host ''
    Write-Host ("Ergebnis: {0} in {1} s – artifacts\summary.json" -f ($(if ($summary.ok) { 'OK' } else { 'FEHLGESCHLAGEN' })), $summary.seconds)
}

if (-not $summary.ok) { exit 1 }
exit 0
