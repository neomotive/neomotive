# Raspberry Pi Deployment

## Prerequisites

```bash
sudo apt install xorg unclutter
```

## Deploy the app

Copy the published build to the Pi:

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

`setup-autostart.sh` does three things:
- Installs a systemd autologin drop-in so `pi` logs in on tty1 at boot (no password prompt)
- Installs `~/.bash_profile` which calls `startx` when logged into tty1
- Installs `~/.xinitrc` which disables screensaver/blanking and launches the app

## Window behavior

`SystemDecorations="None"` and `Position="0,0"` are set in the Avalonia window, and no window manager is started in `~/.xinitrc`, so the app renders flush at the top-left corner with no title bar or border offset. The cursor is hidden immediately at X startup (touchscreen only).
