# ModuleSimulator on Raspberry Pi (Pi Appliance Kit)

Runs `Neomotive.ModuleSimulator.RaspberryPi` on a Pi flashed with
[Pi-Appliance-Kit](../../../../../../ctacke/Pi-Appliance-Kit), driving an 800×480
panel, a Waveshare dual-MCP2515 CAN HAT and an MCP3004 ADC input board.

For end-to-end bring-up from a blank SD card, see
[docs/commissioning-a-pi.md](../../../docs/commissioning-a-pi.md). This file is
the simulator-specific detail.

## How it differs from the ScanTool

Both apps deploy to `/data/app` and use the same A/B update mechanism. The one
real difference is the graphics path:

| | ScanTool | ModuleSimulator |
|---|---|---|
| Rendering | Avalonia **DRM/KMS** — straight to `/dev/dri/card*` | Avalonia **X11** |
| Display stack | none | `xserver-xorg-core` + `xinit`, no window manager |
| Entrypoint | `run` → binary | `run` → `xinit` → `xinitrc` → binary |
| Supervise loop | in `run` | in `xinitrc` (so exit 42 does not tear down X) |
| Extra device prep | `setup-usb-updates.sh` | `setup-appliance.sh` |

The simulator uses a windowed Avalonia lifetime, so it needs an X server. The
appliance image is Raspberry Pi OS Lite with no display stack, which is what
`setup-appliance.sh` fixes.

## Two things `ProtectSystem=strict` breaks, and how they are fixed

`app.service` mounts the entire hierarchy read-only except `/data`. For a DRM app
that is invisible; for X it is fatal twice over.

1. **`/tmp` is read-only**, so the X server cannot create its socket at
   `/tmp/.X11-unix/X0` and exits before the display ever opens — a black screen
   with nothing useful in the journal. Fixed by the drop-in
   `setup-appliance.sh` installs at
   `/etc/systemd/system/app.service.d/10-simulator-x11.conf`, which sets
   `PrivateTmp=yes`. That gives the unit a private writable tmpfs `/tmp` without
   making the real one writable.

   The app's own temp files are *not* affected: `run` exports
   `TMPDIR=$APP_DIR/.tmp`, so a ~150 MB update download lands on `/data` rather
   than in RAM.

2. **`/var/log` is read-only**, which is where Xorg writes by default. `run`
   passes `-logfile "$TMPDIR/Xorg.0.log"`, so the log is on `/data` and readable
   after a failure.

`xorg.conf` is the third case, handled differently: rather than install it onto
the read-only rootfs at `/etc/X11`, it ships in the payload and `run` passes it
with `-config`. Xorg only honours an absolute `-config` path when running as
root, which it does here — `app.service` sets no `User=`. It updates with the app.

## One-time device prep

Run once per device, with the overlay lifted:

```powershell
scp Apps/ModuleSimulator/scripts/pi/neomotive-usb-mount.sh `
    Apps/ModuleSimulator/scripts/pi/setup-appliance.sh pi@neomotive-sim.local:/tmp/
```

```bash
ssh -t pi@neomotive-sim.local
sudo raspi-config nonint disable_overlayfs && sudo reboot
# reconnect
sudo chmod +x /tmp/setup-appliance.sh && sudo /tmp/setup-appliance.sh
sudo raspi-config nonint enable_overlayfs && sudo reboot
```

The script refuses to run while the overlay is on, so a forgotten step fails
loudly instead of silently installing into RAM and evaporating at the next
reboot. It installs the X packages, the `PrivateTmp` drop-in, and the USB
auto-mount rule the update scanner needs.

## Build & deploy

```powershell
# from F:\repos\neomotive\software\dotnet
.\Apps\ModuleSimulator\scripts\publish-simulator-pi.ps1                 # build payload only
.\Apps\ModuleSimulator\scripts\publish-simulator-pi.ps1 -Deploy         # build + install
```

The payload lands in `publish\simulator-pi\`:

```
publish/simulator-pi/
├─ run                            # appliance entrypoint app-launch execs
├─ xinitrc                        # X session + supervise loop
├─ xorg.conf                      # modesetting stanza, passed with -config
├─ neomotive.config.json.default  # seed for the device-local config
└─ app-current/
   ├─ simulator                   # self-contained single-file linux-arm64 binary
   └─ launcher/{run,xinitrc,xorg.conf}   # OTA-updatable copies (see below)
```

`-Deploy` tars the payload, `scp`s it to `/tmp`, stops `app.service`, extracts
into `/data/app`, sets the exec bits, verifies the binary is not truncated, and
starts the service. It uses native Windows OpenSSH and the built-in `tar.exe` —
no git-bash, no WSL, no rsync. Pass `-TargetHost pi@192.168.4.31` to bypass mDNS.

The kit's own `install-app.sh` does not work from Windows: it needs `rsync`
(Git for Windows ships none) and copies with `--rsync-path="sudo rsync"` against
an image that has no NOPASSWD sudoers rule.

## Why the launcher is bundled twice

`$APP_DIR/run` sits **outside** the A/B slots, and an update package only ever
replaces `app-current/`. Without a handoff, every launcher fix would need SSH on
every device forever. So `run` delegates to `app-current/launcher/run` when that
file exists and passes `sh -n`, and falls through to its own logic otherwise —
a bad launcher on a read-only appliance is a brick with no console, so the guard
matters more than the feature.

## The classic Raspberry Pi OS path

`setup-autostart.sh`, `bash_profile`, `neomotive-splash.service` and
`deployment.md` describe the older non-appliance deployment: autologin on tty1,
`~/.bash_profile` calls `startx`, app at `/opt/neomotive`. Both paths end in the
same `xinitrc`, which resolves `NEOMOTIVE_APP_DIR` → `/data/app` → `/opt/neomotive`.
New devices should use the appliance kit.

## Logs

```bash
ssh pi@neomotive-sim.local 'journalctl -u app.service -f'
ssh pi@neomotive-sim.local 'cat /data/app/.tmp/Xorg.0.log'   # X server failures
```

journald is volatile on this image — nothing survives a reboot.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Black screen, `app.service` restarting every 2s | X failed to start — read `Xorg.0.log` |
| `Cannot establish any listening sockets` | `PrivateTmp` drop-in missing, or installed with the overlay on |
| `bad interpreter: /bin/sh^M` | `run`/`xinitrc` were copied with CRLF endings |
| Service does nothing at all | `/data/app/run` is not executable — `app-launch` requires the exec bit |
| App starts, no touch input | `xserver-xorg-input-libinput` missing |
