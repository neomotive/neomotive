# Commissioning a Raspberry Pi

End-to-end bring-up of a Neomotive appliance, from a blank SD card to the app
running on boot. Covers both apps — the **ScanTool** and the **ModuleSimulator**.

Steps marked **ScanTool** or **Simulator** apply to only that app; everything
else applies to both. Budget about 45 minutes, most of it waiting on reboots.

---

## 0. What you need

| | |
|---|---|
| Board | Raspberry Pi 4 (Pi 3B+ and Zero 2 W also work, arm64) |
| Storage | 8 GB+ microSD |
| Display | 800×480 panel on HDMI (both UIs are authored at 800×480 in a `Viewbox`) |
| CAN | Waveshare dual-MCP2515 CAN HAT |
| Inputs (Simulator) | MCP3004 ADC input board on SPI |
| Network | Ethernet for bring-up. WiFi works but is one more variable |
| Workstation | Windows with the .NET 10 SDK and the OpenSSH client (`scp`/`ssh` in `C:\Windows\System32\OpenSSH`) |

The two apps differ in exactly one meaningful way at commissioning time — the
graphics path:

| | ScanTool | ModuleSimulator |
|---|---|---|
| Rendering | Avalonia DRM/KMS, straight to `/dev/dri/card*` | Avalonia X11 |
| Display stack needed | none | `xserver-xorg-core`, `xinit`, no window manager |
| Device prep script | `setup-usb-updates.sh` | `setup-appliance.sh` |
| Default hostname | `pi-appliance` | rename to `neomotive-sim` (§4) |

Everything else — image, `/data/app` layout, deploy, updates — is identical.

---

## 1. Flash the appliance image

