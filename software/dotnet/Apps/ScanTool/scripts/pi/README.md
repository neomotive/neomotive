# ScanTool on Raspberry Pi (Pi Appliance Kit)

Runs `Neomotive.ScanTool.RaspberryPi` on a Pi 4 flashed with
[Pi-Appliance-Kit](../../../../../../ctacke/Pi-Appliance-Kit), driving an 800×480
panel and a Waveshare dual-MCP2515 CAN HAT.

## How it differs from the Desktop build

| | Desktop | Raspberry Pi |
|---|---|---|
| Meadow platform | `Meadow.Windows` | `Meadow.RaspberryPi` |
| CAN | PCAN USB | `WaveshareDualCanHat` → CAN0 (MCP2515 over SPI0) |
| Rendering | Win32 window | **Avalonia DRM/KMS** — no X server |
| Lifetime | `IClassicDesktopStyleApplicationLifetime` (MainWindow) | `ISingleViewApplicationLifetime` (MainView) |
| Updates | `UpdateService` A/B slots + USB watcher | rsync a new payload to `/data/app` |

The UI itself is unchanged: both hosts render the same `ScanToolView` from
`Neomotive.ScanTool.UIShared`, authored at 800×480 and wrapped in a `Viewbox`.

## Why DRM and not X11

The appliance image is Raspberry Pi OS Lite with a read-only overlay rootfs and
no display stack. Avalonia's `StartLinuxDrm` renders straight to `/dev/dri/card*`,
so there is no X server, no window manager, and no autologin/`xinit` chain to
babysit — `app.service` starts the binary and that is the whole graphics path.

## Fresh device bring-up (SD card in a Windows machine)

### 1. Flash the appliance image

