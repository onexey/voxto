using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button   = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color    = System.Windows.Media.Color;
using TextBox  = System.Windows.Controls.TextBox;

namespace Voxto;

/// <summary>
/// Full preferences dialog with a General settings tab and an About tab.
/// On confirmation, the updated <see cref="AppSettings"/> is exposed via <see cref="Result"/>.
/// </summary>
public partial class PreferencesWindow : Window
{
    private readonly OutputManager _outputManager;
    private readonly UpdateService _updateService;

    // Maps output ID → its CheckBox so we can read/write checked state.
    private readonly Dictionary<string, CheckBox> _outputChecks = new();

    // Output Folder sub-row — built in code alongside the MarkdownFile checkbox.
    private TextBox   _outputFolderBox    = null!;
    private Button    _browseOutputFolder = null!;
    private UIElement _outputFolderRow    = null!;
    private CheckBox? _cursorInsertEnterCheck;
    private UIElement? _cursorInsertOptionsRow;

    /// <summary>
    /// The settings produced when the user clicks Save.
    /// Only valid after <see cref="ShowDialog"/> returns <c>true</c>.
    /// </summary>
    public AppSettings Result { get; private set; }

    /// <summary>Opens the preferences window pre-populated with <paramref name="current"/> settings.</summary>
    public PreferencesWindow(AppSettings current, OutputManager outputManager, UpdateService updateService)
    {
        _outputManager = outputManager;
        _updateService = updateService;
        Result         = current; // kept as fallback; replaced on Save

        InitializeComponent();

        PopulateVersionLabel();
        LoadSettings(current);
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void PopulateVersionLabel()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = FormatVersionText(ver);
    }

    internal static string FormatVersionText(Version? version) =>
        version is not null ? $"Version {version}" : "Version 1.0";

    private void LoadSettings(AppSettings s)
    {
        // Model
        foreach (ComboBoxItem item in ModelCombo.Items)
        {
            if ((string)item.Tag == s.ModelType)
            {
                ModelCombo.SelectedItem = item;
                break;
            }
        }
        if (ModelCombo.SelectedItem is null)
            ModelCombo.SelectedIndex = 1; // default: Small

        // Hotkey mode
        RadioToggle.IsChecked     = s.HotkeyMode == HotkeyMode.Toggle;
        RadioPushToTalk.IsChecked = s.HotkeyMode == HotkeyMode.PushToTalk;

        // Output checkboxes — generated dynamically so new outputs appear automatically.
        // For MarkdownFile, an Output Folder sub-row is inserted immediately after.
        foreach (var output in _outputManager.All)
        {
            var cb = new CheckBox
            {
                Content   = output.DisplayName,
                Tag       = output.Id,
                IsChecked = s.EnabledOutputs.Contains(output.Id),
                Margin    = new Thickness(0, 2, 0, 2)
            };

            _outputChecks[output.Id] = cb;
            OutputsPanel.Children.Add(cb);

            if (output.Id == "MarkdownFile")
            {
                _outputFolderRow = BuildOutputFolderRow(s.OutputFolder);
                OutputsPanel.Children.Add(_outputFolderRow);
                cb.Checked   += (_, _) => UpdateMarkdownFolderRowEnabled();
                cb.Unchecked += (_, _) => UpdateMarkdownFolderRowEnabled();
            }

            if (output.Id == "TodoAppend")
            {
                cb.Checked   += (_, _) => UpdateTodoFileRowEnabled();
                cb.Unchecked += (_, _) => UpdateTodoFileRowEnabled();
            }

            if (output.Id == CursorInsertOutput.OutputId)
            {
                _cursorInsertOptionsRow = BuildCursorInsertOptionsRow(s.CursorInsertPressEnter);
                OutputsPanel.Children.Add(_cursorInsertOptionsRow);
                cb.Checked   += (_, _) => UpdateCursorInsertOptionsRowEnabled();
                cb.Unchecked += (_, _) => UpdateCursorInsertOptionsRowEnabled();
            }
        }

        // Todo file
        TodoFileBox.Text = s.TodoFilePath;

        // Startup
        StartupCheck.IsChecked = StartupManager.IsEnabled();

        // Auto-update
        AutoUpdateCheck.IsChecked       = s.AutoUpdateEnabled;
        AutoInstallUpdateCheck.IsChecked = s.AutoDownloadInstallRestartEnabled;

        foreach (ComboBoxItem item in UpdateIntervalCombo.Items)
        {
            if ((string)item.Tag == s.UpdateCheckInterval.ToString())
            {
                UpdateIntervalCombo.SelectedItem = item;
                break;
            }
        }
        if (UpdateIntervalCombo.SelectedItem is null)
            UpdateIntervalCombo.SelectedIndex = 1; // default: Weekly

        RefreshLastCheckedLabel(s);

        // Sync sub-row enabled states
        UpdateMarkdownFolderRowEnabled();
        UpdateTodoFileRowEnabled();
        UpdateCursorInsertOptionsRowEnabled();
        UpdateFrequencyRowEnabled();
    }

