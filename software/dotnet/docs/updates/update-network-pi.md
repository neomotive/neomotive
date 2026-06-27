# Network Updates — Raspberry Pi

Network updates are user-initiated: open the Config tab, scroll to SOFTWARE UPDATES, tap "Check for Updates." The app downloads and applies the update, then restarts automatically.

---

## How it works

1. App sends HTTP GET to the `updateServerUrl` from `neomotive.config.json`
2. Server returns `version-manifest.json` with the latest version for `simulator-linux-arm64`
3. If the remote version is newer, the app downloads the zip to `/tmp/`
4. SHA256 of the zip is verified against the manifest entry
5. Zip is extracted to `/opt/neomotive/app-staging/` and each file's SHA256 is verified against `update.json`
6. If all hashes pass: `app-current/` → `app-previous/`, `app-staging/` → `app-current/`
7. App self-restarts — the new version is live

If anything fails, `app-staging/` is deleted and `app-current/` is untouched.

---

## Step 1 — Configure the update server URL on the Pi

Create or edit `/opt/neomotive/neomotive.config.json`:

```json
{
  "updateServerUrl": "http://192.168.1.50:8080/version-manifest.json"
}
```

Replace `192.168.1.50` with your server's IP (or a hostname the Pi can resolve).

To do this over SSH:
```bash
ssh pi@neomotive-sim
cat > /opt/neomotive/neomotive.config.json << 'EOF'
{
  "updateServerUrl": "http://192.168.1.50:8080/version-manifest.json"
}
EOF
```

This file is read at every app startup. Changes take effect on next reboot (or kill/restart the X session).

---

## Step 2 — Start the update server

### Local dev (Windows build machine)

```powershell
# From the directory containing version-manifest.json and the zip
python -m http.server 8080 --directory dist

# Keep this terminal open while the Pi downloads
```

Verify the server is reachable from the Pi:
```bash
ssh pi@neomotive-sim curl -s http://192.168.1.50:8080/version-manifest.json
```

### Production / cloud

Upload `version-manifest.json` and the zip to your hosting. The URL in `version-manifest.json` must point to the zip's public location. No server process to manage — static files only.

---

## Step 3 — Build and publish the update

On the build machine:
```powershell
.\software\dotnet\scripts\create-update-package.ps1 `
  -Target simulator `
  -Platform linux-arm64 `
  -Version 1.2.0 `
  -OutputDir .\dist
```

Then update `dist\version-manifest.json` — replace the `url` value with your server's actual IP/hostname:
```json
"simulator-linux-arm64": {
  "version": "1.2.0",
  "url": "http://192.168.1.50:8080/neomotive-update-1.2.0-simulator-linux-arm64.zip",
  "sha256": "abc123..."
}
```

---

## Step 4 — Trigger the update from the device

1. On the Pi touchscreen, open the **Config** tab
2. Scroll down to **SOFTWARE UPDATES**
3. Tap **Check for Updates**

Status messages during the update:
| Message | Meaning |
|---------|---------|
| `Checking for updates…` | Fetching version-manifest.json |
| `You are up to date.` | Remote version ≤ current version |
| `Update check failed: …` | Network error or bad manifest |
| `USB update found: v1.2.0. Applying…` | *(USB path — not shown for network)* |
| `v1.2.0 applied.` | Applied; app is about to restart |

After "applied" the app restarts automatically within ~1 second.

---

## Confirming the update

After restart, open Config → SOFTWARE UPDATES. The status will show the last result. There is no explicit version display yet — check `app-current/simulator --version` over SSH if needed, or look at the `AssemblyInformationalVersion` in the binary:

```bash
ssh pi@neomotive-sim
strings /opt/neomotive/app-current/simulator | grep -E '^[0-9]+\.[0-9]+\.[0-9]+'
```

---

## Rollback

The previous version is always kept in `app-previous/`. To roll back manually:

```bash
ssh pi@neomotive-sim
sudo pkill Xorg
mv /opt/neomotive/app-current /opt/neomotive/app-bad
mv /opt/neomotive/app-previous /opt/neomotive/app-current
sudo reboot
```

---

## Troubleshooting

| Symptom | Likely cause |
|---------|-------------|
| "Update check failed: connection refused" | Server not running or wrong IP in config |
| "Update check failed: No update server configured." | `neomotive.config.json` missing or `updateServerUrl` key absent |
| "Update failed: SHA256 mismatch" | Zip was corrupted in transit or `update.json` hashes are wrong |
| App does not restart after "v1.2.0 applied." | Self-restart failed — SSH in and `sudo reboot` |
| App comes back on old version | `app-staging/` verification failed silently — check `/tmp/simulator.log` |
