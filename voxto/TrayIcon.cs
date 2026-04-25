using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Threading;
using Application  = System.Windows.Application;
using WpfColor     = System.Windows.Media.Color;

namespace Voxto;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/>, the tray context menu, the
/// <see cref="GlobalHotkey"/> registration, and the recording overlay window.
/// Acts as the top-level controller that wires the UI to <see cref="RecorderService"/>.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly RecorderService _recorder;
    private AppSettings _settings;
    private GlobalHotkey? _hotkey;
    private OverlayWindow? _overlay;
    private DispatcherTimer? _notificationTimer;
    private bool _isRecording;

    private ToolStripMenuItem _startItem = null!;
    private ToolStripMenuItem _stopItem  = null!;
    private ToolStripMenuItem _modelMenu = null!;
    private ToolStripMenuItem _hotkeyModeMenu = null!;

    // ── Tray icon colours (GDI) ───────────────────────────────────────────────
    private static readonly Color IdleColor         = Color.FromArgb(34,  197, 94);  // green
    private static readonly Color RecordingColor    = Color.FromArgb(220, 38,  38);  // red
    private static readonly Color TranscribingColor = Color.FromArgb(234, 179, 8);   // amber

    // ── Pill dot colours (WPF Media) ─────────────────────────────────────────
    private static readonly WpfColor PillGreen  = WpfColor.FromRgb(34,  197, 94);   // success
    private static readonly WpfColor PillRed    = WpfColor.FromRgb(220, 38,  38);   // error
    private static readonly WpfColor PillAmber  = WpfColor.FromRgb(234, 179, 8);    // info / busy

    /// <summary>Creates the tray icon, builds the context menu, and registers the hotkey.</summary>
    public TrayIcon()
    {
        _settings = AppSettings.Load();
        _recorder = new RecorderService(_settings);
        _recorder.TranscriptionCompleted += OnTranscriptionCompleted;
        _recorder.TranscriptionFailed    += OnTranscriptionFailed;
        _recorder.ModelDownloadStarted   += OnModelDownloadStarted;
        _recorder.ModelDownloadFinished  += OnModelDownloadFinished;

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

    // ── Menu ─────────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _startItem = new ToolStripMenuItem("▶  Start Recording", null, (_, _) => ToggleRecording());
        _stopItem  = new ToolStripMenuItem("⏹  Stop Recording",  null, (_, _) => ToggleRecording())
            { Enabled = false };

        // Model submenu — Tag stores the raw model name for reliable check-mark updates.
        _modelMenu = new ToolStripMenuItem("🤖  Model");
        foreach (var name in new[] { "Tiny", "Small", "Medium", "LargeV3Turbo" })
        {
            var captured = name;
            var item = new ToolStripMenuItem(ModelLabel(captured))
            {
                Tag     = captured,
                Checked = _settings.ModelType == captured
            };
            item.Click += (_, _) => SetModel(captured);
            _modelMenu.DropDownItems.Add(item);
        }

        // Hotkey mode submenu — Tag stores the HotkeyMode enum value.
        _hotkeyModeMenu = new ToolStripMenuItem("⌨  Hotkey Mode");
        AddHotkeyModeItem("Toggle – press once to start/stop", HotkeyMode.Toggle);
        AddHotkeyModeItem("Push-to-talk – hold to record",     HotkeyMode.PushToTalk);

        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_modelMenu);
        menu.Items.Add(_hotkeyModeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("📁  Set Output Folder…", null, OnSetOutputFolder));
        menu.Items.Add(new ToolStripMenuItem("📂  Open Output Folder",  null, OnOpenFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("✖  Exit", null, OnExit));

        return menu;
    }

    private void AddHotkeyModeItem(string label, HotkeyMode mode)
    {
        var item = new ToolStripMenuItem(label)
        {
            Tag     = mode,
            Checked = _settings.HotkeyMode == mode
        };
        item.Click += (_, _) => SetHotkeyMode(mode);
        _hotkeyModeMenu.DropDownItems.Add(item);
    }

    private static string ModelLabel(string name) => name switch
    {
        "Tiny"         => "Tiny  (~75 MB, fastest)",
        "Small"        => "Small  (~244 MB, balanced)",
        "Medium"       => "Medium  (~769 MB, accurate)",
        "LargeV3Turbo" => "Large V3 Turbo  (~809 MB, best)",
        _              => name
    };

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

        // Cancel any auto-dismiss notification that may still be visible.
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

    // ── Settings ─────────────────────────────────────────────────────────────

    private void SetModel(string modelName)
    {
        _settings.ModelType = modelName;
        _settings.Save();
        _recorder.UpdateSettings(_settings);

        foreach (ToolStripMenuItem item in _modelMenu.DropDownItems)
            item.Checked = (string?)item.Tag == modelName;

        ShowNotificationPill($"Model → {modelName}", PillAmber, durationMs: 2500);
    }

    private void SetHotkeyMode(HotkeyMode mode)
    {
        _settings.HotkeyMode = mode;
        _settings.Save();

        foreach (ToolStripMenuItem item in _hotkeyModeMenu.DropDownItems)
            item.Checked = item.Tag is HotkeyMode tagMode && tagMode == mode;

        RegisterHotkey();

        var label = mode == HotkeyMode.Toggle ? "Toggle" : "Push-to-talk";
        ShowNotificationPill($"Hotkey: {label} ✓", PillAmber, durationMs: 2500);
    }

    private void OnSetOutputFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description            = "Choose where transcriptions are saved",
            SelectedPath           = _settings.OutputFolder,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _settings.OutputFolder = dialog.SelectedPath;
            _settings.Save();
            _recorder.UpdateSettings(_settings);

            ShowNotificationPill("Output folder updated ✓", PillAmber, durationMs: 2500);
        }
    }

    private void OnOpenFolder(object? sender, EventArgs e)
    {
        var folder = _settings.OutputFolder;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnTranscriptionCompleted(string outputPath)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetState();
            ShowNotificationPill("Transcription saved ✅", PillGreen, durationMs: 4000);
        });
    }

    private void OnTranscriptionFailed(string error)
    {
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

    // ── Pill notifications ────────────────────────────────────────────────────

    /// <summary>
    /// Shows the overlay pill with <paramref name="message"/> and auto-dismisses it after
    /// <paramref name="durationMs"/> milliseconds. Safe to call while recording — the call
    /// is ignored so the recording pill is never interrupted.
    /// </summary>
    private void ShowNotificationPill(string message, WpfColor dotColor, int durationMs = 3000)
    {
        // Don't disturb the persistent recording or download pill.
        if (_isRecording || _overlay != null) return;

        // Cancel any previous auto-dismiss that is still pending.
        DismissNotificationPill();

        _overlay = new OverlayWindow(message, dotColor);
        _overlay.Show();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _notificationTimer.Tick += (_, _) => DismissNotificationPill();
        _notificationTimer.Start();
    }

    private void DismissNotificationPill()
    {
        _notificationTimer?.Stop();
        _notificationTimer = null;
        _overlay?.Close();
        _overlay = null;
    }

    // ── UI state ─────────────────────────────────────────────────────────────

    private void SetState(bool recording = false, bool transcribing = false)
    {
        if (recording)
        {
            _notifyIcon.Icon = CreateIcon(RecordingColor);
            _notifyIcon.Text = "Voxto – Recording…";
            _startItem.Enabled = false;
            _stopItem.Enabled  = true;
        }
        else if (transcribing)
        {
            _notifyIcon.Icon = CreateIcon(TranscribingColor);
            _notifyIcon.Text = "Voxto – Transcribing…";
            _startItem.Enabled = false;
            _stopItem.Enabled  = false;
        }
        else
        {
            _notifyIcon.Icon = CreateIcon(IdleColor);
            _notifyIcon.Text = "Voxto – Idle";
            _startItem.Enabled = true;
            _stopItem.Enabled  = false;
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

    private void OnExit(object? sender, EventArgs e)
    {
        _notificationTimer?.Stop();
        _hotkey?.Dispose();
        _recorder.Dispose();
        _overlay?.Close();
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _notificationTimer?.Stop();
        _hotkey?.Dispose();
        _overlay?.Close();
        _notifyIcon.Dispose();
        _recorder.Dispose();
    }
}
