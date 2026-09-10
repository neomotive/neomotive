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
├── neomotive.config.json      ← device-local; optional updateServerUrl override
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
| Trigger | operator taps **Check Internet for Updates** | drive inserted, polled every 5 s |
| Source | `version-manifest.json` over HTTP | `neomotive-update*.zip` on removable media |
| Configured by | nothing — defaults to the GitHub manifest | nothing — always on |
| Override | `updateServerUrl` in `neomotive.config.json` | — |

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

**Devices ship pointed at one fixed URL, forever** — it is the default compiled into
`UpdateService.DefaultManifestUrl`, so a stock device checks the internet with no
configuration at all:

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

Everything Meadow resolves from nuget.org at `3.0.1-beta` (the app csprojs float on
`3.*-*`), except **Telematics.J1979**
and **Telematics.Uds**, which are not published. The workflow checks out
`WildernessLabs/Meadow.Foundation` into a sibling `wilderness/` directory so the
existing relative `ProjectReference` paths resolve, and builds those two from source.

Building those two from source pulls in three more repos, because the references
between them are relative paths that assume everything is a sibling under
`wilderness/`: **Meadow.Contracts** (imported by Meadow.Foundation's
`Directory.Packages.props`, and referenced by Foundation.Core and both Telematics
drivers), and **Meadow.Logging** and **Meadow.Units**, which Meadow.Contracts
references. Meadow.Units points back into the Foundation checkout, so the set is
closed at those four.

`MEADOW_REF` at the top of the workflow selects the ref for all four. It defaults to
the `meadow-3.0` branch; pin a full SHA when you want a rebuild of an old tag to
produce what it originally shipped.

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

Only needed to *override* the built-in GitHub manifest — for a bench test against a
local server, say. A stock device already checks GitHub and needs none of this.

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

## 6. The Pi Appliance Kit constraint

The ScanTool image (and any simulator installed the same way) is built from
[Pi-Appliance-Kit](https://github.com/ctacke/Pi-Appliance-Kit). Its `app.service`
runs the app with:

```ini
ProtectSystem=strict
ReadWritePaths=/data
ProtectHome=yes
```

`ProtectSystem=strict` mounts the **entire** filesystem hierarchy read-only in
that unit's mount namespace. `ReadWritePaths=/data` punches one hole in it. So
inside the running app there is exactly one writable location — `/data` — and
that includes `/tmp`, which is read-only here despite being writable from an SSH
shell. This is the single most common way an update path breaks on the appliance:
it works when you test it over SSH and fails from the service.

Consequences the updater has to respect, all of them now enforced by
`Neomotive.Update.Tests.ApplianceLayoutTests`:

- **Every working directory hangs off `baseDir`** (`/data/app`): `app-current/`,
  `app-previous/`, `app-staging/`, `app-downloads/`, `config/`, `data/`.
  `NetworkUpdateSource` takes its download directory as a constructor argument
  for this reason — it used `Path.GetTempPath()`, and every network update on the
  appliance died with `Access to the path '/tmp/neomotive-update-….zip' is denied`.
- **`run` exports `TMPDIR`/`TMP`/`TEMP`** into `$APP_DIR/.tmp`, alongside `HOME`,
  `XDG_RUNTIME_DIR` and `DOTNET_BUNDLE_EXTRACT_BASE_DIR`. `Path.GetTempPath()`
  honours `TMPDIR`, so anything that still reaches for temp lands somewhere
  writable.
- **Space is finite and shared.** Extraction, the downloaded zip and two full
  slots all live on `/data`. `UpdateService` refuses an update up front when free
  space is under 3× the package rather than half-extracting, and deletes a
  network download once it has been applied (a USB package belongs to the
  operator's stick and is never touched).
- **An interrupted update leaves `app-staging/`.** `UpdateApplicator.EnsureLayout()`
  runs at startup and clears it, plus any abandoned `neomotive-update-*.zip`.

### The launcher is inside the payload

`$APP_DIR/run` sits *outside* the A/B slots, so an update package — which only
ever replaces `app-current/` — could never change it, and every launcher fix
meant SSH or a USB stick on every device.

`create-update-package.ps1` now bundles the launcher scripts into the payload at
`app/launcher/` (`run` for the ScanTool; `run` + `xinitrc` for the simulator),
and `$APP_DIR/run` hands off to `app-current/launcher/run` when the promoted slot
carries one. The handoff is deliberately timid — a bad launcher on a read-only
appliance is a brick with no console — so it delegates only to a readable file
that passes `sh -n`, exports `NEOMOTIVE_APP_DIR` so the packaged copy still knows
the real app root, sets `NEOMOTIVE_LAUNCHER_DELEGATED` to stop a re-exec loop, and
falls through to its own built-in logic on any doubt.

Launcher scripts carry no file extension, so `.gitattributes` pins `run`,
`xinitrc` and `bash_profile` to LF explicitly. A CRLF shebang makes `app.service`
fail with `bad interpreter: /bin/sh^M`, and `install-app.sh` rsyncs these straight
from the working tree.

### Simulator on an appliance image

The simulator's classic deployment (`/opt/neomotive`, autologin + `startx`) still
works unchanged. To run it on an appliance image instead, install
`Apps/ModuleSimulator/scripts/pi/` to `/data/app` with the kit's
`install-app.sh`: `run` sets the same environment and then `xinit`s the packaged
`xinitrc`, because the simulator is a windowed Avalonia app and `app.service` has
no controlling tty for `startx` to find. `xinitrc` no longer hardcodes
`/opt/neomotive` — it honours `NEOMOTIVE_APP_DIR`, then `/data/app`, then
`/opt/neomotive` — and keeps the exit-42 supervise loop in either layout.

## 6. Troubleshooting

| Symptom | Cause |
|---|---|
| "Could not reach the update server: …" | device is offline, or DNS/proxy is blocking github.com |
| "Update server timed out." | manifest host unreachable — the check no longer reports this as "up to date" |
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
