---
name: commission-pi
description: Commission, deploy to, or troubleshoot a Neomotive Raspberry Pi appliance (ScanTool or ModuleSimulator) running the Pi Appliance Kit image. Use when asked to set up a new Pi, deploy an app to a device, diagnose a device that will not start or shows a black screen, or fix USB updates.
---

# Commissioning a Neomotive Pi appliance

Both appliances — ScanTool and ModuleSimulator — are set up identically. Both
render with Avalonia DRM/KMS (no X server), live at `/data/app`, and share the
same `run` and `setup-usb-updates.sh`. They differ only in payload, hostname and
`SCANTOOL_*` vs `SIMULATOR_*` env prefixes.

Full reference: `software/dotnet/docs/commissioning-a-pi.md`.

## Prefer the script

`software/dotnet/scripts/commission-pi.ps1` automates everything after the SD
card is flashed and booted: preflight, overlay lift, hostname + `/etc/hosts`,
GUI-on-DRM packages, USB support, build, deploy, verify.

```powershell
cd software\dotnet
.\scripts\commission-pi.ps1 -Target simulator -Hostname neomotive-sim
.\scripts\commission-pi.ps1 -Target scantool -SkipPrep      # redeploy only
.\scripts\commission-pi.ps1 -Target scantool -DryRun        # show, change nothing
```

Run it rather than reproducing its steps by hand. If it fails, fix the cause and
re-run — it is idempotent. Report its warnings verbatim; do not paper over them.

**Always tell the user the script cannot see the panel.** "Service active, log
clean" is not "the UI is on screen". Ask them to confirm.

## What stays manual

Flashing the image and editing `config.txt` are physical steps. Walk the user
through §1–§2 of the commissioning doc; do not pretend to have done them.

`config.txt` needs `dtoverlay=spi0-0cs` (userspace CS for the MCP2515 — without
it CAN init fails). `vc4-kms-v3d` is usually already there; loading it twice
makes the firmware complain.

## Facts that decide most diagnoses

- **`/data` is the only writable filesystem.** The rootfs is a read-only
  overlay. Anything written to `/usr`, `/etc` or `/var` with the overlay up
  lives in RAM and is gone at the next reboot — which looks exactly like a
  successful install until the device is power-cycled. If something "worked
  yesterday and doesn't today", suspect this first.
- **`app.service` runs with `ProtectSystem=strict` and `ReadWritePaths=/data`,**
  so the whole hierarchy is read-only in its namespace, `/tmp` included. `run`
  redirects `TMPDIR`, `HOME`, `XDG_RUNTIME_DIR` and the bundle extract dir under
  `$APP_DIR` for exactly this reason.
- **`app-launch` requires an executable `run`.** Not executable → the service
  does nothing at all: no error, no log line. NTFS carries no mode bits and
  git-bash's `chmod` is a no-op on an ELF, so exec bits are always set on the
  device, never on the workstation.
- **`Restart=always`, `RestartSec=2`.** Never "restart" a service while
  extracting a payload — stop it, extract, start. Overwriting a binary that is
  concurrently being executed races with `ETXTBSY` and truncates it.
- **sudo may or may not need a password**; it varies by image build. Check with
  `sudo -n true`. Never pipe a password into `sudo -S`.
- **udev cannot mount.** udev workers have a private mount namespace: `mount`
  from `RUN+=` fails with `permission denied`, or succeeds invisibly. USB
  mounting goes through `ENV{SYSTEMD_WANTS}` → `neomotive-usb-mount@.service`.
- **`hostnamectl` does not update `/etc/hosts`.** Without a matching
  `127.0.1.1` entry every `sudo` prints `unable to resolve host` and stalls on a
  DNS timeout.

## Symptom → cause

| Symptom | Cause |
|---|---|
| Service active, panel black | Something else holds DRM master. `ps -ef \| grep -E 'scantool\|simulator'` — usually a binary hand-started from an SSH session. Kill it; `Restart=always` recovers |
| Service does nothing, no log lines | `/data/app/run` not executable |
| `bad interpreter: /bin/sh^M` | `run` copied with CRLF endings |
| Crash-loops every 2 s | Read the log. `Could not load file or assembly 'Meadow.Contracts'` means the package was built against source-built Meadow — rebuild; `create-update-package.ps1` fails the build for this now |
| USB stick does nothing | `journalctl -t neomotive-usb-mount` and `systemctl status 'neomotive-usb-mount@*'`. Empty → prep never ran, or ran with the overlay up |
| Changes vanish after reboot | Written to the read-only rootfs with the overlay up |
| `Permission denied` writing a path | Something escaped `/data`; `ProtectSystem=strict` |

## Deploying without the script

Only when the script cannot be used. Build with the app's
`publish-*-pi.ps1`, then: stop the service, `tar xzf` into `/data/app`,
`chmod +x /data/app/run`, verify the binary size matches, start the service.
Never `rsync --delete` — `/data/app/data` and `/data/app/config` hold user state
that lives beside the A/B slots.

## After device work

Per repo convention, update `Apps/ScanTool-Plan.md` and `Apps/ScanTool-Tasks.md`,
and state plainly what was verified on hardware versus reasoned about.