The OS is **not** stock Raspberry Pi OS. It is
[Pi-Appliance-Kit](https://github.com/ctacke/Pi-Appliance-Kit): Raspberry Pi OS
Lite with a **read-only overlay rootfs** and a **writable `/data` partition**,
booting in ~12 s instead of ~32 s, and running exactly one app.

Download the latest image and verify it against its `.sha256`:

```powershell
gh release download -R ctacke/Pi-Appliance-Kit -p "*.img.xz" -p "*.sha256" `
    -D $env:USERPROFILE\Downloads
```

Flash with **Raspberry Pi Imager → Use custom** → select the `.img.xz`.

> **Imager's "Customisation" step is skipped for custom images.** It only offers
> the WiFi / user / hostname / locale prompts for its own catalog images — none
> of those settings are applied here. The image ships a baked identity instead:
> user **`pi`**, password **`pi123!`**, hostname **`pi-appliance`**, region US.
> Change them in §4.

---

## 2. Edit the boot partition before first boot

After flashing, Windows mounts the FAT `bootfs` partition. Open `config.txt` and
append:

```ini
[all]
dtoverlay=spi0-0cs
```

That enables SPI0 with **no kernel-managed chip-select lines**. The Meadow
MCP2515 driver toggles CS from userspace; without it, CAN init fails because the
kernel already holds the CS line. `dtparam=spi=on` is already in the image.

`vc4-kms-v3d` — which creates `/dev/dri/card*` — is normally already loaded by
the stock `config.txt` under `[all]`. Check before adding it, since loading the
same overlay twice makes the firmware complain:

```
findstr vc4 config.txt
```

**WiFi (optional).** Drop a `wifi.conf` on the same partition. First boot
consumes it, connects, then deletes it so credentials don't linger on the
readable partition:

```ini
SSID=MySSID
PSK=MyPassphrase
COUNTRY=US
```

Eject, insert into the Pi, power on. **Give first boot ~40 seconds** — it creates
the `/data` partition and grows the root filesystem by 1 GB of headroom.

---

## 3. First contact

```bash
ssh pi@pi-appliance.local          # password: pi123!
```

If mDNS doesn't resolve, read the IP off the panel: with no app installed the kit
paints a console status page showing the hostname, every IP address, and the
exact `ssh` line to use. It refreshes every 5 s.

Sanity checks:

```bash
ls -d /data              # writable partition mounted
ls /dev/spidev0.0        # SPI present, no kernel CS
ls /dev/dri/card*        # DRM device present  (the ScanTool needs this)
df -h /                  # ~1 GB free on root, for the apt installs in section 4/5
```

---

## 4. Set the device identity

Optional, but do it before anything else — it shares a reboot with §5.

The root **and** boot partitions are read-only, so `passwd` and `hostnamectl`
land in a RAM overlay and are **discarded on reboot**. To make them stick, lift
the overlay, change things, put it back:

```bash
sudo raspi-config nonint disable_overlayfs && sudo reboot
# reconnect — the rootfs is now writable
sudo passwd pi
sudo hostnamectl set-hostname neomotive-sim     # Simulator; ScanTool keeps pi-appliance
sudo raspi-config nonint enable_overlayfs && sudo reboot
```

If you are also doing §5, do both in the same overlay-lifted window and save two
reboots. For fleets, bake the values into the image build instead of repeating
this per device.

---

## 5. One-time device prep

Both apps need something installed on the read-only rootfs, so both go through
the same lift → install → restore cycle. The scripts refuse to run while the
overlay is on, so a forgotten step fails loudly instead of silently installing
into RAM and evaporating at the next reboot.

### ScanTool

USB update support only. Network updates need none of this.

```powershell
scp Apps/ScanTool/scripts/pi/neomotive-usb-mount.sh `
    Apps/ScanTool/scripts/pi/setup-usb-updates.sh pi@pi-appliance.local:/tmp/
```

```bash
ssh -t pi@pi-appliance.local
sudo raspi-config nonint disable_overlayfs && sudo reboot
# reconnect
sudo chmod +x /tmp/setup-usb-updates.sh && sudo /tmp/setup-usb-updates.sh
sudo raspi-config nonint enable_overlayfs && sudo reboot
```

If the image predates the GUI-on-DRM packages in the kit's
`optimizations.yaml`, install them in the same window:

```bash
sudo apt-get update
sudo apt-get install -y libgl1-mesa-dri libegl1 libgles2 libinput10 libfontconfig1
```

### Simulator

X server, a systemd drop-in, and USB update support — one script does all three.

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

**Why the drop-in matters.** `app.service` runs with `ProtectSystem=strict`,
which mounts everything read-only except `/data` — `/tmp` included. The X server
must create `/tmp/.X11-unix/X0` or it exits before the display ever opens, and
the symptom is a black screen with a service restarting every 2 seconds. The
drop-in sets `PrivateTmp=yes`, giving the unit a private writable tmpfs `/tmp`.
The app's own temp files still land on `/data` — `run` exports
`TMPDIR=$APP_DIR/.tmp` — so a 150 MB update download never goes to RAM.

Verify after the final reboot:

```bash
systemctl cat app.service | grep PrivateTmp    # PrivateTmp=yes
which Xorg xinit
```

---

## 6. Deploy the app

Your app is a directory at **`/data/app`** containing an executable **`run`**.
`app.service` ships pre-enabled and simply launches it — you never edit the unit
or run `systemctl enable`.

```powershell
# from F:\repos\neomotive\software\dotnet
.\Apps\ScanTool\scripts\publish-scantool-pi.ps1 -Deploy
# or
.\Apps\ModuleSimulator\scripts\publish-simulator-pi.ps1 -Deploy
```

Each script publishes a self-contained single-file `linux-arm64` binary, tars the
payload, copies it over with `scp`, **stops** `app.service`, extracts into
`/data/app`, sets the exec bits, verifies the binary is not truncated, and starts
the service. Two password prompts. Add `-TargetHost pi@192.168.4.41` to bypass
mDNS.

The service is stopped rather than restarted afterwards for a reason:
`Restart=always` re-launches a crash-looping app every 2 s, and unpacking a
~150 MB binary onto SD takes longer than that. Overwriting an image that is
concurrently being executed races with `ETXTBSY` and yields a truncated binary.

The resulting layout:

```
/data/app/
├─ run                     # entrypoint — must be executable
├─ xinitrc, xorg.conf      # Simulator only
├─ neomotive.config.json   # device-local; holds updateServerUrl
├─ local.env               # device-local overrides (§9) — not in the payload
├─ app-current/            # active A/B slot: the binary + launcher/
├─ app-previous/           # rollback slot, filled on the first update
├─ app-downloads/          # update zips land here, never in /tmp
├─ config/                 # UDS catalog overlays
└─ data/                   # user settings — survives every update
```

`config/` and `data/` sit **beside** the slots, not inside them, so a slot swap
never touches user data.

### Why not the kit's `install-app.sh`

It needs `rsync`, which Git for Windows does not ship, and it copies with
`--rsync-path="sudo rsync"` against an image that has no NOPASSWD sudoers rule —
sudo cannot prompt over a non-tty rsync channel. `/data/app` is owned by `pi`
anyway; only the service restart needs root, and that gets a tty from `ssh -t`.

### The one step you must not skip by hand

NTFS carries no POSIX mode bits, and git-bash's `chmod` is a silent no-op on an
ELF file. If you ever copy a payload manually:

```bash
chmod +x /data/app/run
```

`app-launch` looks for an **executable** `run` and silently does nothing without
it — no error, no log line, just a device that never starts.

---

## 7. Verify

```bash
ssh pi@<host> 'systemctl status app.service'
ssh pi@<host> 'journalctl -u app.service -f'
```

The panel should show the app within a few seconds of the service starting. Then
check in the UI: **Settings → Updates** shows the running version.

journald is volatile on this image — nothing survives a reboot. For the
simulator, X server failures are in `/data/app/.tmp/Xorg.0.log`.

Then reboot once and confirm it comes back on its own. That is the real
acceptance test: `app.service` is `Restart=always`, so a device that only works
after a manual start is a device that will fail in the field.

---

## 8. Point it at updates

Set the update manifest URL once per device. It lives in the device-local config,
which deploys never overwrite:

```bash
ssh pi@<host> 'cat > /data/app/neomotive.config.json' <<'JSON'
{
  "updateServerUrl": "https://github.com/ctacke/neomotive/releases/download/updates-latest/version-manifest.json"
}
JSON
ssh -t pi@<host> 'sudo systemctl restart app.service'
```

From then on the device self-updates from the **Updates** screen: it downloads to
`app-downloads/`, extracts to `app-staging/`, verifies every file's SHA-256,
swaps `app-current` → `app-previous` and `app-staging` → `app-current`, then
exits 42 to ask the launcher for a relaunch. If the new version won't start, the
previous slot is still there.

See [updates/release-and-update.md](updates/release-and-update.md) for how
packages are built and released.

---

## 9. Device-local settings

Do **not** edit `run` to configure a device — `run` is part of the payload and
every deploy overwrites it, so edits there are silently reverted. Put overrides
in `/data/app/local.env`, which is not in the payload and survives upgrades:

```bash
ssh pi@<host> 'cat >> /data/app/local.env' <<'ENV'
export SCANTOOL_CAN_CHANNEL=1
ENV
ssh -t pi@<host> 'sudo systemctl restart app.service'
```

Recognised by the ScanTool:

| Variable | Meaning |
|---|---|
| `SCANTOOL_CAN_CHANNEL` | CAN HAT channel: `0` (default) or `1` |
| `SCANTOOL_DRM_CARD` | e.g. `/dev/dri/card1` if the panel is not on the default |
| `SCANTOOL_DRM_SCALING` | e.g. `1.0` |
| `SCANTOOL_UDP_LOG` | `1` to also ship Meadow logs off-box over UDP |

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Service does nothing, no log lines | `/data/app/run` is not executable |
| `bad interpreter: /bin/sh^M` | `run` was copied with CRLF endings |
| Restarting every 2 s, no UI | App crashes on start — `journalctl -u app.service -n 50` |
| `Could not load file or assembly 'Meadow.Contracts'` | Package built against source-built Meadow. Rebuild; `create-update-package.ps1` now fails the build rather than shipping it |
| `drmModeSetCrtc failed` (ScanTool) | Something else holds DRM master — usually a hand-started binary left over from an SSH session. Find it with `ps -ef \| grep scantool` and kill it |
| Black screen, X exits at once (Simulator) | `PrivateTmp` drop-in missing, or installed with the overlay on. Read `/data/app/.tmp/Xorg.0.log` |
| Changes vanish after reboot | They were written to the read-only rootfs with the overlay on. Only `/data` persists |
| USB stick does nothing | Device prep not run, or run with the overlay on |
| CAN init fails, CS in use | `dtoverlay=spi0-0cs` missing from `config.txt` |
| `apt` fails, read-only filesystem | Lift the overlay first (§4) |
| Out of space on `/` during apt | The 1 GB headroom is granted on first boot only; an already-flashed device cannot gain it retroactively. Reflash |

## Reference

- [Pi-Appliance-Kit README](https://github.com/ctacke/Pi-Appliance-Kit) — image internals, WiFi, boot benchmarks
- [ScanTool Pi notes](../Apps/ScanTool/scripts/pi/README.md)
- [Simulator Pi notes](../Apps/ModuleSimulator/scripts/pi/README.md)
- [Release & update mechanism](updates/release-and-update.md)
