# Voxto Icon — Implementation Spec

Hand-off document for the engineer (or AI agent) wiring these icons into
the Voxto application.

---

## 1. Mark anatomy

- Canvas: **64×64** vector grid. Safe margin: 4px on all sides.
- Bubble shape: rounded squircle with a tail dropping from the bottom-left.
  Path is in `master/voxto-master-*.svg`.
- Waveform: 5 vertical white bars, 4px wide, 2px corner radius.
  Heights (px): `[22, 14, 8, 14, 22]` — symmetric V silhouette.
  Bars centered on y≈29 (slightly above bubble center to balance the tail).
- Bar X positions: `[16, 23, 30, 37, 44]` — 7px column pitch.

For the **16×16 tray variant** (`tray-16/`):
- 3 bars instead of 5 (the outer pair is dropped at this resolution).
- Bar widths bumped to ~2px and positioned on whole pixels.
- Bubble tail simplified to a single triangular notch.

---

## 2. State color tokens

Use **exactly** these values to keep state recognition consistent:

```css
--voxto-ready:        #3DB36A;   /* oklch(0.72 0.17 145) */
--voxto-recording:    #E5484D;   /* oklch(0.65 0.22 25)  */
--voxto-transcribing: #C99A2E;   /* oklch(0.65 0.15 80)  */
--voxto-ink-on-color: #FFFFFF;   /* waveform bars         */
```

The bubble body is filled with the state color; bars are always `#FFFFFF`.
There is no stroke. No gradients. Solid fills only — this is what keeps
the mark legible at 16px.

---

## 3. State machine the icon must reflect

```
            ┌──────────────┐
            │    READY     │  green, static
            └──────┬───────┘
                   │ user starts recording (hotkey / button)
                   ▼
            ┌──────────────┐
            │  RECORDING   │  red, bars animate (see §4)
            └──────┬───────┘
                   │ user stops recording
                   ▼
            ┌──────────────┐
            │ TRANSCRIBING │  yellow, sweep/pulse animation
            └──────┬───────┘
                   │ Whisper finishes, output exported
                   ▼
            ┌──────────────┐
            │    READY     │
            └──────────────┘
```

Transitions should be instantaneous (no fade) for tray clarity.
On error, fall back to `ready` state and surface the error elsewhere
(notification / log) — do not invent a fourth icon color.

---

## 4. Animation specs (recording + transcribing)

Animation **only applies to the live tray icon**, never to the static
executable / start-menu icon.

### Recording
- The 5 bars scale on Y around their center.
- Per-bar keyframes (loop 0.9s, ease-in-out):

```
bar 1:  0%→0.4   50%→1.0   100%→0.4
bar 2:  0%→1.0   50%→0.5   100%→1.0
bar 3:  0%→0.6   50%→0.95  100%→0.6
bar 4:  0%→0.85  50%→0.3   100%→0.85
bar 5:  0%→0.5   50%→0.85  100%→0.5
```

- The bubble itself **does not move** — keep the silhouette stable so the
  tray mark doesn't appear to jitter.

### Transcribing
- Bubble holds a slow `transform: scale()` pulse, 1.0 → 1.03 → 1.0, period 1.4s.
- Bars are static at their resting V silhouette.
- Optionally, swap the center bar for a small sweeping arc if the host can
  render SMIL — see `animated/voxto-transcribing-animated.svg`.

### Implementation notes
- The animated SVGs in `animated/` use SMIL `<animate>` elements (not CSS @keyframes)
  so they render in `<img>`, `<object>`, native macOS (`NSImage`/WebKit), and most
  cross-platform shells. CSS-based SVG animations don't run when an SVG is consumed
  via `<img>` or by native APIs — so SMIL is required for a portable handoff.
- For Electron / web-based shells: load the SVGs in `animated/` directly,
  or apply the keyframes above to the rect elements in JS.
- For native Windows tray (Win32 / WinUI): re-render the bitmap on a
  ~30 FPS timer using GDI+ (cheap — only ~1 KB per frame).
- For native macOS menu bar: use `NSStatusItem` with template images and
  toggle between three pre-rendered PNGs; macOS won't run SVG SMIL natively.

---

## 5. Asset matrix — pick the right file

| Need                         | Source file                                    | Notes                              |
| ---------------------------- | ---------------------------------------------- | ---------------------------------- |
| Build `voxto.ico`            | `png/ready/voxto-ready-{16,24,32,48,256}.png`  | Use 16 from `tray-16/` variant     |
| Build `voxto.icns` (macOS)   | `png/ready/voxto-ready-{16,32,64,128,256,512}` | Add `@2x` by using next size up    |
| Pinned taskbar icon          | `png/ready/voxto-ready-32.png`                 | Windows scales to display DPI      |
| Tray @ 16px (per state)      | `tray-16/voxto-tray-16-{state}.svg`            | Hand-tuned, do not auto-scale 64px |
| Tray @ 24px+ (per state)     | `master/voxto-master-{state}.svg`              | Auto-scales cleanly                |
| Notification overlay         | `master/voxto-master-{state}.svg` @ 64px+      | See §6                             |
| Website / README hero        | `master/voxto-master-ready.svg`                | Vector, scales infinitely          |

---

## 6. Optional: notification / overlay icon

When showing a recording-active toast or HUD, use the master mark at
64px+ with the live waveform animation. Keep at least 16px clear margin
on all sides; do not crop the bubble tail.

---

## 7. Brand do / don't

DO
- Keep bubble fill solid in one of the three state colors.
- Keep waveform bars pure white.
- Maintain the 4px safe margin inside the 64px canvas.

DON'T
- Don't add a drop shadow or outline. The mark is intentionally flat.
- Don't tilt, rotate, or skew the bubble.
- Don't recolor the bars (they're always white).
- Don't introduce a 4th state color — fall back to `ready` on error.

---

## 8. Files in this pack — checklist

- [x] `master/voxto-master-ready.svg`
- [x] `master/voxto-master-recording.svg`
- [x] `master/voxto-master-transcribing.svg`
- [x] `tray-16/voxto-tray-16-ready.svg`
- [x] `tray-16/voxto-tray-16-recording.svg`
- [x] `tray-16/voxto-tray-16-transcribing.svg`
- [x] `png/{state}/voxto-{state}-{16,24,32,48,64,128,256,512}.png` (24 files)
- [x] `animated/voxto-recording-animated.svg`
- [x] `animated/voxto-transcribing-animated.svg`
- [x] `README.md`
- [x] `SPEC.md` (this file)

End of spec.
