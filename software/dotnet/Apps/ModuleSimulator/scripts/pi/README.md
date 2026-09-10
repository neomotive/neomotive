# ModuleSimulator on Raspberry Pi (Pi Appliance Kit)

Runs `Neomotive.ModuleSimulator.RaspberryPi` on a Pi flashed with
[Pi-Appliance-Kit](../../../../../../ctacke/Pi-Appliance-Kit), driving an 800×480
panel, a Waveshare dual-MCP2515 CAN HAT and an MCP3004 ADC input board.

For end-to-end bring-up from a blank SD card, see
[docs/commissioning-a-pi.md](../../../docs/commissioning-a-pi.md). It covers both
appliances, because they are commissioned identically.

## Same as the ScanTool, deliberately

Both heads render with **Avalonia DRM/KMS** straight to `/dev/dri/card*`. There
is no X server, no window manager, no `xinit` chain, no `xinitrc`. `app.service`
starts `run`, `run` starts the binary, and that is the entire graphics path.

| | ScanTool | ModuleSimulator |
|---|---|---|
| Rendering | Avalonia DRM/KMS | Avalonia DRM/KMS |
| Entrypoint | `run` → binary | `run` → binary |
| Supervise loop (exit 42) | in `run` | in `run` |
| Device prep | `setup-usb-updates.sh` | `setup-usb-updates.sh` (identical file) |
| App dir | `/data/app` | `/data/app` |
| Env prefix | `SCANTOOL_*` | `SIMULATOR_*` |

`run` and `setup-usb-updates.sh` are near-identical between the two apps by
design. A fix to one almost always belongs in the other.

The X11 path is gone. `Program.cs` used to be `UseX11()` +
`StartWithClassicDesktopLifetime`, which cost an X server, an `xinit` chain and
an `xinitrc` supervise loop on an image that ships no display stack — plus a
`PrivateTmp` systemd drop-in on top, because `app.service` runs with
`ProtectSystem=strict` and X cannot create `/tmp/.X11-unix/X0` on a read-only
`/tmp`. All of that was in service of a `MainWindow` that only ever wrapped a
single `SimulatorView`. DRM needs none of it.

`App.OnFrameworkInitializationCompleted` now sets `MainView` on the single-view
lifetime (letterboxed in a `Viewbox`, as the ScanTool does), falling back to a
`Window` when the project is run on a dev box for layout checks.

## One-time device prep

USB update support. Every device gets this — the app's scanner only looks at
`/media/usb`, and this image has no udisks2 and no desktop session, so without it
inserting a stick does nothing at all. Network updates need none of it.

```powershell
scp Apps/ModuleSimulator/scripts/pi/neomotive-usb-mount.sh `
    Apps/ModuleSimulator/scripts/pi/neomotive-usb-mount@.service `
    Apps/ModuleSimulator/scripts/pi/setup-usb-updates.sh pi@neomotive-sim.local:/tmp/
```

```bash
ssh -t pi@neomotive-sim.local
sudo raspi-config nonint disable_overlayfs && sudo reboot
# reconnect
sudo chmod +x /tmp/setup-usb-updates.sh && sudo /tmp/setup-usb-updates.sh
sudo raspi-config nonint enable_overlayfs && sudo reboot
```

It goes on the read-only rootfs, so the overlay must be down. The script refuses
to run otherwise, rather than installing into RAM and evaporating at the next
reboot.

### Why a systemd unit and not a udev `RUN`

The rule does **not** mount. It pulls in `neomotive-usb-mount@%k.service` with
`ENV{SYSTEMD_WANTS}`, and systemd does the mount.

A udev worker runs in a private mount namespace. Mounting from `RUN+=` fails on
this image with a bare `mount: /media/usb: permission denied`, and on the systemd
versions where it succeeds the mount is invisible to every other process —
including the app. Both failure modes look identical from the app's side
(`/media/usb` never appears), which is why this looked like a dead udev rule.

The unit is `BindsTo=dev-%i.device`, so pulling the stick stops it and `ExecStop`
unmounts. No remove rule is needed.

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
├─ neomotive.config.json.default  # seed for the device-local config
└─ app-current/
   ├─ simulator                   # self-contained single-file linux-arm64 binary
   └─ launcher/run                # OTA-updatable copy (see below)
```

`-Deploy` tars the payload, `scp`s it to `/tmp`, stops `app.service`, extracts
into `/data/app`, sets the exec bit, verifies the binary is not truncated, and
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

## Device-local settings

`run` is part of the payload and every deploy overwrites it, so put overrides in
`/data/app/local.env`, which is not in the payload and survives upgrades:

| Variable | Meaning |
|---|---|
| `SIMULATOR_CAN_CHANNEL` | CAN HAT channel: `0` (default) or `1` |
| `SIMULATOR_DRM_CARD` | e.g. `/dev/dri/card1` if the panel is not on the default |
| `SIMULATOR_DRM_SCALING` | e.g. `1.0` |
| `SIMULATOR_UDP_LOG` | `1` to also ship Meadow logs off-box over UDP |

## Logs

```bash
ssh pi@neomotive-sim.local 'journalctl -u app.service -f'
```

journald is volatile on this image — nothing survives a reboot.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Service does nothing at all | `/data/app/run` is not executable — `app-launch` requires the exec bit |
| `bad interpreter: /bin/sh^M` | `run` was copied with CRLF endings |
| `drmModeSetCrtc failed` | Something else holds DRM master — usually a hand-started binary from an SSH session. Find it with `ps -ef \| grep simulator` and kill it |
| No `/dev/dri/card*` | `vc4-kms-v3d` not loaded — check `config.txt` |
| USB stick does nothing | `setup-usb-updates.sh` not run, or run with the overlay on. Check `journalctl -t neomotive-usb-mount` and `systemctl status 'neomotive-usb-mount@*'` |
