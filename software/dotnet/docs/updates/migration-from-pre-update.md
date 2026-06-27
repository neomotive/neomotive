# Migration: Pre-Update-Support → Current

Devices deployed before the update mechanism was added have a flat directory layout that must be reorganized before updates can work. This is a one-time migration.

---

## What changed

| | Before | After |
|-|--------|-------|
| Pi binary location | `/opt/neomotive/simulator` | `/opt/neomotive/app-current/simulator` |
| Pi launch script (`~/.xinitrc`) | `cd /opt/neomotive && exec ./simulator` | `exec /opt/neomotive/app-current/simulator` |
| Pi base dir layout | flat — all files at `/opt/neomotive/` | A/B slots: `app-current/`, `app-previous/`, `config/` |
| Windows | binary wherever published | no change required (see below) |

---

## Pi migration

Do this once per device. The app does not need to be running; in fact it's easiest to do it from the console (HDMI + keyboard) or over SSH.

### 1 — Stop the X session

```bash
sudo pkill Xorg
# Wait a few seconds for the session to close
```

If you're SSH'd in you can skip this — just make sure the binary isn't actively writing files.

### 2 — Create the new directory structure

```bash
mkdir -p /opt/neomotive/app-current
mkdir -p /opt/neomotive/config
```

### 3 — Move existing app files into `app-current/`

```bash
cd /opt/neomotive

# Move everything except the directories we just created and splash.png
for item in *; do
  case "$item" in
    app-current|app-previous|app-staging|config|splash.png)
      # leave in place
      ;;
    *)
      mv "$item" app-current/
      ;;
  esac
done
```

`splash.png` stays at `/opt/neomotive/splash.png` — the boot splash systemd service looks there specifically.

After this, verify:
```bash
ls /opt/neomotive/
# Expected: app-current/  config/  splash.png  (and optionally neomotive.config.json)
ls /opt/neomotive/app-current/
# Expected: simulator  (plus all .dll, .so, appsettings.json, etc.)
```

### 4 — Update `~/.xinitrc`

**Option A — Re-run `setup-autostart.sh`** (recommended):

The updated script already contains the correct xinitrc. Copy the latest scripts folder to the Pi and run it:

```bash
# From your dev machine
scp -r software/dotnet/Apps/ModuleSimulator/scripts/pi pi@neomotive-sim:~/pi-scripts
ssh pi@neomotive-sim bash ~/pi-scripts/setup-autostart.sh
```

`setup-autostart.sh` re-installs `~/.xinitrc`, `~/.bash_profile`, `xorg.conf`, and the splash service. It is safe to re-run on an already-configured Pi.

**Option B — Edit manually**:

```bash
nano ~/.xinitrc
```

Change the last line from:
```bash
cd /opt/neomotive
exec ./simulator > /tmp/simulator.log 2>&1
```
to:
```bash
exec /opt/neomotive/app-current/simulator > /tmp/simulator.log 2>&1
```

### 5 — Add `neomotive.config.json` (optional, for network updates)

```bash
cat > /opt/neomotive/neomotive.config.json << 'EOF'
{
  "updateServerUrl": "http://YOUR-SERVER-IP:8080/version-manifest.json"
}
EOF
```

Skip this step if you will only use USB updates.

### 6 — Reboot

```bash
sudo reboot
```

### 7 — Verify

After reboot, the app should start normally. To confirm the new path is in use:

```bash
ssh pi@neomotive-sim
# Check the running process path
cat /proc/$(pgrep simulator)/cmdline | tr '\0' ' '
# Should show: /opt/neomotive/app-current/simulator
```

The Config tab → SOFTWARE UPDATES section should now show "No update check performed." confirming the update service initialized.

---

## Windows migration

No migration is required for Windows dev builds. The `UpdateService` detects whether it's running from `app-current/` and adapts:

- **If binary is NOT in a folder named `app-current/`**: the binary's own directory is used as the base. On the first update, `app-current/` and `app-previous/` are created as subdirectories next to the current binary.
- **If binary IS in `app-current/`**: the parent directory is used as the base (the expected production layout).

For a clean production Windows layout matching the Pi convention, place the published output into an `app-current/` subdirectory:

```
C:\Neomotive\ScanTool\
├── app-current\         ← published binary goes here
│   └── Neomotive.ScanTool.Desktop.exe
├── config\              ← optional catalog overrides
└── neomotive.config.json
```

Deploy with:
```powershell
.\create-update-package.ps1 -Target scantool -Platform windows -Version 1.0.0
# Extract the zip's app/ contents into app-current\
```

---

## Verifying the migration worked

After migration + reboot, confirm:

1. App starts normally via the touchscreen
2. Config tab → SOFTWARE UPDATES section is visible at the bottom
3. USB update: copy a newer-versioned zip to a USB drive, insert → status changes to "USB update found: vX.Y.Z. Applying…" within 5 seconds
4. After update completes: `ls /opt/neomotive/` shows `app-previous/` (the old version is now there)
