# Creating and Distributing Update Packages

## Overview

Update packages are zip files containing a manifest (`update.json`) and optionally new app binaries (`app/`) and/or config file overrides (`config/`). The `create-update-package.ps1` script handles building, hashing, and packaging in one step.

---

## Prerequisites

- .NET SDK installed on the build machine
- PowerShell 7+
- The `software/dotnet/` tree checked out

---

## Full app update

Builds a self-contained publish, hashes every output file, and creates the zip.

```powershell
cd software/dotnet/scripts

# Simulator for Pi (most common)
.\create-update-package.ps1 -Target simulator -Platform linux-arm64 -Version 1.2.0

# Simulator for Windows desktop
.\create-update-package.ps1 -Target simulator -Platform windows -Version 1.2.0

# ScanTool for Windows
.\create-update-package.ps1 -Target scantool -Platform windows -Version 1.2.0
```

### Output (`dist/` by default)

```
dist/
├── neomotive-update-1.2.0-simulator-linux-arm64.zip   ← the update package
└── version-manifest.json                               ← server-side version index
```

`version-manifest.json` maps `{target}-{platform}` keys to `{ version, url, sha256 }`. The URL defaults to `http://localhost:8080/...` — **update it before network distribution** (see below).

---

## Config-only update (catalog refresh)

A config-only update swaps `config/` files without replacing the app binary. No rebuild needed. The app picks up new catalog data on next VIN decode — no restart required.

Create the package manually:

```
my-config-update/
├── update.json
└── config/
    ├── manufacturers.json
    └── model-catalog.json
```

`update.json`:
```json
{
  "version": "1.0.1",
  "target": "simulator",
  "platform": "any",
  "type": "config-only",
  "timestamp": "2026-06-22T00:00:00Z",
  "files": [
    { "path": "config/manufacturers.json", "sha256": "<sha256-of-file>" },
    { "path": "config/model-catalog.json", "sha256": "<sha256-of-file>" }
  ]
}
```

Compute SHA256 hashes (PowerShell):
```powershell
(Get-FileHash config\manufacturers.json -Algorithm SHA256).Hash.ToLowerInvariant()
(Get-FileHash config\model-catalog.json -Algorithm SHA256).Hash.ToLowerInvariant()
```

Zip the folder:
```powershell
Compress-Archive -Path my-config-update\* -DestinationPath dist\neomotive-update-1.0.1-simulator-any.zip
```

---

## Distributing via USB

1. Copy the zip to the root of a FAT32 or ext4 USB drive:
   ```
   neomotive-update-1.2.0-simulator-linux-arm64.zip
   ```
   Or place it inside a `NEOMOTIVE/` subfolder at the drive root.
2. Insert the drive into the Pi while the app is running — detection is automatic.

See [update-usb-pi.md](update-usb-pi.md) for the full USB update procedure.

---

## Distributing via network

### Local dev server

```bash
# Serve the dist/ directory from your dev machine
python -m http.server 8080 --directory dist

# Or with dotnet-serve (install once: dotnet tool install -g dotnet-serve)
dotnet serve --directory dist --port 8080
```

Update `version-manifest.json` to point to your machine's IP:
```json
{
  "simulator-linux-arm64": {
    "version": "1.2.0",
    "url": "http://192.168.1.50:8080/neomotive-update-1.2.0-simulator-linux-arm64.zip",
    "sha256": "<zip-sha256>"
  }
}
```

The zip SHA256 is printed by the build script and written into `version-manifest.json` automatically; only the `url` host needs updating.

### Cloud / production

Upload both the zip and `version-manifest.json` to a static file host (Azure Blob, S3, nginx, etc.). Point the `url` field at the public URL. `neomotive.config.json` on each device points at the manifest URL.

---

## Bumping the version

Before building a release, update `<Version>` in the relevant `.csproj`:

| File | App |
|------|-----|
| `ScanTool/Neomotive.ScanTool.Desktop/Neomotive.ScanTool.Desktop.csproj` | ScanTool |
| `ModuleSimulator/Neomotive.ModuleSimulator.Desktop/Neomotive.ModuleSimulator.Desktop.csproj` | Simulator Desktop |
| `ModuleSimulator/Neomotive.ModuleSimulator.RaspberryPi/Neomotive.ModuleSimulator.RaspberryPi.csproj` | Simulator Pi |

The build script overrides the version at publish time via `-p:Version`, so the `.csproj` value is the dev/debug default. For a release, pass `-Version` explicitly to the script — it takes precedence.
