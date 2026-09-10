<#
.SYNOPSIS
    Publishes a Neomotive app and packages it as an update zip.

.PARAMETER Target
    Which app to build: "scantool" or "simulator"

.PARAMETER Platform
    Target platform: "windows" or "linux-arm64"

.PARAMETER Version
    Package version string, e.g. "1.2.0"

.PARAMETER OutputDir
    Directory to write the output zip and version-manifest.json. Defaults to .\dist\

.EXAMPLE
    .\create-update-package.ps1 -Target scantool -Platform windows -Version 1.2.0

.EXAMPLE
    .\create-update-package.ps1 -Target simulator -Platform linux-arm64 -Version 1.2.0
#>

param(
    [ValidateSet("scantool","simulator")]  [string] $Target   = "simulator",
    [ValidateSet("windows","linux-arm64")] [string] $Platform = "linux-arm64",
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputDir = "$PSScriptRoot\..\..\..\dist"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve project path ──────────────────────────────────────────────────────

$AppsRoot = Resolve-Path "$PSScriptRoot\..\Apps"

$ProjectPath = switch ("$Target-$Platform") {
    "scantool-windows"      { "$AppsRoot\ScanTool\Neomotive.ScanTool.Desktop\Neomotive.ScanTool.Desktop.csproj" }
    "scantool-linux-arm64"  { "$AppsRoot\ScanTool\Neomotive.ScanTool.RaspberryPi\Neomotive.ScanTool.RaspberryPi.csproj" }
    "simulator-windows"     { "$AppsRoot\ModuleSimulator\Neomotive.ModuleSimulator.Desktop\Neomotive.ModuleSimulator.Desktop.csproj" }
    "simulator-linux-arm64" { "$AppsRoot\ModuleSimulator\Neomotive.ModuleSimulator.RaspberryPi\Neomotive.ModuleSimulator.RaspberryPi.csproj" }
    default                 { throw "Unknown target/platform combination: $Target/$Platform" }
}

if (-not (Test-Path $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

$Rid = if ($Platform -eq "windows") { "win-x64" } else { "linux-arm64" }
$PackageName = "neomotive-update-$Version-$Target-$Platform"
$WorkDir     = Join-Path ([System.IO.Path]::GetTempPath()) $PackageName
$PublishDir  = Join-Path $WorkDir "app"
$ZipPath     = Join-Path (New-Item -ItemType Directory -Force $OutputDir).FullName "$PackageName.zip"

Write-Host "=== Neomotive Update Package Builder ===" -ForegroundColor Cyan
Write-Host "  Target   : $Target"
Write-Host "  Platform : $Platform ($Rid)"
Write-Host "  Version  : $Version"
Write-Host "  Output   : $ZipPath"
Write-Host ""

# ── Clean work dir ────────────────────────────────────────────────────────────

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
New-Item -ItemType Directory -Force $PublishDir | Out-Null

# ── Publish ───────────────────────────────────────────────────────────────────

Write-Host "Publishing..." -ForegroundColor Yellow
# NeomotivePackageVersion, not Version/AssemblyVersion/FileVersion. A property on
# the command line is a GLOBAL property: MSBuild flows it into every project in
# the graph, ProjectReferences included. This repo builds Meadow.Contracts,
# Meadow.Logging, Meadow.Units and Telematics.* from source, so -p:Version=1.1.1
# stamped THOSE with 1.1.1 too — while the Meadow NuGet packages still bound to
# Meadow.Contracts 3.0.1.0. The 1.1.1 package installed and then crash-looped the
# device with "Could not load file or assembly 'Meadow.Contracts, Version=3.0.1.0'".
# The app csproj reads this property; the Meadow projects ignore it.
dotnet publish $ProjectPath `
    --configuration Release `
    --runtime $Rid `
    --self-contained true `
    --output $PublishDir `
    -p:NeomotivePackageVersion=$Version

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
Write-Host "Publish complete." -ForegroundColor Green

# ── Guard: the Meadow libraries must keep their own versions ──────────────────
# The 1.1.1 ScanTool package shipped with Meadow.Contracts stamped 1.1.1, so the
# NuGet Meadow assemblies — which bind to Meadow.Contracts 3.0.1.0 — could not
# load it. The device installed the update and then crash-looped on every start,
# which on an appliance with no console looks exactly like a bricked box. Fail
# the build here instead: a package that cannot start is worse than no package.

Write-Host "Verifying Meadow assembly versions..." -ForegroundColor Yellow

$MeadowLibs = @("Meadow.Contracts", "Meadow.Logging", "Meadow.Units", "Meadow")
$VersionErrors = @()

foreach ($lib in $MeadowLibs) {
    $dll = Join-Path $PublishDir "$lib.dll"
    if (-not (Test-Path $dll)) { continue }

    $asmVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
    if ($asmVersion.Major -lt 3) {
        $VersionErrors += "  $lib.dll is $asmVersion (expected 3.x)"
    }
}

if ($VersionErrors.Count -gt 0) {
    Write-Host "Meadow assemblies were stamped with the app's version:" -ForegroundColor Red
    $VersionErrors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "Meadow assembly version check failed. A command-line -p:Version is a global " +
          "MSBuild property and flows into the source-built Meadow ProjectReferences. " +
          "Pass -p:NeomotivePackageVersion instead, which only the app project reads."
}

$AppAsm = Join-Path $PublishDir "$(if ($Target -eq 'scantool') { 'scantool' } else { 'simulator' }).dll"
if (Test-Path $AppAsm) {
    $appVersion = [System.Reflection.AssemblyName]::GetAssemblyName($AppAsm).Version
    # The device compares this against the manifest; if the stamp silently failed
    # to apply, the update would install and then never be recognised as newer.
    if ("$($appVersion.Major).$($appVersion.Minor).$($appVersion.Build)" -ne $Version) {
        throw "App assembly is version $appVersion but the package is $Version — the version stamp did not apply."
    }
    Write-Host "  app assembly $appVersion, Meadow assemblies 3.x — OK" -ForegroundColor Green
}

# ── Bundle the Pi launcher scripts ────────────────────────────────────────────
# The device's entrypoint ($APP_DIR/run) sits outside the A/B slots, so an update
# — which only replaces app-current/ — can never reach it. Shipping the launcher
# inside the payload lets $APP_DIR/run hand off to app-current/launcher/run, so a
# launcher fix rides along with the binary instead of needing SSH on every device.
#
# LF endings are written explicitly: these are extensionless, and a CRLF shebang
# makes app.service fail with "bad interpreter: /bin/sh^M".
#
# xorg.conf rides along for the simulator so the modesetting stanza does not
# have to be installed onto the read-only rootfs at /etc/X11 — `run` hands it
# to the X server with -config.

if ($Platform -eq "linux-arm64") {
    $ScriptsDir = switch ($Target) {
        "scantool"  { "$AppsRoot\ScanTool\scripts\pi" }
        "simulator" { "$AppsRoot\ModuleSimulator\scripts\pi" }
    }
    $LauncherFiles = switch ($Target) {
        "scantool"  { @("run") }
        "simulator" { @("run", "xinitrc", "xorg.conf") }
    }

    $LauncherDir = Join-Path $PublishDir "launcher"
    New-Item -ItemType Directory -Force $LauncherDir | Out-Null

    foreach ($name in $LauncherFiles) {
        $src = Join-Path $ScriptsDir $name
        if (-not (Test-Path $src)) { throw "launcher script not found: $src" }
        $text = (Get-Content $src -Raw) -replace "`r`n", "`n"
        [System.IO.File]::WriteAllText((Join-Path $LauncherDir $name), $text, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  bundled launcher/$name" -ForegroundColor DarkGray
    }
}

# ── Compute SHA256 for every published file ───────────────────────────────────

Write-Host "Computing hashes..." -ForegroundColor Yellow

$Files = Get-ChildItem -Path $PublishDir -Recurse -File
$FileEntries = foreach ($f in $Files) {
    $rel = "app/" + ($f.FullName.Substring($PublishDir.Length).TrimStart('\','/').Replace('\','/'))
    $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [PSCustomObject]@{ path = $rel; sha256 = $hash }
}

# ── Write update.json ─────────────────────────────────────────────────────────

$Manifest = [ordered]@{
    version   = $Version
    target    = $Target
    platform  = $Platform
    type      = "full"
    timestamp = (Get-Date -Format "o")
    files     = @($FileEntries)
}

$ManifestPath = Join-Path $WorkDir "update.json"
$Manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $ManifestPath -Encoding UTF8
Write-Host "Wrote update.json with $($FileEntries.Count) file entries." -ForegroundColor Green

# ── Zip everything ────────────────────────────────────────────────────────────

Write-Host "Creating zip: $ZipPath" -ForegroundColor Yellow

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$WorkDir\*" -DestinationPath $ZipPath
Write-Host "Zip created." -ForegroundColor Green

# ── Compute zip SHA256 (for version-manifest.json) ───────────────────────────

$ZipHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# ── Update (or create) version-manifest.json ─────────────────────────────────

$ManifestFile = Join-Path (Split-Path $ZipPath) "version-manifest.json"
$VersionManifest = if (Test-Path $ManifestFile) {
    Get-Content $ManifestFile -Raw | ConvertFrom-Json -AsHashtable
} else {
    @{}
}

$Key = "$Target-$Platform"
$VersionManifest[$Key] = [ordered]@{
    version = $Version
    url     = "http://localhost:8080/$PackageName.zip"   # update URL before deploying
    sha256  = $ZipHash
}

$VersionManifest | ConvertTo-Json -Depth 3 | Set-Content -Path $ManifestFile -Encoding UTF8
Write-Host "Updated version-manifest.json key: $Key" -ForegroundColor Green

# ── Clean up temp dir ─────────────────────────────────────────────────────────

Remove-Item $WorkDir -Recurse -Force

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "  Package : $ZipPath"
Write-Host "  Zip SHA : $ZipHash"
Write-Host ""
Write-Host "To test locally, serve the dist/ folder and set updateServerUrl in neomotive.config.json:"
Write-Host "  python -m http.server 8080 --directory dist"
Write-Host "  # neomotive.config.json: { ""updateServerUrl"": ""http://localhost:8080/version-manifest.json"" }"
