using System.IO;
using System.Drawing;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Application  = System.Windows.Application;
using WpfColor     = System.Windows.Media.Color;

namespace Voxto;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/>, the minimal tray context menu,
/// the <see cref="GlobalHotkey"/> registration, the recording overlay window, and
/// the <see cref="UpdateService"/> that checks GitHub for newer releases.
/// All user preferences are exposed through <see cref="PreferencesWindow"/>.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly NotifyIcon     _notifyIcon;
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
    private ToolStripMenuItem _updateItem = null!; // update menu item; visibility is managed when the menu is built/updated

    // ── Tray icon colours (GDI) ───────────────────────────────────────────────
    private static readonly Color IdleColor         = Color.FromArgb(34,  197, 94);  // green
    private static readonly Color RecordingColor    = Color.FromArgb(220, 38,  38);  // red
    private static readonly Color TranscribingColor = Color.FromArgb(234, 179, 8);   // amber

    // ── Pill dot colours (WPF Media) ─────────────────────────────────────────
    private static readonly WpfColor PillGreen  = WpfColor.FromRgb(34,  197, 94);
    private static readonly WpfColor PillRed    = WpfColor.FromRgb(220, 38,  38);
    private static readonly WpfColor PillAmber  = WpfColor.FromRgb(234, 179, 8);
    private static readonly WpfColor PillBlue   = WpfColor.FromRgb(59,  130, 246);

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

        _notifyIcon = new NotifyIcon
        {
            Icon    = CreateIcon(IdleColor),
            Visible = true,
            Text    = "Voxto – Idle"
        };

        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => ToggleRecording();

        RegisterHotkey();
    }

    /// <summary>Starts the background update-check loop. Called by App after startup.</summary>
    public void StartUpdateService() => _updateService.Start();

    // ── Menu ─────────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _recordItem = new ToolStripMenuItem("▶  Start Recording",  null, (_, _) => ToggleRecording());
        _prefsItem  = new ToolStripMenuItem("⚙  Preferences",      null, OnPreferences);
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

        _overlay = new OverlayWindow();
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
        // If an update is already downloaded and ready, apply it immediately.
        if (_updateService.PendingMsiPath is not null)
        {
            _updateService.ApplyUpdateAndRestart();
            return;
        }

        // Otherwise kick off an on-demand check.
        _updateItem.Enabled = false;
        _updateItem.Text    = "🔄  Checking…";
        try
        {
            await _updateService.CheckForUpdatesAsync();
        }
        finally
        {
            // Re-enable. Text will be updated by the event handlers if an update was found.
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
            _updateItem.Text    = $"⬇  Downloading v{version}…";
            _updateItem.Enabled = false;
            ShowNotificationPill($"Update v{version} downloading…", PillBlue, durationMs: 5000);
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
            // Persistent pill — dismissed only when user acts or restarts.
            ShowNotificationPill($"Update v{version} ready — click tray to install", PillBlue, durationMs: 8000);
        });
    }

    private void OnUpdateFailed(string error)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Warning("Update failed: {Error}", error);
            _updateItem.Enabled = true;
            _updateItem.Text    = "🔄  Check for Updates";
            ShowNotificationPill($"Update failed: {error}", PillAmber, durationMs: 5000);
        });
    }

    // ── Transcription callbacks ───────────────────────────────────────────────

    private void OnTranscriptionCompleted()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetState();
            ShowNotificationPill("Transcription saved ✅", PillGreen, durationMs: 4000);
        });
    }

    private void OnTranscriptionFailed(string error)
    {
        Log.Error("Transcription failed: {Error}", error);
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetState();
            ShowNotificationPill($"Failed: {error}", PillRed, durationMs: 4000);
        });
    }

    private void OnModelDownloadStarted(string modelName)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _notifyIcon.Icon = CreateIcon(TranscribingColor);
            _notifyIcon.Text = $"Voxto – Downloading {modelName} model…";

            _overlay?.Close();
            _overlay = new OverlayWindow($"Downloading {modelName} model…", PillAmber);
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
    private void ShowNotificationPill(string message, WpfColor dotColor, int durationMs = 3000)
    {
        if (_isRecording || _overlay != null) return;

        DismissNotificationPill();

        _notificationOverlay = new OverlayWindow(message, dotColor);
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
        if (recording)
        {
            _notifyIcon.Icon    = CreateIcon(RecordingColor);
            _notifyIcon.Text    = "Voxto – Recording…";
            _recordItem.Text    = "⏹  Stop Recording";
            _recordItem.Enabled = true;
            _prefsItem.Enabled  = false;
        }
        else if (transcribing)
        {
            _notifyIcon.Icon    = CreateIcon(TranscribingColor);
            _notifyIcon.Text    = "Voxto – Transcribing…";
            _recordItem.Text    = "⏹  Stop Recording";
            _recordItem.Enabled = false;
            _prefsItem.Enabled  = true;
        }
        else
        {
            _notifyIcon.Icon    = CreateIcon(IdleColor);
            _notifyIcon.Text    = "Voxto – Idle";
            _recordItem.Text    = "▶  Start Recording";
            _recordItem.Enabled = true;
            _prefsItem.Enabled  = true;
        }
    }

    private static Icon CreateIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 13, 13);
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
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
