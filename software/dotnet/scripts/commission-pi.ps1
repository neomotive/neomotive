<#
.SYNOPSIS
    Commissions a Raspberry Pi appliance from first boot to running app.

.DESCRIPTION
    Automates every step of docs/commissioning-a-pi.md that happens after the SD
    card is in the Pi and powered on. Flashing the image and editing config.txt
    are physical steps and stay manual — everything from the first SSH onward is
    here.

    Idempotent: re-running on a commissioned device re-checks everything and
    re-deploys. Safe to re-run after a failure.

    The read-only overlay is lifted and restored automatically, with the two
    reboots that implies. Each reboot is waited on, not slept through.

.PARAMETER Target
    Which app to install: "scantool" or "simulator".

.PARAMETER TargetHost
    user@host to commission. Defaults to pi@pi-appliance.local — the identity a
    freshly flashed appliance image ships with.

.PARAMETER Hostname
    Rename the device to this (persistently: hostnamectl + /etc/hosts, with the
    overlay down). Omit to leave the hostname alone.

.PARAMETER UpdateServerUrl
    Written to the device's neomotive.config.json. Omit to leave it alone; on a
    device that has never been commissioned it stays null and the Updates screen
    will have nothing to poll.

.PARAMETER SkipDeploy
    Do the device prep but do not build or install the app.

.PARAMETER SkipPrep
    Deploy only. Skips the overlay lift entirely — use on a device already
    commissioned, when you just want new bits on it.

.PARAMETER DryRun
    Print every remote command instead of running it. Nothing is changed.

.EXAMPLE
    .\commission-pi.ps1 -Target scantool

