# Auto-Update

Voxto can check GitHub Releases for newer versions, download the installer in the background, verify its integrity, and apply it with a single click — no manual download or UAC prompt required.

## How it works

The update flow has five stages:

**1. Periodic check** — On startup (after a 45-second delay) and then every hour, `UpdateService` checks whether enough time has elapsed since the last successful check. If so, it calls the GitHub Releases API:

```
GET https://api.github.com/repos/onexey/voxto/releases/latest
```

**2. Version comparison** — The `tag_name` from the response (e.g. `v2026.4.26.1`) is parsed into a `System.Version` and compared against the running assembly version. If the remote version is strictly greater, a download begins.

**3. Background download** — The architecture-appropriate MSI asset is streamed to `%LocalAppData%\Voxto\updates\`. A `.sha256` sidecar file is downloaded from the same release and used for integrity verification.

**4. Integrity check** — The SHA-256 of the downloaded MSI is computed locally and compared to the sidecar. If they don't match the file is deleted and the update is aborted.

**5. Apply** — When the user clicks **Restart to Update** in the tray menu, `UpdateService.ApplyUpdateAndRestart()` writes a PowerShell trampoline script to `%TEMP%`, launches it hidden, then shuts down the app. The trampoline:

```powershell
Start-Sleep -Seconds 3
$result = Start-Process -FilePath 'msiexec.exe' `
    -ArgumentList '/i "…\voxto-…-win-x64.msi" /passive /norestart /l*v "…\voxto_update.log"' `
    -Wait -PassThru
if ($result.ExitCode -eq 0 -and (Test-Path '…\voxto.exe')) {
    Start-Process -FilePath '…\voxto.exe'
}
Remove-Item -Path $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue
```

The MSI runs a silent install (`/passive` shows only a small progress bar), replaces all application files, then the trampoline relaunches the new executable. The script self-deletes on completion.

## Security

| Measure | Detail |
|---------|--------|
| HTTPS only | All GitHub API calls and asset downloads use TLS. The shared `HttpClient` sets no `AllowInsecureRedirects` override. |
| SHA-256 verification | The MSI is verified against the `.sha256` sidecar before the installer is ever executed. A mismatch causes the file to be deleted immediately. |
| No arbitrary code execution | `msiexec.exe` is a trusted Windows component. Only the specifically downloaded and hash-verified MSI path is passed to it — not a script or arbitrary executable. |
| No elevation required | The MSI uses `Scope="perUser"` (installs to `%LocalAppData%\Programs\Voxto`), so `msiexec /passive` runs without a UAC prompt. |
| PowerShell script `-ExecutionPolicy Bypass` | This flag is scoped to the single trampoline invocation and does not affect the system or user execution policy. |

## Tray menu

| Menu item | State |
|-----------|-------|
| `🔄  Check for Updates` | Default idle state |
| `⬇  Downloading v{version}…` | Download in progress (disabled) |
| `↺  Restart to Update v{version}` | Update ready — click to apply |

A pill notification appears briefly in the top-right corner when a download starts and when the update is ready.

## Preferences

Open **Preferences → General → UPDATES**:

- **Check for updates automatically** — toggle the background check on/off.
- **Frequency** — Daily or Weekly (default: Weekly).
- **Check Now** — run an immediate on-demand check.
- **Last checked** label — shows when the last check completed.

Settings are stored in `%LocalAppData%\Voxto\settings.json` alongside all other app preferences.

## Update cache

Downloaded MSI files are stored in `%LocalAppData%\Voxto\updates\`. They are not automatically deleted after a successful update; you can remove them manually if disk space is a concern. Future versions may add automatic cleanup.

## msiexec install log

The trampoline passes `/l*v "%TEMP%\voxto_update.log"` to msiexec. If an update fails silently, open that log file for diagnostics.
