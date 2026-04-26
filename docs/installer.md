# MSI Installer

Voxto ships as a per-user MSI installer built with [WiX Toolset v5](https://wixtoolset.org/).

## Design decisions

**Per-user install (`Scope="perUser"`)** — Voxto installs to `%LocalAppData%\Programs\Voxto`. This means:

- No UAC elevation prompt during first install or subsequent updates.
- The installer is suitable for environments where the user doesn't have admin rights.
- `msiexec /passive` (the command used by the auto-updater) works silently without elevation.

**MajorUpgrade** — Every new version unconditionally removes the previous one before installing. Stale files (removed DLLs, renamed binaries) are always cleaned up. Downgrading is blocked with a friendly error message.

**File harvesting** — `installer.wixproj` uses `HarvestDirectory` to auto-discover every file in the `dotnet publish` output. No manual file list is maintained; new Whisper native DLLs added by NuGet upgrades are picked up automatically.

**No launch-after-install custom action** — Fresh installs are launched from the Start Menu shortcut created by the installer. Updates are handled by `UpdateService`'s PowerShell trampoline, which relaunches the app after `msiexec` finishes. This removes the need for architecture-specific WiX custom-action DLLs.

## Installer artefacts (per release)

| File | Purpose |
|------|---------|
| `voxto-{version}-win-x64.msi` | Installer for x64 machines |
| `voxto-{version}-win-arm64.msi` | Installer for ARM64 machines |
| `voxto-{version}-win-x64.msi.sha256` | SHA-256 hash of the x64 MSI (used by auto-updater) |
| `voxto-{version}-win-arm64.msi.sha256` | SHA-256 hash of the ARM64 MSI |
| `voxto-{version}-win-x64.zip` | Portable zip — no install needed |
| `voxto-{version}-win-arm64.zip` | Portable zip — ARM64 |

## Repository layout

```
installer/
├── installer.wixproj   # WiX MSBuild project — controls version, publish dir, harvesting
└── Package.wxs         # Package definition — install paths, shortcuts, upgrade config
```

## Build locally

```powershell
# 1. Publish the app first
dotnet publish voxto/voxto.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output publish/win-x64

# 2. Build the MSI
dotnet build installer/installer.wixproj `
    -p:Version="1.0.0.0" `
    -p:PublishDir="$(Resolve-Path publish\win-x64)\" `
    -p:OutputName="voxto-1.0.0.0-win-x64" `
    -p:OutputPath="$(Resolve-Path .)\" `
    -p:Platform="x64" `
    --configuration Release
```

The MSI file appears in the current directory as `voxto-1.0.0.0-win-x64.msi`.

## CI/CD

The `publish.yml` workflow builds the MSI automatically for every release. The key steps are:

1. `dotnet publish` — self-contained, all native DLLs flattened to the output root.
2. Sign `voxto.exe` (skipped if `SIGNING_CERT_BASE64` secret is absent).
3. `dotnet build installer/installer.wixproj` — WiX SDK pulled from NuGet, no separate install step.
4. Sign the MSI (same cert, skipped if secret absent).
5. `Get-FileHash -Algorithm SHA256` → write bare hex to `.msi.sha256` sidecar.
6. Upload MSI + sidecar + ZIP as release assets.

## UpgradeCode

The `UpgradeCode` GUID in `Package.wxs` is **`{7C9A5E2F-3B8D-4F1A-9E6C-2D5A8B0F3E7C}`**.

This value must **never change**. Changing it breaks the upgrade chain: Windows Installer treats the new package as a completely different product and will not remove the old one, leaving two "Voxto" entries in Add/Remove Programs.

## Install location

```
%LocalAppData%\Programs\Voxto\
    voxto.exe
    ggml-*.dll          ← Whisper native libraries
    *.dll               ← .NET runtime (self-contained)
    …
```

## Uninstall

Use **Windows Settings → Apps → Voxto → Uninstall** or **Control Panel → Add or Remove Programs → Voxto → Remove**.

The installer does **not** remove user data:
- `%LocalAppData%\Voxto\settings.json`
- `%LocalAppData%\Voxto\logs\`
- `%LocalAppData%\Voxto\updates\`
- `%Documents%\Voxto\`

These must be removed manually if a clean uninstall is needed.
