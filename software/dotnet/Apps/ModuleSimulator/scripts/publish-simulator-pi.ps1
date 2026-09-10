# Publishes Neomotive.ModuleSimulator.RaspberryPi as a self-contained linux-arm64
# payload laid out for the Pi Appliance Kit (/data/app/run).
#
# Mirrors Apps/ScanTool/scripts/publish-scantool-pi.ps1 — the only real
# difference is the X server: the simulator is a windowed Avalonia app, so the
# payload carries an xinitrc and an xorg.conf beside `run`. See scripts/pi/README.md.
[CmdletBinding()]
param(
    [string]$TargetHost = "pi@neomotive-sim.local",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$Dotnet    = "F:\repos\neomotive\software\dotnet"
$Simulator = "$Dotnet\Apps\ModuleSimulator"
$Project   = "$Simulator\Neomotive.ModuleSimulator.RaspberryPi\Neomotive.ModuleSimulator.RaspberryPi.csproj"
$OutDir    = "$Dotnet\publish\simulator-pi"
# The binary lives in the A/B slot app-current/; the launcher scripts and the
# device-local config sit alongside it at the payload root so an in-app slot
# swap never touches them. See docs/updates/release-and-update.md.
$SlotDir   = "$OutDir\app-current"
$PiAssets  = "$Simulator\scripts\pi"
$RemoteDir = "/data/app"

# Every Meadow dependency comes from nuget.org at 3.*-*. Nothing is built from
# source: a source-built Meadow.Contracts has no <Version> on meadow-3.0 and so
# compiles as 1.0.0.0, while the Meadow NuGet assemblies bind to 3.0.1.0 — which
# is exactly what crash-looped the ScanTool's 1.1.1 package on the device.
function Assert-Tool($name, $hint) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "$name not found on PATH. $hint" }
    return $cmd.Source
}

Write-Host "==> Publishing ModuleSimulator for linux-arm64..."

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }

dotnet publish $Project `
  -c Release `
  -r linux-arm64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:PublishTrimmed=false `
  -m:1 `
  -o $SlotDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# The appliance launcher requires an executable named exactly `run`. These are
# written with LF endings — CRLF makes the kernel reject the #! line ("bad
# interpreter"), and app.service then restarts it forever with no visible cause.
Write-Host "==> Staging appliance entrypoint..."

foreach ($name in @("run", "xinitrc", "xorg.conf")) {
    $text = (Get-Content "$PiAssets\$name" -Raw) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText("$OutDir\$name", $text, (New-Object System.Text.UTF8Encoding $false))
}

# Also inside the slot, so an OTA update can fix the launcher. $APP_DIR/run hands
# off to app-current/launcher/run when it is present and parses.
$LauncherDir = New-Item -ItemType Directory -Force "$SlotDir\launcher"
foreach ($name in @("run", "xinitrc", "xorg.conf")) {
    Copy-Item "$OutDir\$name" (Join-Path $LauncherDir $name)
}

# Staged as .default: the live neomotive.config.json holds the device's update
# server URL, and the deploy untars over the top without deleting. Shipping the
# real name would reset every device's updateServerUrl to null on each deploy.
Copy-Item "$PiAssets\neomotive.config.json" "$OutDir\neomotive.config.json.default"

if (-not (Test-Path "$SlotDir\simulator")) { throw "Expected single-file binary $SlotDir\simulator not found" }

# NOTE: no chmod here. NTFS carries no POSIX mode bits, and git-bash's chmod is a
# silent no-op on an ELF file. Both exec bits are set remotely after extraction.

Write-Host ""
Write-Host "==> Done. Payload: $OutDir"
Write-Host ""

if (-not $Deploy) {
    Write-Host "To deploy:  .\Apps\ModuleSimulator\scripts\publish-simulator-pi.ps1 -Deploy"
    Write-Host "(or -Deploy -TargetHost pi@192.168.4.31 to skip mDNS)"
    return
}

# --- deploy -----------------------------------------------------------------
# One tar.gz over scp beats copying ~40 loose files: a single connection, one
# password prompt, and the archive carries the directory structure intact.
$scp = Assert-Tool scp "Enable the Windows OpenSSH Client optional feature."
$ssh = Assert-Tool ssh "Enable the Windows OpenSSH Client optional feature."
Assert-Tool tar "Windows 10 1803+ ships bsdtar at C:\Windows\System32\tar.exe." | Out-Null

$stamp   = Get-Date -Format "yyyyMMdd-HHmmss"
$tarball = Join-Path $env:TEMP "simulator-pi-$stamp.tgz"

Write-Host "==> Packing payload..."
# -C so paths inside the archive are relative to the payload root, not F:\...
tar -czf $tarball -C $OutDir .
if ($LASTEXITCODE -ne 0) { throw "tar failed" }
$sizeMb = [math]::Round((Get-Item $tarball).Length / 1MB, 1)
Write-Host "    $sizeMb MB -> $tarball"

try {
    Write-Host "==> Copying to ${TargetHost} (password required)..."
    & $scp $tarball "${TargetHost}:/tmp/simulator-deploy.tgz"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }

    # STOP the service before extracting — do not "restart" afterwards.
    # app.service has Restart=always, so a crash-looping app gets re-exec'd every
    # 2s. Unpacking a ~150MB single-file binary onto SD takes longer than that,
    # and writing an image that is concurrently being exec'd races with ETXTBSY.
    #
    # Extracted over the top rather than wiped: /data/app also holds runtime
    # state (data/, config/) that a --delete-style sync would destroy.
    $expected = (Get-Item "$SlotDir\simulator").Length
    $remote = @(
        "sudo systemctl stop app.service",
        "mkdir -p $RemoteDir",
        "tar xzf /tmp/simulator-deploy.tgz -C $RemoteDir",
        "rm -f /tmp/simulator-deploy.tgz",
        "chmod +x $RemoteDir/run $RemoteDir/xinitrc",
        # First deploy on a device seeds the config; later ones leave the URL alone.
        "if [ ! -f $RemoteDir/neomotive.config.json ]; then cp $RemoteDir/neomotive.config.json.default $RemoteDir/neomotive.config.json; fi",
        # Fail loudly here rather than as a boot loop the operator has to decode.
        "actual=`$(stat -c%s $RemoteDir/app-current/simulator)",
        "if [ `"`$actual`" != `"$expected`" ]; then echo `"TRUNCATED: `$actual != $expected`"; exit 1; fi",
        # A device deployed before A/B slots has the old binary loose in /data/app.
        "rm -f $RemoteDir/simulator",
        "sudo systemctl start app.service"
    ) -join " && "

    Write-Host "==> Installing + starting app.service (sudo password required)..."
    & $ssh -t $TargetHost $remote
    if ($LASTEXITCODE -ne 0) { throw "Remote install failed (see output above)" }
}
finally {
    Remove-Item $tarball -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==> Deployed. Watch startup with:"
Write-Host "    ssh $TargetHost 'journalctl -u app.service -f'"
