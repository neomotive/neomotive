# Publishes Neomotive.ScanTool.RaspberryPi as a self-contained linux-arm64
# payload laid out for the Pi Appliance Kit (/data/app/run).
[CmdletBinding()]
param(
    [string]$TargetHost = "pi@pi-appliance.local",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$Wilderness = "F:\repos\wilderness"
$Dotnet     = "F:\repos\neomotive\software\dotnet"
$ScanTool   = "$Dotnet\Apps\ScanTool"
$Project    = "$ScanTool\Neomotive.ScanTool.RaspberryPi\Neomotive.ScanTool.RaspberryPi.csproj"
$OutDir     = "$Dotnet\publish\scantool-pi"
$PiAssets   = "$ScanTool\scripts\pi"
$RemoteDir  = "/data/app"

# Deploy uses native Windows OpenSSH (scp/ssh) + the built-in bsdtar, NOT the
# kit's install-app.sh. That script needs rsync, which Git for Windows does not
# ship, and it copies with --rsync-path="sudo rsync" even though /data/app is
# owned by the appliance user — on an image with no NOPASSWD sudo rule that
# fails outright, because sudo cannot prompt over a non-tty rsync channel.
function Assert-Tool($name, $hint) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "$name not found on PATH. $hint" }
    return $cmd.Source
}

# Force-rebuild wilderness dependencies so their output timestamps reflect
# today's source. When dotnet publish then evaluates the Pi project, MSBuild
# will see these DLLs as already up-to-date and copy them into the publish
# output without re-running incremental (stale) builds.
# -m:1 because these projects share obj/ state and race under parallel builds.
Write-Host "==> Rebuilding wilderness dependencies (forced)..."

$deps = @(
    "$Wilderness\Meadow.Core\Source\Meadow.Core\Meadow.Core.csproj",
    "$Wilderness\Meadow.Core\Source\ui\Meadow.Avalonia\Meadow.Avalonia.csproj",
    "$Wilderness\Meadow.Core\Source\implementations\linux\Meadow.Linux\Meadow.Linux.csproj",
    "$Wilderness\Meadow.Foundation\Source\Meadow.Foundation.Peripherals\ICs.CAN.Mcp2515\Driver\ICs.CAN.Mcp2515.csproj"
)
foreach ($dep in $deps) {
    dotnet build --no-incremental -m:1 $dep
    if ($LASTEXITCODE -ne 0) { throw "Failed to build $dep" }
}

Write-Host "==> Publishing ScanTool for linux-arm64..."

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }

dotnet publish $Project `
  -c Release `
  -r linux-arm64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:PublishTrimmed=false `
  -m:1 `
  -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# The appliance launcher requires an executable named exactly `run`. Write it
# with LF endings — CRLF makes the kernel reject the #! line ("bad interpreter").
Write-Host "==> Staging appliance entrypoint..."

$runText = (Get-Content "$PiAssets\run" -Raw) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText("$OutDir\run", $runText, (New-Object System.Text.UTF8Encoding $false))

Copy-Item "$PiAssets\neomotive.config.json" "$OutDir\neomotive.config.json"

if (-not (Test-Path "$OutDir\scantool")) { throw "Expected single-file binary $OutDir\scantool not found" }

# NOTE: no chmod here. NTFS carries no POSIX mode bits, and git-bash's chmod is a
# silent no-op on an ELF file. Both exec bits are set remotely after extraction.

Write-Host ""
Write-Host "==> Done. Payload: $OutDir"
Write-Host ""

if (-not $Deploy) {
    Write-Host "To deploy:  .\Apps\ScanTool\scripts\publish-scantool-pi.ps1 -Deploy"
    Write-Host "(or -Deploy -TargetHost pi@192.168.4.41 to skip mDNS)"
    return
}

# --- deploy -----------------------------------------------------------------
# One tar.gz over scp beats copying ~40 loose files: a single connection, one
# password prompt, and the archive carries the directory structure intact.
$scp = Assert-Tool scp "Enable the Windows OpenSSH Client optional feature."
$ssh = Assert-Tool ssh "Enable the Windows OpenSSH Client optional feature."
Assert-Tool tar "Windows 10 1803+ ships bsdtar at C:\Windows\System32\tar.exe." | Out-Null

$stamp   = Get-Date -Format "yyyyMMdd-HHmmss"
$tarball = Join-Path $env:TEMP "scantool-pi-$stamp.tgz"

Write-Host "==> Packing payload..."
# -C so paths inside the archive are relative to the payload root, not F:\...
tar -czf $tarball -C $OutDir .
if ($LASTEXITCODE -ne 0) { throw "tar failed" }
$sizeMb = [math]::Round((Get-Item $tarball).Length / 1MB, 1)
Write-Host "    $sizeMb MB -> $tarball"

try {
    Write-Host "==> Copying to ${TargetHost} (password required)..."
    & $scp $tarball "${TargetHost}:/tmp/scantool-deploy.tgz"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }

    # Extracted over the top rather than wiped: /data/app also holds runtime
    # state (data/, config/) that a --delete-style sync would destroy.
    #
    # chmod runs here because NTFS carried no mode bits. `run` then chmods the
    # scantool binary itself. Only the service restart needs sudo, and -t gives
    # it the tty it needs to prompt.
    $remote = @(
        "mkdir -p $RemoteDir",
        "tar xzf /tmp/scantool-deploy.tgz -C $RemoteDir",
        "rm -f /tmp/scantool-deploy.tgz",
        "chmod +x $RemoteDir/run",
        "sudo systemctl restart app.service"
    ) -join " && "

    Write-Host "==> Installing + restarting app.service (sudo password required)..."
    & $ssh -t $TargetHost $remote
    if ($LASTEXITCODE -ne 0) { throw "Remote install failed" }
}
finally {
    Remove-Item $tarball -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==> Deployed. Watch startup with:"
Write-Host "    ssh $TargetHost 'journalctl -u app.service -f'"