Download the latest `*.img.xz` + `.sha256` from
[Pi-Appliance-Kit releases](https://github.com/ctacke/Pi-Appliance-Kit/releases),
verify, and flash with **Raspberry Pi Imager → Use custom**.

```powershell
gh release download -R ctacke/Pi-Appliance-Kit -p "*.img.xz" -p "*.sha256" -D $env:USERPROFILE\Downloads
```

> Imager's "Customisation" step is **skipped for custom images** — no WiFi, user,
> or hostname prompts. The image ships a baked identity: `pi` / `pi123!`,
> hostname `pi-appliance`.

### 2. Edit the boot partition before first boot

After flashing, Windows shows the FAT `bootfs` partition. Open `config.txt` and
append (checking first — stock `config.txt` may already load `vc4-kms-v3d`):

```ini
[all]
dtoverlay=spi0-0cs
dtoverlay=vc4-kms-v3d
```

`dtparam=spi=on` is already in the image. For WiFi, also drop a `wifi.conf` on
the same partition — first boot consumes and deletes it:

```ini
SSID=MySSID
PSK=MyPassphrase
COUNTRY=US
```

Eject, boot the Pi, and give it ~40 s (first boot creates the `/data` partition).

### 3. Install the GUI-on-DRM packages

Released images built before these packages were added to `optimizations.yaml`
need them installed by hand. `apt` is blocked by the read-only root, so lift the
overlay, install, and restore:

```bash
ssh pi@pi-appliance.local                      # password: pi123!

sudo raspi-config nonint disable_overlayfs && sudo reboot
# reconnect — rootfs is now writable
sudo apt-get update
sudo apt-get install -y libgl1-mesa-dri libegl1 libgles2 libinput10 libfontconfig1
sudo raspi-config nonint enable_overlayfs && sudo reboot
```

For repeat devices, tag a new kit release instead — `optimizations.yaml` already
carries the packages and overlays, so a rebuilt image ships ready.

### 4. Verify the device is ready

```bash
ls /dev/dri/card*     # DRM — else vc4-kms-v3d missing
ls /dev/spidev0.0     # SPI, userspace CS — else spi0-0cs missing
ls -d /data           # writable partition mounted
```

### 5. Deploy

```powershell
.\Apps\ScanTool\scripts\publish-scantool-pi.ps1 -Deploy
ssh pi@pi-appliance.local 'journalctl -u app.service -f'
```

Two password prompts (`pi123!`) — one for the `scp`, one for the `ssh` that
extracts and restarts the service.

---

## One-time device prep (existing device)

The kit needs two additions before ScanTool will run (both already committed to
`Pi-Appliance-Kit/config/optimizations.yaml`):

- **Packages**: `libgl1-mesa-dri`, `libegl1`, `libgles2`, `libinput10`, `libfontconfig1`
- **Overlays**: `dtoverlay=spi0-0cs` (userspace CS for the MCP2515) and
  `dtoverlay=vc4-kms-v3d` (creates `/dev/dri/card*`)

Apply to a live Pi and reboot:

```bash
scp -r F:/repos/ctacke/Pi-Appliance-Kit pi@pi-appliance.local:/tmp/kit
ssh pi@pi-appliance.local 'sudo /tmp/kit/scripts/apply.sh && sudo reboot'
```

Or rebuild the image (Track A) for a production unit.

Verify afterwards:

```bash
ls /dev/dri/card*      # DRM device present
ls /dev/spidev0.0      # SPI present, no kernel CS
ls -d /data            # writable partition mounted
```

> If boot logs complain about a duplicate `vc4-kms-v3d` overlay, the stock
> `config.txt` already loads it — remove ours from `hardware_overlays`.

## Build & deploy

```powershell
# from F:\repos\neomotive\software\dotnet
.\Apps\ScanTool\scripts\publish-scantool-pi.ps1                 # build payload only
.\Apps\ScanTool\scripts\publish-scantool-pi.ps1 -Deploy         # build + install
```

The payload lands in `publish\scantool-pi\` and contains:

- `scantool` — self-contained single-file linux-arm64 binary
- `run` — the appliance entrypoint `app-launch` execs
- `neomotive.config.json`

`-Deploy` packs the payload into a single `.tar.gz`, `scp`s it to `/tmp`, then
extracts it into `/data/app`, sets the exec bit on `run`, and restarts
`app.service`. Two password prompts (one per connection); pass
`-TargetHost pi@192.168.4.41` to bypass mDNS.

It uses **native Windows OpenSSH** (`scp`/`ssh` in `C:\Windows\System32\OpenSSH`)
and the built-in `tar.exe` — no git-bash, no WSL, no rsync.

### Why not the kit's `install-app.sh`

Two reasons it cannot work from a Windows workstation:

1. **It needs `rsync`**, which Git for Windows does not ship.
2. **It copies with `--rsync-path="sudo rsync"`**, but the appliance image has no
   `010_pi-nopasswd` sudoers rule, so sudo demands a password it cannot prompt
   for over a non-tty rsync channel. `/data/app` is owned by `pi` anyway, so the
   copy never needed sudo — only the service restart does.

### Exec bits (a Windows gotcha)

NTFS carries no POSIX mode bits, and git-bash's `chmod` is a silent no-op on an
ELF file (it infers the bit from content: `#!` reads as 755, ELF as 644). So
neither `run` nor `scantool` can be made executable on the Windows side.

Both are therefore fixed **on the device**: the deploy step chmods `run`, and
`run` chmods `scantool` before exec'ing it. If you ever copy the payload by hand,
`chmod +x /data/app/run` is the one step you must not skip — `app-launch` looks
for an executable `run` and silently does nothing without it.

## Runtime environment

`app.service` runs as root with `ProtectSystem=strict`, `ReadWritePaths=/data`,
`ProtectHome=yes`. `/data` is the only writable mount, so `run` redirects
everything .NET and Mesa want to write into the app directory:

- `HOME=/data/app` (Mesa shader cache, .NET probing)
- `DOTNET_BUNDLE_EXTRACT_BASE_DIR=/data/app/.dotnet-extract` (single-file extraction)
- `XDG_RUNTIME_DIR=/data/app/.runtime`
- `DOTNET_EnableWriteXorExecute=0` (standard Pi/arm64 workaround)

Overrides, if the panel is not on the default connector:

```sh
export SCANTOOL_DRM_CARD=/dev/dri/card1
export SCANTOOL_DRM_SCALING=1.0
```

## Logs

journald is **volatile** (RAM) on this image, so the app also ships logs off-box
via Meadow's `UdpLogger`. For a live view:

```bash
ssh pi@pi-appliance.local 'journalctl -u app.service -f'
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Blank screen, service restarting | No `/dev/dri/card*` — `vc4-kms-v3d` overlay missing |
| `Failed to initialize CAN0 ... CS is probably in use` | `dtoverlay=spi0-0cs` missing |
| UI renders, Connect finds nothing | CAN HAT wiring/termination, or wrong bitrate (expects 500 kbps) |
| App runs but no touch input | `libinput10` missing |
| `bad interpreter` on `run` | CRLF line endings — the publish script normalizes to LF |
| Offline-mode UI with `NullCanBus` | HAT init threw; check `journalctl` for the Meadow error |
