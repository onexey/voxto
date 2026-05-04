[![CI Gate](https://github.com/onexey/voxto/actions/workflows/ci.yml/badge.svg)](https://github.com/onexey/voxto/actions/workflows/ci.yml)
[![Publish](https://github.com/onexey/voxto/actions/workflows/publish.yml/badge.svg)](https://github.com/onexey/voxto/actions/workflows/publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

<a href="https://www.buymeacoffee.com/onexey" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me a Coffee" height="30"></a>

# Voxto

A minimal Windows tray app that records audio, transcribes it with Whisper.net, and saves the result as a Markdown file.

## Features

- 🎙️ Start/stop recording from the system tray or hotkey
- 🔴 Small always-on-top overlay while recording (hidden when idle)
- ⌨️ Hotkey support — **Toggle** (press once) or **Push-to-talk** (hold key), switchable from the tray
- 🤖 Model selection from the tray — Tiny / Small / Medium / Large V3 Turbo
- 🖊️ Output directly to the current cursor location, with optional Enter after insertion
- 📁 Configurable output folder (set once, auto-named files)
- 🔔 Balloon notification when transcription completes

## Requirements

- Windows 10/11
- .NET 10 SDK

## Setup

```bash
dotnet run
```

The model (~244 MB for Small) is downloaded automatically on first use to:
```
%LocalAppData%\Voxto\models\
```

Settings are saved to:
```
%LocalAppData%\Voxto\settings.json
```

## Default Hotkey

**F9** — change mode via tray → Hotkey Mode

| Mode | Behaviour |
|---|---|
| Toggle | Press F9 once to start, press again to stop |
| Push-to-talk | Hold F9 to record, release to stop |

## Tray Menu

| Item | Description |
|---|---|
| ▶ Start / ⏹ Stop Recording | Manual control |
| 🤖 Model | Pick Tiny / Small / Medium / Large V3 Turbo |
| ⌨ Hotkey Mode | Toggle or Push-to-talk |
| 📁 Set Output Folder… | Choose where `.md` files are saved |
| 📂 Open Output Folder | Opens folder in Explorer |

## Icon colours

| Colour | State |
|---|---|
| 🟢 Green | Idle |
| 🔴 Red | Recording |
| 🟡 Amber | Transcribing |

## Documentation

- [Output targets](docs/outputs.md)
- [Auto-update](docs/auto-update.md)
- [Installer](docs/installer.md)
- [Transcription performance](docs/transcription-performance.md)

## Output format

```markdown
# Transcription

**Date:** Friday, April 24, 2026
**Time:** 14:32:10

---

`00:00:01` → `00:00:04`
Hello, this is the first segment.

`00:00:04` → `00:00:09`
And this is the second segment.
```
