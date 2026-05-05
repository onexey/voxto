# Voxto — AI Coding Instructions

## Project overview

Voxto is a Windows-only system-tray app that records microphone audio, transcribes it locally with **Whisper.net** (no data leaves the machine), and writes the result to one or more configurable output destinations.

---

## Tech stack

| Layer | Technology |
|---|---|
| UI | WPF (`net10.0-windows`) + Windows Forms interop for tray/dialogs |
| Audio capture | NAudio (`WaveInEvent`, 16 kHz mono) |
| Transcription | Whisper.net + Whisper.net.Runtime (native DLLs) |
| Logging | Serilog → daily rolling file in `%LocalAppData%\Voxto\logs\` |
| Tests | xUnit (project `voxto.Tests`) |
| CI/CD | GitHub Actions — CI Gate (tests) + Publish (CalVer, win-x64 & win-arm64 ZIP + MSI) |
| Installer | WiX Toolset v5 MSI (`installer/`) — per-user, no UAC, auto-upgrades via MajorUpgrade |
| Auto-update | `UpdateService` — GitHub Releases API, SHA-256 verified download, PowerShell trampoline |

---

## Repository layout

```
voxto/
├── voxto/                    # main application project
│   ├── App.xaml / .cs        # entry point, logging bootstrap, global exception handlers
│   ├── TrayIcon.cs           # tray icon, minimal context menu, hotkey wiring
│   ├── RecorderService.cs    # audio capture + Whisper transcription
│   ├── OutputManager.cs      # routes results to all enabled ITranscriptionOutput
│   ├── ITranscriptionOutput.cs
│   ├── IOutputSettings.cs    # contract for self-contained output settings tabs
│   ├── CursorInsertOutput.cs # inserts transcription into the active cursor location
│   ├── CursorInsertOutputSettings.cs # typed config for cursor insertion output
│   ├── CursorInsertOutputSettingsPage.cs # preferences tab for cursor insertion output
│   ├── MarkdownFileOutput.cs # one .md file per recording
│   ├── MarkdownFileOutputSettings.cs # typed config for markdown file output
│   ├── MarkdownFileOutputSettingsPage.cs # preferences tab for markdown file output
│   ├── TodoAppendOutput.cs   # appends [ ] task line to a single .md file
│   ├── TodoAppendOutputSettings.cs # typed config for todo append output
│   ├── TodoAppendOutputSettingsPage.cs # preferences tab for todo append output
│   ├── TranscriptionResult.cs
│   ├── AppSettings.cs        # JSON settings in %LocalAppData%\Voxto\settings.json
│   ├── DisposableResourceCache.cs # reusable keyed cache for disposable resources
│   ├── MarkdownFormatter.cs  # pure formatting helper (no I/O)
│   ├── GlobalHotkey.cs       # Win32 hotkey + low-level keyboard hook
│   ├── OverlayWindow.xaml/.cs # always-on-top pill notification
│   ├── OutputSettingsAdapter.cs # reads/writes typed per-output config blobs
│   ├── OutputSettingsManager.cs # registry of output settings tabs
│   ├── OutputSettingsPageBase.cs # shared UI scaffold for output settings tabs
│   ├── PreferencesWindow.xaml/.cs # modern settings UI with General, output, and About tabs
│   ├── StartupManager.cs     # HKCU run-at-startup registry helper
│   └── UpdateService.cs      # GitHub Releases update checker + downloader + installer
├── installer/                # WiX v5 MSI installer project
│   ├── installer.wixproj     # WiX MSBuild project — version and publish-dir wiring
│   └── Package.wxs           # Package definition — per-user install, shortcuts, MajorUpgrade, publish-file harvesting
├── voxto.Tests/              # xUnit test project
│   ├── AppSettingsTests.cs
│   ├── MarkdownFormatterTests.cs
│   ├── TranscriptionResultTests.cs
│   ├── TodoAppendOutputTests.cs
│   ├── MarkdownFileOutputTests.cs
│   ├── InstallerConfigurationTests.cs
│   ├── OutputManagerTests.cs
│   ├── OutputSettingsAdapterTests.cs
│   ├── OutputSettingsManagerTests.cs
│   ├── OverlayWindowTests.cs
│   ├── DisposableResourceCacheTests.cs
│   ├── CursorInsertOutputTests.cs
│   ├── RecorderServiceTests.cs
│   ├── TrayIconTest.cs
│   └── UpdateServiceTests.cs # ParseVersionFromTag, VerifySha256, IsDueForCheck
├── docs/                     # detailed documentation (one file per feature/topic)
│   ├── auto-update.md        # auto-update flow, security model, preferences
│   ├── installer.md          # MSI design, build instructions, UpgradeCode, uninstall
│   ├── outputs.md            # output targets and their configuration
│   ├── preferences.md        # preferences tabs and isolated output settings
│   └── transcription-performance.md # model reuse and hardware acceleration notes
├── .github/
│   ├── copilot-instructions.md  # ← you are here
│   ├── dependabot.yml
│   ├── scripts/
│   │   └── should-publish.ps1   # publish workflow filter for docs/.github-only commits
│   └── workflows/
│       ├── ci.yml            # CI Gate — runs all tests on PRs/push to main
│       └── publish.yml       # CalVer build + sign + MSI + SHA-256 + GitHub Release
└── README.md                 # quick-start only (see Documentation rules below)
```

---

## Non-negotiable rules

### 0 — Keep this file current
**Any change to the repository layout must be reflected in the _Repository layout_ section above.**
- File added → add it to the tree with a one-line description.
- File deleted or moved → remove or update its entry.
- New project or folder introduced → add the directory with a short explanation.

### 1 — Tests
**Every change must include a corresponding unit test.**
- New behaviour → new `[Fact]` or `[Theory]` in the matching production-class test file (for example `SomeService.cs` → `SomeServiceTest.cs`).
- Bug fix → add a regression test that would have caught the bug.
- Tests live in `voxto.Tests/` and use xUnit.
- Keep tests in the matching test class for the production type they cover instead of creating separate scenario-specific test classes.
- Test classes that touch the file system must use a temp directory and clean up in `Dispose()`.
- Do not use mocking frameworks; prefer simple hand-written test doubles (spy/fake/stub classes defined as private sealed nested classes inside the test class).

### 2 — Documentation
**Every new feature must be documented in the `docs/` folder.**
- Create a new `docs/<feature-name>.md` file for each significant feature.
- Keep it concise: what the feature does, how to use it, any configuration.

### 3 — README scope
**`README.md` is for basics only**: project description, quick-start (`dotnet run`), default hotkey, icon colours, and links to `docs/`.  
All other content (architecture details, output formats, configuration reference, CI/CD setup, etc.) belongs in `docs/`.

---

## Coding conventions

- **C# 12**, file-scoped namespaces (`namespace Voxto;`), nullable enabled, implicit usings enabled.
- **No top-level statements** — the WPF `App` class is the entry point.
- Private fields use `_camelCase`; public members use `PascalCase`.
- Async methods are suffixed `Async`. Fire-and-forget tray handlers use `async void` (acceptable for event handlers only).
- Prefer `using` declarations over `using` blocks where lifetime is clear.
- Comments should add non-obvious context, rationale, or constraints; do not restate what the code already makes clear.
- `ITranscriptionOutput` is the extension point for new output formats — implement the interface and register the instance in `OutputManager()`. No other file needs to change.

## Documentation style

- Keep documentation concise, avoid repetition, and include only content that adds useful context.

### WPF + Windows Forms interop
Both `System.Windows.Controls` and `System.Windows.Forms` are in scope. Resolve ambiguities with using aliases at the top of the file:

```csharp
using Button   = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color    = System.Windows.Media.Color;
using TextBox  = System.Windows.Controls.TextBox;
```

Tray icon GDI colours use `System.Drawing.Color`; WPF pill colours use `System.Windows.Media.Color` (aliased as `WpfColor` in `TrayIcon.cs`).

### Logging
Use Serilog's static `Log` class. Levels:
- `Log.Information` — lifecycle events (start, stop, download, transcription complete).
- `Log.Warning` — recoverable problems (no audio captured, missing file).
- `Log.Error(ex, ...)` — exceptions that result in a user-visible failure.
- `Log.Fatal(ex, ...)` — unhandled exceptions that may terminate the process.

## Self-review workflow

- After completing the initial work, always run a separate review pass on your changes.
- If your environment supports a built-in review agent or a separate AI review session, use that as the reviewer.
- If no such reviewer is available, perform a manual self-review of the changed files before finishing.
- Address actionable review comments: concrete correctness, test, security, reliability, or maintainability issues that are in scope for the current task.
- Repeat the review cycle only while actionable comments remain, and stop after at most 2 additional review passes after the initial review.
- Do not continue iterating on non-actionable preferences, speculative ideas, or minor stylistic nits once the task requirements are satisfied.

---

## Adding a new output destination

1. Create `voxto/<Name>Output.cs` implementing `ITranscriptionOutput`.
2. Register an instance in `OutputManager()`.
3. If the output needs user configuration, add a typed settings model, create an `IOutputSettings` implementation, and register it in `OutputSettingsManager`.
4. Write tests in `voxto.Tests/<Name>OutputTests.cs`.
5. Document the output in `docs/outputs.md`.