.EXAMPLE
    .\commission-pi.ps1 -Target simulator -Hostname neomotive-sim `
        -UpdateServerUrl "https://github.com/ctacke/neomotive/releases/download/updates-latest/version-manifest.json"

.EXAMPLE
    .\commission-pi.ps1 -Target scantool -SkipPrep      # redeploy only

.EXAMPLE
    $env:NEOMOTIVE_PI_PASSWORD = 'pi123!'
    .\commission-pi.ps1 -Target scantool                 # unattended
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("scantool", "simulator")]
    [string] $Target,

    [string] $TargetHost = "pi@pi-appliance.local",

    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,62}$')]
    [string] $Hostname,

    [string] $UpdateServerUrl,

    # Pin the device's SSH host key, e.g. "SHA256:ey3Fiwdhy...". Omit and the
    # script learns it from the device on first contact — right for a lab LAN,
    # wrong if you are commissioning across an untrusted network.
    [string] $HostKey,

    # The appliance image ships a published default password, so this is a
    # convenience for lab and CI use, not a secret worth protecting here. Prefer
    # the NEOMOTIVE_PI_PASSWORD environment variable to keep it out of shell
    # history. Omit both and the script prompts.
    [string] $Password,

    [switch] $SkipDeploy,
    [switch] $SkipPrep,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Dotnet    = Split-Path $PSScriptRoot -Parent
$RemoteDir = "/data/app"
$User, $HostName_ = $TargetHost -split '@', 2
if (-not $HostName_) { throw "TargetHost must be user@host, got '$TargetHost'" }

$AppPaths = @{
    scantool  = @{
        Scripts = "$Dotnet\Apps\ScanTool\scripts"
        Pi      = "$Dotnet\Apps\ScanTool\scripts\pi"
        Publish = "$Dotnet\Apps\ScanTool\scripts\publish-scantool-pi.ps1"
        Binary  = "scantool"
    }
    simulator = @{
        Scripts = "$Dotnet\Apps\ModuleSimulator\scripts"
        Pi      = "$Dotnet\Apps\ModuleSimulator\scripts\pi"
        Publish = "$Dotnet\Apps\ModuleSimulator\scripts\publish-simulator-pi.ps1"
        Binary  = "simulator"
    }
}
$App = $AppPaths[$Target]

# ── Output ────────────────────────────────────────────────────────────────────

$script:Warnings = @()
$script:HostKeyResolved = $false
function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    [ok]   $m" -ForegroundColor Green }
function Info($m) { Write-Host "    $m" -ForegroundColor Gray }
function Warn($m) { Write-Host "    [warn] $m" -ForegroundColor Yellow; $script:Warnings += $m }
function Fail($m) { throw $m }

# ── Transport ─────────────────────────────────────────────────────────────────
# plink/pscp when present: they take a password non-interactively, which is what
# makes an unattended run possible. Native OpenSSH otherwise, which will prompt
# once per connection — workable, just not unattended.

$Plink = (Get-Command plink -ErrorAction SilentlyContinue)?.Source
$Pscp  = (Get-Command pscp  -ErrorAction SilentlyContinue)?.Source
$UsePutty = $Plink -and $Pscp

if (-not $UsePutty) {
    Info "plink/pscp not on PATH — falling back to OpenSSH. Expect a password prompt per step."
    Info "  (winget install PuTTY.PuTTY  makes this unattended.)"
}

$script:Password = if ($Password) { $Password } else { $env:NEOMOTIVE_PI_PASSWORD }

function Get-DevicePassword {
    if ($script:Password) { return $script:Password }
    if (-not [Environment]::UserInteractive -or $Host.Name -eq 'Default Host') {
        Fail "No password available and this session cannot prompt. Pass -Password or set NEOMOTIVE_PI_PASSWORD."
    }
    $secure = Read-Host "Password for $TargetHost" -AsSecureString
    $script:Password = [System.Net.NetworkCredential]::new('', $secure).Password
    return $script:Password
}

# plink has no "accept any key" switch (0.76 has neither -auto-store-sshkey nor a
# wildcard -hostkey), and a reflashed device presents a new key every time, so a
# cached entry is more often stale than a real MITM signal on a lab LAN. Learn
# the fingerprint from the device on first contact and pin it for the rest of the
# run — that at least makes every later connection consistent with the first.
# Pass -HostKey to pin it up front instead.
$script:ResolvedHostKey = $HostKey

function Resolve-HostKey {
    if ($null -ne $script:ResolvedHostKey -and $script:ResolvedHostKey -ne '') { return $script:ResolvedHostKey }
    if ($script:HostKeyResolved) { return $script:ResolvedHostKey }
    if (-not $UsePutty) { return $null }

    $probe = & $Plink -batch -pw (Get-DevicePassword) $TargetHost "true" 2>&1 | Out-String
    if ($probe -match '(SHA256:[A-Za-z0-9+/=]{20,})') {
        $script:ResolvedHostKey = $Matches[1]
        Info "learned host key $($script:ResolvedHostKey)"
    }
    else {
        # Already cached in the registry from an earlier session — plink accepts
        # it without -hostkey.
        $script:ResolvedHostKey = ''
    }
    $script:HostKeyResolved = $true
    return $script:ResolvedHostKey
}

function Invoke-Pi {
    param([string] $Command, [switch] $AllowFail, [switch] $Tty)

    if ($DryRun) { Write-Host "    [dry] $Command" -ForegroundColor DarkGray; return "" }

    if ($UsePutty) {
        $pwd_ = Get-DevicePassword
        $args_ = @('-batch', '-pw', $pwd_)
        $hk = Resolve-HostKey
        if ($hk) { $args_ += @('-hostkey', $hk) }
        if ($Tty) { $args_ += '-t' }
        $out = & $Plink @args_ $TargetHost $Command 2>&1
    }
    else {
        $args_ = @('-o', 'StrictHostKeyChecking=no', '-o', 'UserKnownHostsFile=/dev/null')
        if ($Tty) { $args_ += '-t' }
        $out = & ssh @args_ $TargetHost $Command 2>&1
    }

    $code = $LASTEXITCODE
    # The kit's own hostname warning is noise on every single sudo call until
    # /etc/hosts is fixed — which is one of the things this script fixes.
    $text = ($out | Where-Object { $_ -notmatch 'unable to resolve host' }) -join "`n"

    if ($code -ne 0 -and -not $AllowFail) {
        Fail "Remote command failed (exit $code):`n  $Command`n$text"
    }
    return $text
}

