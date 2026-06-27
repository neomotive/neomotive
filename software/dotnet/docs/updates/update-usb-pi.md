# USB Updates — Raspberry Pi

USB updates are fully automatic: insert a drive containing a valid update package and the app detects and applies it within ~5 seconds. No user interaction is required beyond inserting the drive.

---

## How it works

- The app polls removable drives every 5 seconds
- On each poll it scans for `neomotive-update*.zip` at the drive root and inside a `NEOMOTIVE/` subfolder
- If a matching zip is found for `simulator-linux-arm64` with a version newer than the running app:
  1. Zip is extracted to `/opt/neomotive/app-staging/`
  2. SHA256 of every file is verified against `update.json`
  3. `app-current/` → `app-previous/`, `app-staging/` → `app-current/`
  4. App self-restarts

The USB drive can be removed after the status changes to "applying" — the zip has already been extracted locally.

---

## Step 1 — Prepare the USB drive

1. Format the drive as FAT32 (recommended for cross-platform) or ext4
2. Copy the update zip to the **root** of the drive:
   ```
   neomotive-update-1.2.0-simulator-linux-arm64.zip
   ```
   Or place it inside a `NEOMOTIVE/` folder at the root:
   ```
   NEOMOTIVE/
   └── neomotive-update-1.2.0-simulator-linux-arm64.zip
   ```

The filename must match the pattern `neomotive-update*.zip`. The `target` and `platform` fields inside `update.json` determine whether it applies to this device.

Build the package with:
```powershell
.\software\dotnet\scripts\create-update-package.ps1 `
  -Target simulator -Platform linux-arm64 -Version 1.2.0
```
The zip is written to `dist/` and is ready to copy to USB.

---

## Step 2 — Insert the USB drive

Insert the drive into any USB port on the Pi while the app is running. The Pi mounts USB drives under `/media/pi/<label>/`.

Within ~5 seconds the Config tab → SOFTWARE UPDATES section shows:

```
USB update found: v1.2.0. Applying…
```

The app then:
1. Extracts and verifies the package
2. Performs the A/B slot swap
3. Restarts automatically

---

## Step 3 — Wait for restart

The app will close and reopen. After restart the new version is running. The status line in Config → SOFTWARE UPDATES shows:

```
v1.2.0 applied.
```

You can remove the USB drive at any point after "Applying…" appears.

---

## Config-only USB update

Config-only packages (VIN catalog refresh, etc.) work the same way but the app does **not** restart — the status shows `v1.0.1 applied.` and the new catalog is used from that point on.

---

## Rollback

On a successful update, `app-previous/` holds the prior version. To roll back:

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
| No detection after 10+ seconds | Drive not mounted under `/media/pi/` — check `lsblk` and `dmesg \| tail` over SSH |
| No detection — drive mounted | Zip filename doesn't match `neomotive-update*.zip`; or `target`/`platform` in `update.json` doesn't match `simulator`/`linux-arm64` |
| "Update failed: SHA256 mismatch" | File corruption on the drive; re-copy the zip |
| "Update failed: Expected file missing" | Zip was truncated; re-copy or re-build the package |
| App shows old version after restart | `app-staging/` verification failed — `app-current/` was not replaced; check `/tmp/simulator.log` |
| Drive mounts at a different path | Edit `UsbUpdateSource.GetLinuxMediaRoots()` in `Neomotive.Update/UsbUpdateSource.cs` to include the actual mount root |
