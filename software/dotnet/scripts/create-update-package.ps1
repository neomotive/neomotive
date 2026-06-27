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
    [Parameter(Mandatory)][ValidateSet("scantool","simulator")] [string] $Target,
    [Parameter(Mandatory)][ValidateSet("windows","linux-arm64")] [string] $Platform,
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
dotnet publish $ProjectPath `
    --configuration Release `
    --runtime $Rid `
    --self-contained true `
    --output $PublishDir `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
Write-Host "Publish complete." -ForegroundColor Green

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