function Copy-ToPi {
    param([string[]] $Paths, [string] $Destination)

    foreach ($p in $Paths) {
        if (-not (Test-Path $p)) { Fail "Not found: $p" }
    }
    if ($DryRun) { Write-Host "    [dry] copy $($Paths.Count) file(s) -> ${TargetHost}:$Destination" -ForegroundColor DarkGray; return }

    if ($UsePutty) {
        $pwd_ = Get-DevicePassword
        $args_ = @('-batch', '-pw', $pwd_)
        $hk = Resolve-HostKey
        if ($hk) { $args_ += @('-hostkey', $hk) }
        & $Pscp @args_ @Paths "${TargetHost}:$Destination" | Out-Null
    }
    else {
        & scp -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null @Paths "${TargetHost}:$Destination" | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { Fail "Copy to $Destination failed" }
}

function Wait-ForPi {
    param([int] $TimeoutSeconds = 240, [string] $Because = "device")

    Info "waiting for $Because..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    # A pause first: right after `reboot` the old sshd is still answering, and
    # connecting into it would look like success and then die mid-command.
    Start-Sleep -Seconds 15

    while ((Get-Date) -lt $deadline) {
        $probe = Invoke-Pi "echo up" -AllowFail
        if ($probe -match 'up') { Ok "$Because is back"; return }
        Start-Sleep -Seconds 5
    }
    Fail "$Because did not come back within $TimeoutSeconds s. Check the console page on the panel for its IP."
}

# ── Overlay control ───────────────────────────────────────────────────────────
# The rootfs is a read-only overlay. Anything installed to /usr, /etc or /var
# with it up lives in RAM and vanishes at the next reboot — which looks exactly
# like a successful install until the device is power-cycled.

function Get-OverlayState {
    $fs = Invoke-Pi "findmnt -no FSTYPE /" -AllowFail
    return ($fs -match 'overlay')
}

function Set-Overlay {
    param([ValidateSet("up", "down")] [string] $State)

    $isUp = Get-OverlayState
    if (($State -eq "up" -and $isUp) -or ($State -eq "down" -and -not $isUp)) {
        Info "overlay already $State"
        return
    }

    $verb = if ($State -eq "down") { "disable_overlayfs" } else { "enable_overlayfs" }
    Info "taking overlay $State (device will reboot)"

    # /boot/firmware is mounted read-only on this image, and both raspi-config
    # verbs rewrite cmdline.txt. Without the remount raspi-config exits non-zero,
    # the reboot still happens, and the device comes back in the state it was
    # already in — which reads as "the reboot didn't take" unless you check.
    $out = Invoke-Pi @"
sudo mount -o remount,rw /boot/firmware
sudo raspi-config nonint $verb
"@ -AllowFail

    if ($out -match 'a password is required|terminal is required') {
        Fail @"
sudo needs a password, so the overlay cannot be toggled unattended.

Run this once, then re-run the script:

  ssh -t $TargetHost "sudo mount -o remount,rw /boot/firmware && sudo raspi-config nonint $verb && sudo reboot"
"@
    }

    Invoke-Pi "sudo systemctl reboot" -AllowFail | Out-Null
    Wait-ForPi -Because "device after overlay $State"

    $nowUp = Get-OverlayState
    if ($nowUp -ne ($State -eq "up")) {
        Fail @"
Overlay did not go $State — the device rebooted but came back with the rootfs
$(if ($nowUp) { 'still an overlay' } else { 'still writable' }).

Check by hand:
  ssh $TargetHost 'grep -o boot=overlay /boot/firmware/cmdline.txt; findmnt -no FSTYPE /'
  ssh -t $TargetHost 'sudo mount -o remount,rw /boot/firmware && sudo raspi-config nonint $verb && sudo reboot'
"@
    }
    Ok "overlay is $State"
}

# ── Sudo ──────────────────────────────────────────────────────────────────────

function Assert-Sudo {
    $r = Invoke-Pi "sudo -n true 2>&1 && echo NOPASSWD || echo NEEDS-PASSWORD" -AllowFail
    if ($r -match 'NOPASSWD') { Ok "passwordless sudo available"; return $true }

    Warn "sudo needs a password on this device, so it cannot run unattended."
    Write-Host ""
    Write-Host "    Grant passwordless sudo once (this is a lab appliance with a published" -ForegroundColor Yellow
    Write-Host "    default password, so it is not the thing protecting it):" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "      ssh -t $TargetHost 'sudo raspi-config nonint disable_overlayfs && sudo reboot'" -ForegroundColor White
    Write-Host "      ssh -t $TargetHost `"echo 'pi ALL=(ALL) NOPASSWD: ALL' | sudo tee /etc/sudoers.d/010_pi-nopasswd`"" -ForegroundColor White
    Write-Host ""
    Write-Host "    ...then re-run this script. Or answer each prompt as it comes." -ForegroundColor Yellow
    Write-Host ""
    return $false
}

# ══ Preflight ═════════════════════════════════════════════════════════════════

Write-Host "=== Neomotive Pi Commissioning ===" -ForegroundColor Cyan
Write-Host "  Target   : $Target"
Write-Host "  Device   : $TargetHost"
if ($Hostname) { Write-Host "  Rename to: $Hostname" }
if ($DryRun)   { Write-Host "  DRY RUN — nothing will be changed" -ForegroundColor Yellow }

Step "Reaching the device"
$probe = Invoke-Pi "echo up" -AllowFail
if ($probe -notmatch 'up') {
    Fail @"
Cannot reach $TargetHost.

  - First boot takes ~40 s (it creates the /data partition).
  - If mDNS is not resolving, the panel shows a console status page with the
    device's IP and the exact ssh line to use. Pass it as -TargetHost.
  - A reflashed device presents a new host key; this script accepts any key,
    but your own ssh client may refuse until you clear the old entry.
"@
}
Ok "reachable"

$sudoOk = Assert-Sudo

Step "Checking hardware"
$hw = Invoke-Pi @"
echo "data=`$( [ -d /data ] && echo yes || echo no )"
echo "dri=`$( ls /dev/dri/card* >/dev/null 2>&1 && echo yes || echo no )"
echo "spi=`$( [ -e /dev/spidev0.0 ] && echo yes || echo no )"
echo "rootfree=`$(df -Pm / | awk 'NR==2{print `$4}')"
echo "datafree=`$(df -Pm /data 2>/dev/null | awk 'NR==2{print `$4}')"
"@

foreach ($line in $hw -split "`n") {
    if ($line -match '^data=no')  { Fail "/data is not mounted. This is not a Pi-Appliance-Kit image, or first boot has not finished." }
    if ($line -match '^data=yes') { Ok "/data mounted" }
    # Both apps render through DRM/KMS — no X on either device — so a missing
    # /dev/dri means neither app can put anything on the panel.
    if ($line -match '^dri=no')   { Warn "no /dev/dri/card* — vc4-kms-v3d is not loaded. The app will start but show nothing. Check config.txt." }
    if ($line -match '^dri=yes')  { Ok "DRM device present" }
    if ($line -match '^spi=no')   { Warn "no /dev/spidev0.0 — CAN will fail to initialise. Add 'dtoverlay=spi0-0cs' to config.txt." }
    if ($line -match '^spi=yes')  { Ok "SPI present" }
    if ($line -match '^rootfree=(\d+)') {
        $mb = [int]$Matches[1]
        if ($mb -lt 200) { Warn "only ${mb} MB free on / — apt installs may fail. Headroom is granted on first boot only; a reflash is the fix." }
        else { Ok "${mb} MB free on /" }
    }
    if ($line -match '^datafree=(\d+)') { Ok "$([int]$Matches[1]) MB free on /data" }
}

# ══ Device prep (needs the overlay down) ══════════════════════════════════════

if (-not $SkipPrep) {
    if (-not $sudoOk -and -not $DryRun) {
        Fail "Device prep needs sudo. See the note above, or pass -SkipPrep to deploy only."
    }

    Step "Device prep (read-only overlay comes down for this)"
    Set-Overlay -State down

    # --- identity -------------------------------------------------------------
    if ($Hostname) {
        Step "Setting hostname to $Hostname"
        # hostnamectl does NOT touch /etc/hosts, and without a matching entry
        # every sudo call prints "unable to resolve host" and pauses on a DNS
        # timeout first. Fix both together or the device is annoying forever.
        Invoke-Pi @"
sudo hostnamectl set-hostname '$Hostname'
if grep -q '^127\.0\.1\.1' /etc/hosts; then
    sudo sed -i 's/^127\.0\.1\.1.*/127.0.1.1\t$Hostname/' /etc/hosts
else
    printf '127.0.1.1\t%s\n' '$Hostname' | sudo tee -a /etc/hosts >/dev/null
fi
"@ | Out-Null
        Ok "hostname set (and /etc/hosts updated)"
        Warn "the device is now $Hostname.local — pass -TargetHost $User@$Hostname.local on the next run"
    }

    # --- GUI-on-DRM packages --------------------------------------------------
    Step "Checking GUI-on-DRM packages"
    $pkgs = @("libgl1-mesa-dri", "libegl1", "libgles2", "libinput10", "libfontconfig1")
    $missing = (Invoke-Pi ("for p in $($pkgs -join ' '); do dpkg -s `$p >/dev/null 2>&1 || echo `$p; done")).Trim()
    if ($missing) {
        $list = ($missing -split "`n") -join ' '
        Info "installing: $list"
        Invoke-Pi "sudo apt-get update -qq && sudo apt-get install -y --no-install-recommends $list" | Out-Null
        Ok "installed"
    }
    else { Ok "all present" }

    # --- USB update support ---------------------------------------------------
    # Not optional. UsbUpdateSource.HasRemovableDrive() tests /proc/mounts for
    # /media/usb and bails before scanning anything else; this image has no
    # udisks2 and no desktop session, so without this a stick does nothing.
    Step "Installing USB update support"
    Copy-ToPi @(
        "$($App.Pi)\neomotive-usb-mount.sh",
        "$($App.Pi)\neomotive-usb-mount@.service",
        "$($App.Pi)\setup-usb-updates.sh"
    ) "/tmp/"
    Invoke-Pi "chmod +x /tmp/setup-usb-updates.sh && sudo /tmp/setup-usb-updates.sh" | Out-Null
    Ok "USB auto-mount installed"

    Step "Restoring the read-only overlay"
    Set-Overlay -State up
}
else {
    Info "-SkipPrep: leaving device prep alone"
}

# ══ Deploy ════════════════════════════════════════════════════════════════════

if (-not $SkipDeploy) {
    Step "Building the $Target payload"
    if ($DryRun) {
        Write-Host "    [dry] & $($App.Publish)" -ForegroundColor DarkGray
    }
    else {
        & $App.Publish
        if ($LASTEXITCODE -ne 0) { Fail "Publish failed" }
    }

    $outDir = "$Dotnet\publish\$Target-pi"
    $binary = "$outDir\app-current\$($App.Binary)"
    if (-not $DryRun -and -not (Test-Path $binary)) { Fail "Expected $binary after publish" }

    Step "Installing to $RemoteDir"
    $tarball = Join-Path $env:TEMP "$Target-commission-$(Get-Date -Format yyyyMMdd-HHmmss).tgz"
    if (-not $DryRun) {
        tar -czf $tarball -C $outDir .
        if ($LASTEXITCODE -ne 0) { Fail "tar failed" }
        Info "$([math]::Round((Get-Item $tarball).Length / 1MB, 1)) MB"
    }

    try {
        Copy-ToPi @($tarball) "/tmp/neomotive-deploy.tgz"

        # STOP, do not restart. app.service is Restart=always, so a crash-looping
        # app is re-exec'd every 2 s; unpacking ~150 MB onto SD takes longer than
        # that, and overwriting a binary that is concurrently being executed
        # races with ETXTBSY and leaves it truncated.
        $expected = if ($DryRun) { 0 } else { (Get-Item $binary).Length }
        Invoke-Pi @"
set -e
sudo systemctl stop app.service
mkdir -p $RemoteDir
tar xzf /tmp/neomotive-deploy.tgz -C $RemoteDir
rm -f /tmp/neomotive-deploy.tgz
chmod +x $RemoteDir/run
if [ ! -f $RemoteDir/neomotive.config.json ]; then
    cp $RemoteDir/neomotive.config.json.default $RemoteDir/neomotive.config.json
fi
actual=`$(stat -c%s $RemoteDir/app-current/$($App.Binary))
if [ "`$actual" != "$expected" ]; then echo "TRUNCATED: `$actual != $expected"; exit 1; fi
rm -f $RemoteDir/$($App.Binary)
"@ | Out-Null
        Ok "extracted and verified"
    }
    finally {
        if (-not $DryRun) { Remove-Item $tarball -Force -ErrorAction SilentlyContinue }
    }

    if ($UpdateServerUrl) {
        Step "Setting the update server URL"
        # Written as JSON via a heredoc rather than sed: the file is small and a
        # botched in-place edit would leave the device unable to parse its config.
        $json = @{ updateServerUrl = $UpdateServerUrl } | ConvertTo-Json -Compress
        Invoke-Pi "cat > $RemoteDir/neomotive.config.json <<'NEOJSON'`n$json`nNEOJSON" | Out-Null
        Ok "updateServerUrl set"
    }

    Step "Starting app.service"
    Invoke-Pi "sudo systemctl start app.service" | Out-Null
    Start-Sleep -Seconds 12
}

# ══ Verify ════════════════════════════════════════════════════════════════════

Step "Verifying"

$state = Invoke-Pi @"
echo "active=`$(systemctl is-active app.service)"
echo "restarts=`$(systemctl show app.service -p NRestarts --value)"
echo "runexec=`$( [ -x $RemoteDir/run ] && echo yes || echo no )"
echo "usbunit=`$( [ -f /etc/systemd/system/neomotive-usb-mount@.service ] && echo yes || echo no )"
echo "usbrule=`$( [ -f /etc/udev/rules.d/99-neomotive-usb.rules ] && echo yes || echo no )"
echo "overlay=`$(findmnt -no FSTYPE /)"
echo "xserver=`$(pgrep -c Xorg || true)"
"@

$active = $false
# PowerShell's switch -regex runs EVERY matching clause, so each one breaks:
# without it "active=active" also fell through to the catch-all and reported the
# healthy case as a warning.
foreach ($line in $state -split "`n") {
    switch -regex ($line.Trim()) {
        '^active=active'   { Ok "app.service is active"; $active = $true; break }
        '^active=(.+)'     { Warn "app.service is $($Matches[1])"; break }
        '^restarts=(\d+)'  {
            $n = [int]$Matches[1]
            if ($n -gt 2) { Warn "$n restarts - the app is crash-looping, not running" }
            else { Ok "$n restarts" }
            break
        }
        '^runexec=yes'     { Ok "run is executable"; break }
        '^runexec=no'      { Warn "$RemoteDir/run is not executable - app-launch will silently do nothing"; break }
        '^usbunit=yes'     { Ok "USB mount unit installed"; break }
        '^usbunit=no'      { Warn "USB mount unit missing - USB updates will not work"; break }
        '^usbrule=yes'     { Ok "USB udev rule installed"; break }
        '^usbrule=no'      { Warn "USB udev rule missing - USB updates will not work"; break }
        '^overlay=overlay' { Ok "read-only overlay is up"; break }
        '^overlay=(.+)'    { Warn "rootfs is $($Matches[1]), not an overlay - changes to / will persist AND wear the card"; break }
        # Neither app uses X. A stray Xorg would be a leftover from the old
        # simulator deployment and will fight the app for DRM master.
        '^xserver=[1-9]'   { Warn "an X server is running - it will hold DRM master and the app will show nothing"; break }
    }
}

if ($active) {
    $log = Invoke-Pi "journalctl -u app.service --no-pager -n 40" -AllowFail
    foreach ($pattern in @(
        @{ Rx = 'drmModeSetCrtc failed';        Msg = "DRM master is held by another process. Find it: ps -ef | grep $($App.Binary)" }
        @{ Rx = 'Could not load file or assembly'; Msg = "Assembly load failure — the package was built against source Meadow. Rebuild." }
        @{ Rx = 'bad interpreter';               Msg = "run has CRLF line endings" }
        @{ Rx = 'Permission denied';             Msg = "Something tried to write outside /data (app.service is ProtectSystem=strict)" }
    )) {
        if ($log -match $pattern.Rx) { Warn $pattern.Msg }
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
if ($script:Warnings.Count -eq 0) {
    Write-Host "=== Commissioned ===" -ForegroundColor Green
}
else {
    Write-Host "=== Commissioned with $($script:Warnings.Count) warning(s) ===" -ForegroundColor Yellow
    $script:Warnings | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "Nobody has looked at the panel. 'Running' here means the service is up and the" -ForegroundColor Gray
Write-Host "log is clean — confirm the UI is actually on screen." -ForegroundColor Gray
Write-Host ""
Write-Host "  ssh $TargetHost 'journalctl -u app.service -f'"
Write-Host "  ssh $TargetHost 'findmnt /media/usb'          # with a stick inserted"
Write-Host ""
Write-Host "Reboot once and confirm it comes back on its own — that is the real acceptance test."
