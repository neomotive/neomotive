#!/bin/bash
# Run once on the Pi to configure boot-to-app behavior.
# Requires: sudo apt install xorg unclutter

set -e

SCRIPTS_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Installing boot autostart for Neomotive Module Simulator ==="

# 1. Enable console autologin for the 'pi' user on tty1
#    This lets the Pi boot straight to a logged-in shell without a password prompt.
AUTOLOGIN_DIR=/etc/systemd/system/getty@tty1.service.d
sudo mkdir -p "$AUTOLOGIN_DIR"
sudo tee "$AUTOLOGIN_DIR/autologin.conf" > /dev/null <<EOF
[Service]
ExecStart=
ExecStart=-/sbin/agetty --autologin pi --noclear %I \$TERM
EOF

# 2. Install ~/.bash_profile — starts X automatically when pi logs into tty1
cp "$SCRIPTS_DIR/bash_profile" ~/.bash_profile
echo "Installed ~/.bash_profile"

# 3. Install ~/.xinitrc — X init that launches the app with no WM
cp "$SCRIPTS_DIR/xinitrc" ~/.xinitrc
chmod +x ~/.xinitrc
echo "Installed ~/.xinitrc"

# 4. Suppress boot splash phases
#    - disable_splash=1  removes the rainbow GPU test pattern
#    - boot_delay=0      removes the 1s firmware delay added for display sync
#    - quiet loglevel=3  suppresses kernel boot messages on the console
#    Raspberry Pi OS Bookworm moved the boot partition to /boot/firmware/;
#    older releases use /boot/.
if [ -f /boot/firmware/config.txt ]; then
    BOOT_DIR=/boot/firmware
else
    BOOT_DIR=/boot
fi

for setting in "disable_splash=1" "boot_delay=0"; do
    key="${setting%%=*}"
    if grep -q "^${key}" "$BOOT_DIR/config.txt" 2>/dev/null; then
        sudo sed -i "s/^${key}=.*/${setting}/" "$BOOT_DIR/config.txt"
    else
        echo "$setting" | sudo tee -a "$BOOT_DIR/config.txt" > /dev/null
    fi
done
echo "Updated $BOOT_DIR/config.txt (disable_splash, boot_delay)"

if ! grep -q "quiet" "$BOOT_DIR/cmdline.txt" 2>/dev/null; then
    sudo sed -i 's/$/ quiet loglevel=3/' "$BOOT_DIR/cmdline.txt"
    echo "Updated $BOOT_DIR/cmdline.txt (quiet boot)"
fi

# 5. Install xorg.conf so X11 skips hardware probing on every boot
sudo cp "$SCRIPTS_DIR/xorg.conf" /etc/X11/xorg.conf
echo "Installed /etc/X11/xorg.conf"

# 7. Install boot splash service — shows /opt/neomotive/splash.png on the
#    framebuffer as early as possible, before X11 starts. No-op if splash.png
#    is absent (ConditionPathExists in the unit file handles this).
sudo cp "$SCRIPTS_DIR/neomotive-splash.service" /etc/systemd/system/neomotive-splash.service
sudo systemctl enable neomotive-splash.service
echo "Installed and enabled neomotive-splash.service"

# 8. Create A/B update directory layout under /opt/neomotive/
#    app-current/ is where the app binary lives; app-previous/ is the rollback slot.
#    data/ holds user settings (VIN config, input mappings) — survives updates.
mkdir -p /opt/neomotive/app-current
mkdir -p /opt/neomotive/config
mkdir -p /opt/neomotive/data
echo "Created /opt/neomotive/{app-current,config,data}/"

# 9. USB auto-mount for update packages
#    The Pi runs headless with no udisks2/desktop session, so USB drives are never
#    auto-mounted. This udev rule mounts any USB block device read-only at /media/usb
#    so the app's USB update scanner can find update packages.
sudo cp "$SCRIPTS_DIR/neomotive-usb-mount.sh" /usr/local/bin/neomotive-usb-mount
sudo chmod +x /usr/local/bin/neomotive-usb-mount
sudo mkdir -p /media/usb

sudo tee /etc/udev/rules.d/99-neomotive-usb.rules > /dev/null <<'EOF'
# Mount USB storage at /media/usb for Neomotive update packages.
# ENV{ID_FS_USAGE}=="filesystem" ensures the kernel has probed and confirmed
# a mountable filesystem on the partition before we attempt to mount it,
# avoiding the race where the add event fires before the device is ready.
ACTION=="add",    SUBSYSTEM=="block", KERNEL=="sd[a-z][0-9]", ENV{ID_FS_USAGE}=="filesystem", RUN+="/usr/local/bin/neomotive-usb-mount add %k"
ACTION=="remove", SUBSYSTEM=="block", KERNEL=="sd[a-z][0-9]", RUN+="/usr/local/bin/neomotive-usb-mount remove %k"
EOF

sudo udevadm control --reload-rules
echo "Installed USB auto-mount udev rule (mounts to /media/usb)"

# 10. Reload systemd so all changes take effect on next boot
sudo systemctl daemon-reload

echo ""
echo "Done. Reboot the Pi to start the app automatically on boot."
echo "  sudo reboot"
echo ""
echo "IMPORTANT: Deploy the app binary to /opt/neomotive/app-current/ (not /opt/neomotive/ directly)."
echo "  See deployment.md for details."
