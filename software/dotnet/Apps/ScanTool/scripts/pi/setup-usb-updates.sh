#!/bin/bash
# One-time device prep: USB update support for the ScanTool appliance.
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
#   scp Apps/ScanTool/scripts/pi/neomotive-usb-mount.sh \
#       Apps/ScanTool/scripts/pi/setup-usb-updates.sh pi@pi-appliance.local:/tmp/
#   ssh -t pi@pi-appliance.local 'chmod +x /tmp/setup-usb-updates.sh && sudo /tmp/setup-usb-updates.sh'

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HELPER_SRC="$SCRIPT_DIR/neomotive-usb-mount.sh"

if [ "$(id -u)" -ne 0 ]; then
    echo "setup-usb-updates: must run as root (sudo)." >&2
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

install -m 755 "$HELPER_SRC" /usr/local/bin/neomotive-usb-mount
mkdir -p /media/usb
echo "Installed /usr/local/bin/neomotive-usb-mount"

tee /etc/udev/rules.d/99-neomotive-usb.rules > /dev/null <<'EOF'
# Mount USB storage at /media/usb for Neomotive update packages.
# ENV{ID_FS_USAGE}=="filesystem" ensures the kernel has probed and confirmed
# a mountable filesystem on the partition before we attempt to mount it,
# avoiding the race where the add event fires before the device is ready.
ACTION=="add",    SUBSYSTEM=="block", KERNEL=="sd[a-z][0-9]", ENV{ID_FS_USAGE}=="filesystem", RUN+="/usr/local/bin/neomotive-usb-mount add %k"
ACTION=="remove", SUBSYSTEM=="block", KERNEL=="sd[a-z][0-9]", RUN+="/usr/local/bin/neomotive-usb-mount remove %k"
EOF

udevadm control --reload-rules
echo "Installed /etc/udev/rules.d/99-neomotive-usb.rules (mounts to /media/usb)"

echo ""
echo "Done. Verify by inserting a USB stick and checking:"
echo "  findmnt /media/usb"
echo "  journalctl -t neomotive-usb-mount"
echo ""
echo "Then re-enable the overlay:"
echo "  sudo raspi-config nonint enable_overlayfs && sudo reboot"
