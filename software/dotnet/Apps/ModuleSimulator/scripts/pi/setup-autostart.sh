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

# 4. Reload systemd so autologin takes effect on next boot
sudo systemctl daemon-reload

echo ""
echo "Done. Reboot the Pi to start the app automatically on boot."
echo "  sudo reboot"
