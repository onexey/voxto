# Voxto — Icon Pack (Direction 07 · Bubble + V-Wave)

Final icon assets for Voxto, a local Whisper-based voice transcription app.
The mark is a rounded speech bubble holding a five-bar waveform whose
heights form a V silhouette (tall–mid–short–mid–tall). One mark, two reads:
the bubble carries the "voice → text" story; the V quietly carries the brand.

This package contains everything needed to ship the executable icon, the
taskbar/dock icon, and the live system-tray indicator that changes color
with app state.

---

## States

The mark is rendered in three colors that map to runtime state:

| State          | Hex (sRGB)  | OKLCH                      | Meaning                          |
| -------------- | ----------- | -------------------------- | -------------------------------- |
| `ready`        | `#3DB36A`   | `oklch(0.72 0.17 145)`     | Idle, ready to record (default)  |
| `recording`    | `#E5484D`   | `oklch(0.65 0.22 25)`      | Capturing user audio             |
| `transcribing` | `#C99A2E`   | `oklch(0.65 0.15 80)`      | Whisper is processing recording  |

Bubble fill = state color. Waveform bars are pure white (`#FFFFFF`).
The default executable / start-menu icon uses **`ready`** (green).

---

## Folder layout

```
voxto-icon-pack/
├── README.md                       (this file)
├── SPEC.md                         (implementation spec for AI handoff)
│
├── master/                         hand-authored vector sources
│   ├── voxto-master-ready.svg          64×64 grid, default green
│   ├── voxto-master-recording.svg      64×64 grid, red
│   └── voxto-master-transcribing.svg   64×64 grid, yellow
│
├── tray-16/                        hand-tuned for 16×16 pixel grid
│   ├── voxto-tray-16-ready.svg
│   ├── voxto-tray-16-recording.svg
│   └── voxto-tray-16-transcribing.svg
│
├── png/                            rasterized PNGs at all sizes
│   ├── ready/
│   │   ├── voxto-ready-16.png
│   │   ├── voxto-ready-24.png
│   │   ├── voxto-ready-32.png
│   │   ├── voxto-ready-48.png
│   │   ├── voxto-ready-64.png
│   │   ├── voxto-ready-128.png
│   │   ├── voxto-ready-256.png
│   │   └── voxto-ready-512.png
│   ├── recording/
│   │   └── (same sizes)
│   └── transcribing/
│       └── (same sizes)
│
└── animated/                       optional motion variants (SVG SMIL + CSS)
    ├── voxto-recording-animated.svg     bars dance
    └── voxto-transcribing-animated.svg  pulse + sweep
```

---

## File-naming convention

```
voxto-{variant}-{state}-{size}.{ext}
```

- **variant** — `master` (full mark), `tray-16` (pixel-tuned)
- **state** — `ready` | `recording` | `transcribing`
- **size** — pixel size for raster outputs (16, 24, 32, 48, 64, 128, 256, 512)
- **ext** — `svg` for vector, `png` for raster

The 16×16 tray variant has a **simplified mark** (3 thicker bars instead of 5)
to stay crisp on a 16-pixel grid. Use it for tray/notification-area at 16px.
The standard master mark is used at 24px and above.

---

## Quick usage by surface

| Surface                         | File                                          |
| ------------------------------- | --------------------------------------------- |
| Windows `.exe` icon             | `png/ready/voxto-ready-256.png` → `.ico`      |
| Windows taskbar (pinned)        | `png/ready/voxto-ready-32.png`                |
| Windows tray @ 16px             | `tray-16/voxto-tray-16-{state}.svg` per state |
| macOS app bundle                | `png/ready/voxto-ready-512.png` → `.icns`     |
| macOS menu bar (template)       | tray-16 variants, recolored by macOS          |
| Website / README                | `master/voxto-master-ready.svg`               |

For Windows `.ico`, embed at minimum: 16, 24, 32, 48, 256.
For macOS `.icns`, embed at minimum: 16, 32, 64, 128, 256, 512 (and @2x).

See **SPEC.md** for the full state-machine + integration contract.