    /// <summary>Builds the Output Folder path row that sits beneath the MarkdownFile checkbox.</summary>
    private UIElement BuildOutputFolderRow(string initialPath)
    {
        _outputFolderBox = new TextBox
        {
            Height                   = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize                 = 11,
            ToolTip                  = "Folder where transcription Markdown files are saved",
            Text                     = initialPath
        };

        _browseOutputFolder = new Button
        {
            Content = "Browse…",
            Width   = 70,
            Height  = 24,
            Margin  = new Thickness(6, 0, 0, 0)
        };
        _browseOutputFolder.Click += OnBrowseOutputFolder;

        var label = new TextBlock
        {
            Text              = "Folder:",
            Width             = 46,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        };

        var row = new DockPanel { Margin = new Thickness(18, 4, 0, 4) };
        DockPanel.SetDock(label,               Dock.Left);
        DockPanel.SetDock(_browseOutputFolder, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(_browseOutputFolder);
        row.Children.Add(_outputFolderBox);

        return row;
    }

    private UIElement BuildCursorInsertOptionsRow(bool pressEnter)
    {
        _cursorInsertEnterCheck = new CheckBox
        {
            Content   = "Press Enter after inserting text",
            IsChecked = pressEnter,
            Margin    = new Thickness(18, 2, 0, 4)
        };

        return _cursorInsertEnterCheck;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateMarkdownFolderRowEnabled()
    {
        var on = _outputChecks.TryGetValue("MarkdownFile", out var cb) && cb.IsChecked == true;
        _outputFolderRow.IsEnabled = on;
    }

    private void UpdateTodoFileRowEnabled()
    {
        var on = _outputChecks.TryGetValue("TodoAppend", out var cb) && cb.IsChecked == true;
        TodoFileBox.IsEnabled   = on;
        BrowseTodoBtn.IsEnabled = on;
    }

    private void UpdateCursorInsertOptionsRowEnabled()
    {
        if (_cursorInsertOptionsRow is null)
            return;

        var on = _outputChecks.TryGetValue(CursorInsertOutput.OutputId, out var cb) && cb.IsChecked == true;
        _cursorInsertOptionsRow.IsEnabled = on;
    }

    private void UpdateFrequencyRowEnabled()
    {
        var isEnabled = AutoUpdateCheck.IsChecked == true;
        UpdateFrequencyRow.IsEnabled    = isEnabled;
        AutoInstallUpdateCheck.IsEnabled = isEnabled;
    }

    private void RefreshLastCheckedLabel(AppSettings s)
    {
        if (s.LastUpdateCheck is null)
        {
            LastCheckedText.Text = "Never checked";
            return;
        }

        var ago = DateTime.UtcNow - s.LastUpdateCheck.Value;
        LastCheckedText.Text = ago.TotalMinutes < 2  ? "Checked just now"
                             : ago.TotalHours   < 1  ? $"Checked {(int)ago.TotalMinutes} min ago"
                             : ago.TotalDays    < 1  ? $"Checked {(int)ago.TotalHours} h ago"
                             : ago.TotalDays    < 2  ? "Checked yesterday"
                             : $"Checked {(int)ago.TotalDays} days ago";
    }

    private AppSettings BuildSettings()
    {
        // Start from current persisted settings so unrelated fields are preserved.
        var s = AppSettings.Load();

        s.ModelType = ModelCombo.SelectedItem is ComboBoxItem selected
            ? (string)selected.Tag
            : "Small";

        s.HotkeyMode = RadioToggle.IsChecked == true
            ? HotkeyMode.Toggle
            : HotkeyMode.PushToTalk;

        s.EnabledOutputs = _outputChecks
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToList();

        s.TodoFilePath = TodoFileBox.Text.Trim();
        s.OutputFolder = _outputFolderBox.Text.Trim();
        s.CursorInsertPressEnter = GetCursorInsertPressEnter(_cursorInsertEnterCheck);

        s.AutoUpdateEnabled = AutoUpdateCheck.IsChecked == true;
        s.AutoDownloadInstallRestartEnabled = AutoInstallUpdateCheck.IsChecked == true;

        if (UpdateIntervalCombo.SelectedItem is ComboBoxItem intervalItem)
        {
            s.UpdateCheckInterval = (string)intervalItem.Tag == "Daily"
                ? UpdateCheckInterval.Daily
                : UpdateCheckInterval.Weekly;
        }

        // Preserve the last-check timestamp — don't reset it on save.

        return s;
    }

    internal static bool GetCursorInsertPressEnter(CheckBox? checkBox) =>
        checkBox?.IsChecked == true;

    // ── Browse dialogs ────────────────────────────────────────────────────────

    private void OnBrowseTodoFile(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title            = "Choose Todo file",
            Filter           = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            FileName         = Path.GetFileName(TodoFileBox.Text),
            InitialDirectory = Path.GetDirectoryName(TodoFileBox.Text)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt  = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TodoFileBox.Text = dialog.FileName;
    }

    private void OnBrowseOutputFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "Choose where transcriptions are saved",
            SelectedPath           = _outputFolderBox.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _outputFolderBox.Text = dialog.SelectedPath;
    }

    // ── Update section handlers ───────────────────────────────────────────────

    private void OnAutoUpdateChanged(object sender, RoutedEventArgs e) =>
        UpdateFrequencyRowEnabled();

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowBtn.IsEnabled = false;
        LastCheckedText.Text  = "Checking…";

        try
        {
            await _updateService.CheckForUpdatesAsync();
        }
        finally
        {
            CheckNowBtn.IsEnabled = true;
            RefreshLastCheckedLabel(AppSettings.Load());
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = BuildSettings();
        Result.Save();

        // Startup is a system-level operation; apply it here rather than in TrayIcon.
        if (StartupCheck.IsChecked == true)
            StartupManager.Enable();
        else
            StartupManager.Disable();

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── About tab ─────────────────────────────────────────────────────────────

    private void OnOpenRepo(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
