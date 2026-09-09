# Releasing and Updating ScanTool & ModuleSimulator

How a build becomes a release, and how a device in the field picks it up.

Companion docs: [creating-packages.md](creating-packages.md) (package internals),
[update-network-pi.md](update-network-pi.md) and [update-usb-pi.md](update-usb-pi.md)
(operator-facing steps), [migration-from-pre-update.md](migration-from-pre-update.md).

---

## 1. The model

Both apps carry the same updater (`Apps/Shared/Neomotive.Update`) and the same
on-device layout. `baseDir` is the payload root; the binary runs one level down
in an A/B slot:

```
<baseDir>/                     ScanTool: /data/app     Simulator: /opt/neomotive
├── app-current/               ← the running binary
├── app-previous/              ← the slot it replaced (rollback source)
├── app-staging/               ← extracted + hash-checked, deleted after the swap
├── config/                    ← UDS catalog overlays etc. — survives updates
├── data/                      ← captures, settings — survives updates
├── neomotive.config.json      ← device-local; holds updateServerUrl
├── local.env                  ← device-local env overrides (ScanTool only)
└── update-state.json          ← pending-version marker
```

An update never writes into the running slot. The app extracts to `app-staging/`,
verifies every file's SHA256 against the package manifest, then renames
`app-current → app-previous` and `app-staging/app → app-current`. If any step
fails, staging is deleted and the live slot is untouched.

### Restart

On Linux the app cannot relaunch itself usefully — after the swap its own path
points into `app-previous`, and a child spawned from a dying process loses the
display. So the app **exits with code 42** (`UpdateService.RestartExitCode`) and
the launcher, which still owns the tty/DRM handle, starts the newly promoted
binary:

- ScanTool — `scripts/pi/run` loops and re-resolves `app-current/scantool`
- Simulator — `scripts/pi/xinitrc` loops on `app-current/simulator`

Any other exit code is the app's own and is passed through unchanged. On Windows
there is no launcher, so `Apply()` reports `RequiresRestart = true` and the UI
says "staged — restart to apply."

### Two delivery paths

| | Network | USB |
|---|---|---|
| Trigger | operator taps **Check for Updates** | drive inserted, polled every 5 s |
| Source | `version-manifest.json` over HTTP | `neomotive-update*.zip` on removable media |
| Configured by | `updateServerUrl` in `neomotive.config.json` | nothing — always on |
| Off switch | leave `updateServerUrl` null | — |

Both converge on the same extract → verify → swap → restart path.

---

## 2. Cutting a release (CI — the normal path)

Push a tag; `.github/workflows/release.yml` does the rest.

```
can-scan-v1.1.0   → ScanTool 1.1.0
can-sim-v1.1.0    → ModuleSimulator 1.1.0
```

```bash
git tag can-scan-v1.1.0 && git push origin can-scan-v1.1.0
```

The workflow derives target and version from the tag, builds the package, verifies
`update.json` matches the tag, publishes the zip as a release asset, and merges that
entry into the manifest devices poll. The tag is the only version input — the csproj
`<Version>` stays 1.0.0 and is overridden at publish time.

**Devices point at one fixed URL, forever:**

```
https://github.com/neomotive/neomotive/releases/download/updates-latest/version-manifest.json
```

`updates-latest` is a rolling release holding only that manifest. Each tag release
merges its own `{target}-linux-arm64` key in and leaves the other target's key alone,
so releasing ScanTool never un-publishes the simulator. The zips live in their own
per-version releases. The repo is public, so devices download without credentials.

Tag hygiene the workflow enforces, so a bad tag fails the build instead of shipping:

- prefix must be `can-scan-v` or `can-sim-v`
- version must be `MAJOR.MINOR.PATCH` — `can-scan-v1.2` is rejected, because the
  device's version comparison would never offer it

Releases are serialised by a concurrency group; two tags pushed at once queue rather
than racing the shared manifest.

### Dependencies in CI

Everything Meadow resolves from nuget.org at `3.0.0-beta`, except **Telematics.J1979**
and **Telematics.Uds**, which are not published. The workflow checks out
`WildernessLabs/Meadow.Foundation` into a sibling `wilderness/` directory so the
existing relative `ProjectReference` paths resolve, and builds those two from source.

`MEADOW_FOUNDATION_REF` at the top of the workflow selects the ref. It defaults to the
`meadow-3.0` branch; pin a full SHA when you want a rebuild of an old tag to produce
what it originally shipped.

---

## 3. Cutting a release by hand (fallback)

Still works, and is what CI runs under the hood. Use it to test a package before
tagging, or when CI is unavailable.

### Step 1 — Build the packages

From `F:\repos\neomotive\software\dotnet`:

```powershell
.\scripts\create-update-package.ps1 -Target scantool  -Platform linux-arm64 -Version 1.1.0
.\scripts\create-update-package.ps1 -Target simulator -Platform linux-arm64 -Version 1.1.0
```

Each run publishes self-contained, hashes every file, writes `update.json` into
the zip, and adds/updates an entry in `dist\version-manifest.json`. Both targets
write to the same manifest, so run them in any order and the file accumulates.

