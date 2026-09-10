#!/bin/bash
# One-time device prep: USB update support for the Neomotive appliances.
#
# The app's USB scanner only looks at /media/usb — UsbUpdateSource.HasRemovableDrive()
# tests /proc/mounts for exactly that mount point and bails before scanning anything
# else. The appliance image is Raspberry Pi OS Lite with no udisks2 and no desktop
# session, so nothing auto-mounts a stick. Without this rule, inserting a USB drive
# does nothing at all.
#
# This installs the same helper + udev rule the simulator gets from
# setup-autostart.sh. Keep the two copies of neomotive-usb-mount.sh in step.
#
# IMPORTANT — read-only overlay:
#   The appliance rootfs is an overlay, so /usr/local/bin and /etc/udev survive
#   only in RAM and are lost on reboot. Lift the overlay first, run this, then
#   put it back:
#
#     sudo raspi-config nonint disable_overlayfs && sudo reboot
#     # reconnect, then:
#     sudo /tmp/setup-usb-updates.sh
#     sudo raspi-config nonint enable_overlayfs && sudo reboot
#
# Usage:
#   scp Apps/<app>/scripts/pi/neomotive-usb-mount.sh \
#       Apps/<app>/scripts/pi/setup-usb-updates.sh pi@<host>:/tmp/
#   ssh -t pi@<host> 'chmod +x /tmp/setup-usb-updates.sh && sudo /tmp/setup-usb-updates.sh'

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HELPER_SRC="$SCRIPT_DIR/neomotive-usb-mount.sh"
UNIT_SRC="$SCRIPT_DIR/neomotive-usb-mount@.service"

if [ "$(id -u)" -ne 0 ]; then
    echo "setup-usb-updates: must run as root (sudo)." >&2
    exit 1
fi

if [ ! -f "$UNIT_SRC" ]; then
    echo "setup-usb-updates: neomotive-usb-mount@.service not found next to this script." >&2
    echo "  Copy all three files to the device together." >&2
    exit 1
fi

if [ ! -f "$HELPER_SRC" ]; then
    echo "setup-usb-updates: neomotive-usb-mount.sh not found next to this script." >&2
    echo "  Copy both files to the device together." >&2
    exit 1
fi

# A writable /usr means the overlay is down. If it is still up, everything below
# lands in RAM and vanishes on the next reboot — which would look like it worked.
if ! touch /usr/local/bin/.neomotive-write-test 2>/dev/null; then
    echo "setup-usb-updates: /usr/local/bin is not writable." >&2
    echo "  Lift the read-only overlay first:" >&2
    echo "    sudo raspi-config nonint disable_overlayfs && sudo reboot" >&2
    exit 1
fi
rm -f /usr/local/bin/.neomotive-write-test

if grep -qs 'overlayroot' /proc/cmdline || findmnt -no FSTYPE / | grep -qs overlay; then
    echo "WARNING: / still looks like an overlay mount. If this device reboots and USB"
    echo "         updates stop working, the overlay was up and this install was in RAM."
fi

install -m 0755 "$HELPER_SRC" /usr/local/bin/neomotive-usb-mount
install -m 0644 "$UNIT_SRC" /etc/systemd/system/neomotive-usb-mount@.service
mkdir -p /media/usb

cat > /etc/udev/rules.d/99-neomotive-usb.rules <<'UDEVRULE'
# Mount USB storage at /media/usb for Neomotive update packages.
#
# udev must NOT mount here. A udev worker runs in a private mount namespace, so
# `mount` from a RUN+= rule fails outright ("mount: /media/usb: permission
# denied") or lands somewhere no other process can see. Pull in a systemd unit
# instead and let systemd do the mount in the host namespace.
#
# ID_FS_USAGE=="filesystem" means blkid has already probed the partition and
# found a mountable filesystem, so this fires only once the device is genuinely
# ready. ID_BUS=="usb" keeps the SD card out of it.
#
# No remove rule is needed: the unit is BindsTo=dev-%i.device, so pulling the
# stick stops it and ExecStop unmounts.
ACTION=="add", SUBSYSTEM=="block", KERNEL=="sd[a-z][0-9]", ENV{ID_BUS}=="usb", ENV{ID_FS_USAGE}=="filesystem", ENV{SYSTEMD_WANTS}+="neomotive-usb-mount@%k.service"
UDEVRULE

systemctl daemon-reload
udevadm control --reload-rules

echo "Installed:"
echo "  /usr/local/bin/neomotive-usb-mount"
echo "  /etc/systemd/system/neomotive-usb-mount@.service"
echo "  /etc/udev/rules.d/99-neomotive-usb.rules"

echo ""
echo "Done. Verify by inserting a USB stick and checking:"
echo "  findmnt /media/usb"
echo "  journalctl -t neomotive-usb-mount"
echo "  systemctl status 'neomotive-usb-mount@*'"
echo ""
echo "Then re-enable the overlay:"
echo "  sudo raspi-config nonint enable_overlayfs && sudo reboot"
