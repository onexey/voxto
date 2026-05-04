using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Serilog;
using Application = System.Windows.Application;

namespace Voxto;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/>, the minimal tray context menu,
/// the <see cref="GlobalHotkey"/> registration, the recording overlay window, and
/// the <see cref="UpdateService"/> that checks GitHub for newer releases.
/// All user preferences are exposed through <see cref="PreferencesWindow"/>.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly NotifyIcon      _notifyIcon;
    private readonly RecorderService _recorder;
    private readonly OutputManager   _outputManager;
    private readonly UpdateService   _updateService;
    private readonly SingleOpenWindowGate<PreferencesWindow> _preferencesWindowGate = new();
    private AppSettings _settings;
    private GlobalHotkey? _hotkey;
    private OverlayWindow? _overlay;             // persistent: recording + model download
    private OverlayWindow? _notificationOverlay; // transient: auto-dismiss notifications
    private DispatcherTimer? _notificationTimer;
    private bool _isRecording;

    private ToolStripMenuItem _recordItem = null!;
    private ToolStripMenuItem _prefsItem  = null!;
    private ToolStripMenuItem _updateItem = null!;

    // ── Brand tray icons ─────────────────────────────────────────────────────
    // Icons are pre-rendered at startup using GDI+, matching the geometry of
    // tray-16/voxto-tray-16-*.svg from the icon pack bundled under Assets/icons/.
    private readonly Icon   _readyIcon;
    private readonly Icon   _transcribingIcon;
    private readonly Icon[] _recordingFrames;           // pre-baked animation cycle
    private readonly List<IntPtr> _ownedIconHandles = new(); // HICON handles; destroyed on Dispose

    // Recording bar animation: 12 frames, 0.9 s cycle → ~13 fps, 75 ms per frame.
    private const int AnimFrameCount = 12;
    private const int AnimIntervalMs = 75;

    private DispatcherTimer? _animTimer;
    private int _animFrameIdx;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates the tray icon, builds the context menu, and registers the hotkey.</summary>
    public TrayIcon()
    {
        _settings      = AppSettings.Load();
        _outputManager = new OutputManager();
        _recorder      = new RecorderService(_settings, _outputManager);
        _updateService = new UpdateService(_settings);

        _recorder.TranscriptionCompleted += OnTranscriptionCompleted;
        _recorder.TranscriptionFailed    += OnTranscriptionFailed;
        _recorder.ModelDownloadStarted   += OnModelDownloadStarted;
        _recorder.ModelDownloadFinished  += OnModelDownloadFinished;

        _updateService.UpdateAvailable += OnUpdateAvailable;
        _updateService.UpdateReady     += OnUpdateReady;
        _updateService.UpdateFailed    += OnUpdateFailed;

        // Pre-render all brand tray icons once at startup (cheap; ~1 KB each).
        _readyIcon        = MakeTrayIcon(AppState.Ready,        0.0);
        _transcribingIcon = MakeTrayIcon(AppState.Transcribing, 0.0);
        _recordingFrames  = new Icon[AnimFrameCount];
        for (int i = 0; i < AnimFrameCount; i++)
            _recordingFrames[i] = MakeTrayIcon(AppState.Recording, (double)i / AnimFrameCount);

        _notifyIcon = new NotifyIcon
        {
            Icon    = _readyIcon,
            Visible = true,
            Text    = "Voxto – Idle"
        };

        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => ToggleRecording();

        RegisterHotkey();
    }

    /// <summary>Starts the background update-check loop. Called by App after startup.</summary>
    public void StartUpdateService() => _updateService.Start();

    // ── Brand icon rendering ─────────────────────────────────────────────────

    /// <summary>
    /// Renders a 16×16 brand tray icon and returns a <see cref="Icon"/> backed by a
    /// GDI HICON. The handle is tracked in <see cref="_ownedIconHandles"/> and
    /// destroyed in <see cref="Dispose"/>.
    /// </summary>
    private Icon MakeTrayIcon(AppState state, double animPhase)
    {
        using var bmp    = RenderBrandTrayBitmap(state, animPhase);
        var       handle = bmp.GetHicon();
        _ownedIconHandles.Add(handle);
        return Icon.FromHandle(handle);
    }

    /// <summary>
    /// Draws a 16×16 brand bitmap using the exact geometry extracted from
    /// <c>Assets/icons/tray-16/voxto-tray-16-*.svg</c>:
    /// speech-bubble body (polygon approximation of the rounded path) +
    /// 3 white waveform bars at their SVG resting positions, scaled per SPEC §4
    /// keyframes when <paramref name="state"/> is <see cref="AppState.Recording"/>.
    /// </summary>
    internal static Bitmap RenderBrandTrayBitmap(AppState state, double animPhase)
    {
        const int Size = 16;
        var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.Clear(Color.Transparent);
        g.SmoothingMode   = SmoothingMode.None;    // crisp pixels at 16 px
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceOver;

        // State colour tokens (SPEC §2)
        Color fill = state switch
        {
            AppState.Recording    => Color.FromArgb(0xE5, 0x48, 0x4D), // #E5484D
            AppState.Transcribing => Color.FromArgb(0xC9, 0x9A, 0x2E), // #C99A2E
            _                     => Color.FromArgb(0x3D, 0xB3, 0x6A), // #3DB36A ready
        };

        // Bubble polygon — derived from the SVG path in tray-16/voxto-tray-16-ready.svg:
        //   M2,2 H14 a(→15,3) V10 a(→14,11) H7 L4,14 V11 H2 a(→1,10) V3 a(→2,2) z
        // Corner arcs (radius 1) are approximated by the polygon vertex pairs.
        PointF[] bubble =
        [
            new(2,  2),  new(14, 2),   // top edge
            new(15, 3),  new(15, 10),  // right edge (corner approximated)
            new(14, 11), new(7,  11),  // bottom right → tail start
            new(4,  14),               // tail tip
            new(4,  11), new(2,  11),  // tail back up + bottom left
            new(1,  10), new(1,  3),   // left edge (corner approximated)
        ];

        using (var brush = new SolidBrush(fill))
            g.FillPolygon(brush, bubble);

        // Bar scale factors, derived from SPEC §4 keyframes for the 3-bar tray variant.
        // The 3 bars map to 5-bar indices 2, 3, 4 (outer pair dropped at 16 px).
        //   bar 2: 0%→1.0  50%→0.5  100%→1.0  → 0.75 + 0.25·cos(2π·t)
        //   bar 3: 0%→0.6  50%→0.95 100%→0.6  → 0.775 − 0.175·cos(2π·t)
        //   bar 4: 0%→0.85 50%→0.3  100%→0.85 → 0.575 + 0.275·cos(2π·t)
        double leftScale, centerScale, rightScale;
        if (state == AppState.Recording)
        {
            double cosT = Math.Cos(2 * Math.PI * animPhase);
            leftScale   = 0.75  + 0.25  * cosT;
            centerScale = 0.775 - 0.175 * cosT;
            rightScale  = 0.575 + 0.275 * cosT;
        }
        else
        {
            leftScale = centerScale = rightScale = 1.0;
        }

        // Resting bar geometry (from SVG rect attributes):
        //   left   x=4  y=4  w=2  h=5 → centerY = 6.5
        //   center x=7  y=6  w=2  h=3 → centerY = 7.5
        //   right  x=10 y=4  w=2  h=5 → centerY = 6.5
        using var barBrush = new SolidBrush(Color.White);
        DrawAnimatedBar(g, barBrush, 4,  6.5f, 2, 5f, (float)leftScale);
        DrawAnimatedBar(g, barBrush, 7,  7.5f, 2, 3f, (float)centerScale);
        DrawAnimatedBar(g, barBrush, 10, 6.5f, 2, 5f, (float)rightScale);

        return bmp;
    }

    /// <summary>
    /// Draws a single waveform bar centred at <paramref name="centerY"/>,
    /// with height clamped to at least 1 px.
    /// </summary>
    private static void DrawAnimatedBar(
        Graphics g, Brush brush,
        int x, float centerY, int w, float baseH, float scale)
    {
        float h = Math.Max(1f, baseH * scale);
        float y = centerY - h / 2f;
        g.FillRectangle(brush, x, y, w, h);
    }

    // ── Tray animation (recording state) ─────────────────────────────────────

    private void StartRecordingAnimation()
    {
        _animFrameIdx = 0;
        _animTimer    = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimIntervalMs) };
        _animTimer.Tick += (_, _) =>
        {
            _animFrameIdx    = (_animFrameIdx + 1) % AnimFrameCount;
            _notifyIcon.Icon = _recordingFrames[_animFrameIdx];
        };
        _animTimer.Start();
    }

    private void StopTrayAnimation()
    {
        _animTimer?.Stop();
        _animTimer = null;
    }

    // ── Menu ─────────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _recordItem = new ToolStripMenuItem("▶  Start Recording",   null, (_, _) => ToggleRecording());
        _prefsItem  = new ToolStripMenuItem("⚙  Preferences",       null, OnPreferences);
        _updateItem = new ToolStripMenuItem("🔄  Check for Updates", null, OnCheckForUpdates)
        {
            Visible = true  // always visible; text changes to "Restart to Update" when ready
        };

        menu.Items.Add(_recordItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_prefsItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("✖  Exit", null, OnExit));

        return menu;
    }

    // ── Recording ────────────────────────────────────────────────────────────

    private async void ToggleRecording()
    {
        if (!_isRecording) await StartRecording();
        else               await StopRecording();
    }

    private async Task StartRecording()
    {
        if (_isRecording) return;
        _isRecording = true;

        DismissNotificationPill();
        SetState(recording: true);

        _overlay = new OverlayWindow("Recording…", AppState.Recording);
        _overlay.Show();

        await _recorder.StartRecordingAsync();
    }

    private async Task StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        _overlay?.Close();
        _overlay = null;

        SetState(transcribing: true);
        await _recorder.StopAndTranscribeAsync();
    }

    // ── Hotkey ───────────────────────────────────────────────────────────────

    private void RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = new GlobalHotkey(_settings.HotkeyVirtualKey, _settings.HotkeyMode);

        if (_settings.HotkeyMode == HotkeyMode.Toggle)
        {
            _hotkey.Pressed += () =>
                Application.Current.Dispatcher.Invoke(ToggleRecording);
        }
        else
        {
            _hotkey.Pressed  += () =>
                Application.Current.Dispatcher.Invoke(async () => await StartRecording());
            _hotkey.Released += () =>
                Application.Current.Dispatcher.Invoke(async () => await StopRecording());
        }
    }

    // ── Preferences ──────────────────────────────────────────────────────────

    private void OnPreferences(object? sender, EventArgs e)
    {
        var win = new PreferencesWindow(_settings, _outputManager, _updateService);
        if (!TryUseSingleOpenWindow(_preferencesWindowGate, win, BringWindowToFront))
            return;

        try
        {
            if (win.ShowDialog() == true)
            {
                _settings = win.Result;
                _recorder.UpdateSettings(_settings);
                _updateService.UpdateSettings(_settings);
                RegisterHotkey();
            }
        }
        finally
        {
            _preferencesWindowGate.Clear(win);
        }
    }

    internal static bool TryUseSingleOpenWindow<T>(
        SingleOpenWindowGate<T> windowGate,
        T newWindow,
        Action<T> activateExistingWindow) where T : class
    {
        if (windowGate.TrySet(newWindow))
            return true;

        if (windowGate.TryGetExisting(out var existingWindow))
            activateExistingWindow(existingWindow);

        return false;
    }

    internal static void BringWindowToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        var wasTopmost = window.Topmost;

        window.Activate();
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
    }

    // ── Update menu handlers ──────────────────────────────────────────────────

    private async void OnCheckForUpdates(object? sender, EventArgs e)
    {
        if (_updateService.PendingMsiPath is not null)
        {
            _updateService.ApplyUpdateAndRestart();
            return;
        }

        if (_updateService.PendingVersion is not null)
        {
            _updateItem.Enabled = false;
            _updateItem.Text    = $"⬇  Installing v{_updateService.PendingVersion}…";
            await _updateService.DownloadAndApplyPendingUpdateAsync();
            return;
        }

        _updateItem.Enabled = false;
        _updateItem.Text    = "🔄  Checking…";
        try
        {
            await _updateService.CheckForUpdatesAsync();
        }
        finally
        {
            if (_updateItem.Text == "🔄  Checking…")
            {
                _updateItem.Enabled = true;
                _updateItem.Text    = "🔄  Check for Updates";
            }
        }
    }

    // ── Update service callbacks ──────────────────────────────────────────────

    private void OnUpdateAvailable(string version)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Information("Update available: {Version}", version);
            if (_settings.AutoDownloadInstallRestartEnabled)
            {
                _updateItem.Text    = $"⬇  Downloading and installing v{version}…";
                _updateItem.Enabled = false;
                ShowNotificationPill($"Update v{version} downloading and installing…", AppState.Ready, durationMs: 5000);
                return;
            }

            _updateItem.Text    = $"↺  Install Update v{version}";
            _updateItem.Enabled = true;
            ShowNotificationPill($"Update v{version} available — click tray to install", AppState.Ready, durationMs: 8000);
        });
    }

    private void OnUpdateReady()
    {
        var version = _updateService.PendingVersion ?? "new version";
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Information("Update ready to install: {Version}", version);
            _updateItem.Text    = $"↺  Restart to Update v{version}";
            _updateItem.Enabled = true;
            ShowNotificationPill($"Update v{version} ready — click tray to install", AppState.Ready, durationMs: 8000);
        });
    }

    private void OnUpdateFailed(string error)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Warning("Update failed: {Error}", error);
            _updateItem.Enabled = true;
            _updateItem.Text    = "🔄  Check for Updates";
            ShowNotificationPill($"Update failed: {error}", AppState.Transcribing, durationMs: 5000);
        });
    }

    // ── Transcription callbacks ───────────────────────────────────────────────

    private void OnTranscriptionCompleted()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetState();
            ShowNotificationPill("Transcription saved ✅", AppState.Ready, durationMs: 4000);
        });
    }

    private void OnTranscriptionFailed(string error)
    {
        Log.Error("Transcription failed: {Error}", error);
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetState();
            ShowNotificationPill($"Failed: {error}", AppState.Recording, durationMs: 4000);
        });
    }

    private void OnModelDownloadStarted(string modelName)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _notifyIcon.Icon = _transcribingIcon;
            _notifyIcon.Text = $"Voxto – Downloading {modelName} model…";

            _overlay?.Close();
            _overlay = new OverlayWindow($"Downloading {modelName} model…", AppState.Transcribing);
            _overlay.Show();
        });
    }

    private void OnModelDownloadFinished()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _overlay = null;
            SetState(transcribing: true);
        });
    }

    // ── Exit ─────────────────────────────────────────────────────────────────

    private void OnExit(object? sender, EventArgs e)
    {
        StopTrayAnimation();
        _notificationTimer?.Stop();
        _hotkey?.Dispose();
        _recorder.Dispose();
        _updateService.Dispose();
        _overlay?.Close();
        _notificationOverlay?.Close();
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    // ── Pill notifications ────────────────────────────────────────────────────

    /// <summary>
    /// Shows a transient notification pill that auto-dismisses after <paramref name="durationMs"/>
    /// milliseconds. If another notification is already visible it is replaced immediately.
    /// Ignored while the persistent recording or download pill is up.
    /// </summary>
    private void ShowNotificationPill(string message, AppState iconState, int durationMs = 3000)
    {
        if (_isRecording || _overlay != null) return;

        DismissNotificationPill();

        _notificationOverlay = new OverlayWindow(message, iconState);
        _notificationOverlay.Show();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _notificationTimer.Tick += (_, _) => DismissNotificationPill();
        _notificationTimer.Start();
    }

    private void DismissNotificationPill()
    {
        _notificationTimer?.Stop();
        _notificationTimer = null;
        _notificationOverlay?.Close();
        _notificationOverlay = null;
    }

    // ── UI state ─────────────────────────────────────────────────────────────

    private void SetState(bool recording = false, bool transcribing = false)
    {
        StopTrayAnimation();

        if (recording)
        {
            _notifyIcon.Icon    = _recordingFrames[0];
            _notifyIcon.Text    = "Voxto – Recording…";
            _recordItem.Text    = "⏹  Stop Recording";
            _recordItem.Enabled = true;
            _prefsItem.Enabled  = false;
            StartRecordingAnimation();
        }
        else if (transcribing)
        {
            _notifyIcon.Icon    = _transcribingIcon;
            _notifyIcon.Text    = "Voxto – Transcribing…";
            _recordItem.Text    = "⏹  Stop Recording";
            _recordItem.Enabled = false;
            _prefsItem.Enabled  = true;
        }
        else
        {
            _notifyIcon.Icon    = _readyIcon;
            _notifyIcon.Text    = "Voxto – Idle";
            _recordItem.Text    = "▶  Start Recording";
            _recordItem.Enabled = true;
            _prefsItem.Enabled  = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopTrayAnimation();
        _notificationTimer?.Stop();
        _hotkey?.Dispose();
        if (_preferencesWindowGate.TryGetExisting(out var preferencesWindow) && preferencesWindow is not null)
        {
            preferencesWindow.Close();
            _preferencesWindowGate.Clear(preferencesWindow);
        }
        _overlay?.Close();
        _notificationOverlay?.Close();
        _notifyIcon.Dispose();
        _recorder.Dispose();
        _updateService.Dispose();

        // Release all GDI HICON handles owned by the brand tray icons.
        foreach (var handle in _ownedIconHandles)
            DestroyIcon(handle);
        _ownedIconHandles.Clear();
    }
}

internal sealed class SingleOpenWindowGate<T> where T : class
{
    private readonly object _syncRoot = new();
    private T? _currentWindow;

    public bool TryGetExisting([NotNullWhen(true)] out T? window)
    {
        lock (_syncRoot)
        {
            window = _currentWindow;
            return window is not null;
        }
    }

    public bool TrySet(T window)
    {
        lock (_syncRoot)
        {
            if (_currentWindow is not null)
                return false;

            _currentWindow = window;
            return true;
        }
    }

    public void Clear(T window)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_currentWindow, window))
                _currentWindow = null;
        }
    }
}