Output in `dist\`:

```
neomotive-update-1.1.0-scantool-linux-arm64.zip
neomotive-update-1.1.0-simulator-linux-arm64.zip
version-manifest.json
```

**The version must be higher than what the device runs.** `NetworkUpdateSource`
compares against the running assembly's informational version and returns "no
update" for equal or lower. The csproj `<Version>` is 1.0.0 and is overridden by
`-Version` at publish time — the number you pass here is the number that ships.

### Step 2 — Fix the URLs in `version-manifest.json`

The script writes `http://localhost:8080/<package>.zip` as a placeholder. Rewrite
each `url` to a host the device can actually reach:

```json
{
  "scantool-linux-arm64": {
    "version": "1.1.0",
    "url": "http://192.168.1.50:8080/neomotive-update-1.1.0-scantool-linux-arm64.zip",
    "sha256": "…"
  },
  "simulator-linux-arm64": { "version": "1.1.0", "url": "…", "sha256": "…" }
}
```

Keys are `{target}-{platform}` and must match exactly — a device looks up its own
key and silently reports "no update available" if it is absent or misspelled.
Leave the `sha256` values alone; they gate the download.

### Step 3 — Serve it

Static files only, no server process to manage:

```powershell
python -m http.server 8080 --directory dist
```

For anything beyond a bench test, put the two zips and the manifest behind normal
static hosting. HTTPS is recommended — the payload is hash-verified end to end,
but the manifest that carries those hashes is not signed, so plain HTTP trusts
the network.

### Step 4 — Point the devices at it

Once per device, in `neomotive.config.json` at the **payload root** (not inside
`app-current/`, so deploys and updates never overwrite it):

```bash
# ScanTool
ssh pi@pi-appliance.local 'cat > /data/app/neomotive.config.json' <<'EOF'
{ "updateServerUrl": "http://192.168.1.50:8080/version-manifest.json" }
EOF
ssh -t pi@pi-appliance.local 'sudo systemctl restart app.service'

# Simulator
ssh pi@neomotive-sim 'cat > /opt/neomotive/neomotive.config.json' <<'EOF'
{ "updateServerUrl": "http://192.168.1.50:8080/version-manifest.json" }
EOF
```

Read once at startup, so the app must restart to pick up a change.

### Step 5 — Update

On the device: **Updates** tab → **Check for Updates**. The status line walks
through checking → downloading → applying, then the app restarts into the new
version. For USB instead, drop one `neomotive-update*.zip` on a drive and insert
it — see [update-usb-pi.md](update-usb-pi.md).

---

## 4. Release checklist

- [ ] Version number is higher than every device in the field
- [ ] Tag matches `can-scan-vX.Y.Z` / `can-sim-vX.Y.Z` exactly
- [ ] Workflow green, and the zip is attached to the tag's release
- [ ] `updates-latest/version-manifest.json` shows the new version under the right key,
      and still lists the *other* target
- [ ] Manifest reachable from the device (`curl` it over SSH)
- [ ] Updated one device end to end before rolling out further
- [ ] Confirmed the new version after restart, and that `config/` and `data/` survived

---

## 5. Bootstrapping a device that predates this

Network update is a property of the **running** build. A device on older software
has no updater to invoke, so its first hop is manual — after that it self-updates.

Both devices also need the A/B layout in place. `publish-scantool-pi.ps1 -Deploy`
now lays it down and removes the stale loose binary; the simulator's
`setup-autostart.sh` already creates `/opt/neomotive/app-current`.

```powershell
# ScanTool — creates /data/app/app-current, seeds neomotive.config.json
.\Apps\ScanTool\scripts\publish-scantool-pi.ps1 -Deploy -TargetHost pi@pi-appliance.local
```

For the simulator, copy the new build into `/opt/neomotive/app-current/` and
reinstall `~/.xinitrc` from `Apps/ModuleSimulator/scripts/pi/xinitrc` — the old
one `exec`s the app and would end the X session on the first self-restart instead
of relaunching.

Verify before relying on it:

```bash
ls /data/app                 # app-current/, run, neomotive.config.json
cat /data/app/neomotive.config.json
journalctl -u app.service -f
```

---

## 6. Troubleshooting

| Symptom | Cause |
|---|---|
| "No update server configured." | `updateServerUrl` is null, or the app has not restarted since it was set |
| "No update available" with a newer package published | manifest key is not `{target}-{platform}`, or the version is not strictly higher |
| Check fails silently, no download | manifest unreachable, malformed JSON, or the zip's SHA256 does not match the manifest |
| "SHA256 mismatch for …" | zip was rebuilt or truncated after the manifest was written — repackage both together |
| Update applies, app never comes back | launcher is still the old `exec` form — reinstall `run` / `.xinitrc` |
| Update applied but version unchanged | the launcher started a binary outside `app-current/` — remove any stale loose binary at the payload root |

Rollback is manual: stop the app, swap `app-current` and `app-previous`, start it
again. There is no automatic health check that reverts a bad update — a package
that starts and crashes stays current.

```bash
sudo systemctl stop app.service
cd /data/app && mv app-current app-bad && mv app-previous app-current
sudo systemctl start app.service
```
