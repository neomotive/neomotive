#!/bin/bash
# One-time device prep: run the ModuleSimulator on a Pi Appliance Kit image.
#
# The ScanTool needs none of this — it renders straight to /dev/dri via
# Avalonia's DRM/KMS backend. The simulator is a windowed Avalonia app, so it
# needs an X server, and the appliance image is Raspberry Pi OS Lite with no
# display stack at all.
#
# Three things have to change, and all three live on the read-only rootfs:
#
#   1. X packages           — apt is blocked while the overlay is on
#   2. app.service drop-in  — PrivateTmp=yes (see below)
#   3. USB auto-mount       — udev rule + helper, for USB update packages
#
# Why the drop-in: app.service runs with ProtectSystem=strict, which mounts the
# whole hierarchy read-only except /data. That includes /tmp — and the X server
# must create its socket at /tmp/.X11-unix/X0 or it dies before opening the
# display. PrivateTmp=yes gives the unit a private writable tmpfs /tmp, which
# fixes X without making the real /tmp writable. The app's own temp files stay
# on /data regardless: `run` exports TMPDIR=$APP_DIR/.tmp, so a 150 MB update
# download never lands in RAM.
#
# IMPORTANT — read-only overlay:
#   /usr, /etc and /var survive only in RAM and are lost on reboot. Lift the
#   overlay first, run this, then put it back:
#
#     sudo raspi-config nonint disable_overlayfs && sudo reboot
#     # reconnect, then:
#     sudo /tmp/setup-appliance.sh
#     sudo raspi-config nonint enable_overlayfs && sudo reboot
#
# Usage:
#   scp Apps/ModuleSimulator/scripts/pi/neomotive-usb-mount.sh \
#       Apps/ModuleSimulator/scripts/pi/setup-appliance.sh pi@neomotive-sim.local:/tmp/
#   ssh -t pi@neomotive-sim.local 'chmod +x /tmp/setup-appliance.sh && sudo /tmp/setup-appliance.sh'

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$(id -u)" -ne 0 ]; then
    echo "setup-appliance: must run as root (sudo)." >&2
    exit 1
fi

# Fail loudly on a forgotten overlay rather than silently installing into RAM,
# where everything here evaporates at the next reboot and the symptom is a
# device that worked yesterday and shows a black screen today.
if ! touch /usr/local/bin/.neomotive-write-test 2>/dev/null; then
    echo "setup-appliance: / is read-only — the overlay is still on." >&2
    echo "  sudo raspi-config nonint disable_overlayfs && sudo reboot" >&2
    exit 1
fi
rm -f /usr/local/bin/.neomotive-write-test

# ── 1. X server ──────────────────────────────────────────────────────────────
# xserver-xorg-core carries the modesetting driver; xfonts-base is not optional
# (the server refuses to start with an empty font path). The GL/EGL/input libs
# are already in the kit's optimizations.yaml for the ScanTool, but list them
# here too so this works on an image built before that landed.
echo "==> Installing X packages..."
apt-get update
apt-get install -y --no-install-recommends \
    xserver-xorg-core \
    xserver-xorg-input-libinput \
    xinit \
    x11-xserver-utils \
    xfonts-base \
    unclutter \
    libgl1-mesa-dri libegl1 libgles2 libinput10 libfontconfig1

# ── 2. app.service drop-in ───────────────────────────────────────────────────
echo "==> Installing app.service drop-in (PrivateTmp)..."
install -d /etc/systemd/system/app.service.d
cat > /etc/systemd/system/app.service.d/10-simulator-x11.conf <<'DROPIN'
# Installed by Neomotive ModuleSimulator setup-appliance.sh.
#
# app.service ships with ProtectSystem=strict, so /tmp is read-only in the
# unit's namespace and the X server cannot create /tmp/.X11-unix/X0. A private
# tmpfs satisfies X without opening the real /tmp for writing. The app's own
# temp files are unaffected: `run` points TMPDIR at /data/app/.tmp so update
# downloads stay on disk instead of in RAM.
[Service]
PrivateTmp=yes
DROPIN
systemctl daemon-reload

# ── 3. USB update support ────────────────────────────────────────────────────
# UsbUpdateSource.HasRemovableDrive() tests /proc/mounts for /media/usb and
# bails before scanning anything else. This image has no udisks2 and no desktop
# session, so without this rule inserting a stick does nothing at all.
HELPER_SRC="$SCRIPT_DIR/neomotive-usb-mount.sh"
if [ -f "$HELPER_SRC" ]; then
    echo "==> Installing USB auto-mount..."
    install -m 0755 "$HELPER_SRC" /usr/local/bin/neomotive-usb-mount
    cat > /etc/udev/rules.d/99-neomotive-usb.rules <<'UDEV'
KERNEL=="sd[a-z][0-9]", SUBSYSTEMS=="usb", ACTION=="add",    RUN+="/usr/local/bin/neomotive-usb-mount add %k"
KERNEL=="sd[a-z][0-9]", SUBSYSTEMS=="usb", ACTION=="remove", RUN+="/usr/local/bin/neomotive-usb-mount remove %k"
UDEV
    udevadm control --reload-rules
else
    echo "==> Skipping USB auto-mount: neomotive-usb-mount.sh not found next to this script."
    echo "    Copy both files to the device together to enable USB updates."
fi

echo ""
echo "Done. Restore the overlay and reboot:"
echo "  sudo raspi-config nonint enable_overlayfs && sudo reboot"
