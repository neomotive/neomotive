# Raspberry Pi Deployment

## Prerequisites

```bash
sudo apt update
sudo apt install xorg unclutter fbi feh
```

## Deploy the app

Include a `splash.png` (800×480) in the published output so the boot splash service has something to show. Copy the published build to the Pi:

```bash
scp -r publish/* pi@neomotive-sim:/opt/neomotive/
```

## Configure boot autostart

Copy this `scripts/pi/` folder to the Pi and run the setup script:

```bash
scp -r scripts/pi pi@neomotive-sim:~/pi-scripts
ssh pi@neomotive-sim
bash ~/pi-scripts/setup-autostart.sh
sudo reboot
```

`setup-autostart.sh` does the following:
- Installs a systemd autologin drop-in so `pi` logs in on tty1 at boot (no password prompt)
- Installs `~/.bash_profile` which calls `startx` when logged into tty1
- Installs `~/.xinitrc` which disables screensaver/blanking and launches the app
- Installs `/etc/X11/xorg.conf` with an explicit `modesetting` driver so X skips hardware probing
- Installs and enables `neomotive-splash.service` which shows `/opt/neomotive/splash.png` on the framebuffer before X starts (requires `splash.png` in the deployed output)

## Reduce boot splash time

Three visible phases add ~12 seconds of blank/rainbow/spew before the app appears. All can be eliminated.

Run `setup-autostart.sh` — it applies these automatically. What it does:

**`/boot/firmware/config.txt`** (path auto-detected for Bullseye vs Bookworm):
- `disable_splash=1` — removes the rainbow GPU test pattern
- `boot_delay=0` — removes a 1-second firmware delay added for display sync

**`/boot/firmware/cmdline.txt`**:
- Appends `quiet loglevel=3` — suppresses kernel boot messages ("startup spew")

> **Note:** If the display doesn't sync reliably after removing `boot_delay`, add `boot_delay=1` back.

## Window behavior

`SystemDecorations="None"` and `Position="0,0"` are set in the Avalonia window, and no window manager is started in `~/.xinitrc`, so the app renders flush at the top-left corner with no title bar or border offset. The cursor is hidden immediately at X startup (touchscreen only).
