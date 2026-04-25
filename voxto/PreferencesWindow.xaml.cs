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

    // Maps output ID → its CheckBox so we can read/write checked state.
    private readonly Dictionary<string, CheckBox> _outputChecks = new();

    // Output Folder sub-row — built in code alongside the MarkdownFile checkbox.
    private TextBox  _outputFolderBox    = null!;
    private Button   _browseOutputFolder = null!;
    private UIElement _outputFolderRow   = null!;

    /// <summary>
    /// The settings produced when the user clicks Save.
    /// Only valid after <see cref="ShowDialog"/> returns <c>true</c>.
    /// </summary>
    public AppSettings Result { get; private set; }

    /// <summary>Opens the preferences window pre-populated with <paramref name="current"/> settings.</summary>
    public PreferencesWindow(AppSettings current, OutputManager outputManager)
    {
        _outputManager = outputManager;
        Result         = current; // kept as fallback; replaced on Save

        InitializeComponent();

        PopulateVersionLabel();
        LoadSettings(current);
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void PopulateVersionLabel()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver is not null
            ? $"Version {ver.Major}.{ver.Minor}.{ver.Build}"
            : "Version 1.0";
    }

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
        }

        // Todo file
        TodoFileBox.Text = s.TodoFilePath;

        // Startup
        StartupCheck.IsChecked = StartupManager.IsEnabled();

        // Sync sub-row enabled states
        UpdateMarkdownFolderRowEnabled();
        UpdateTodoFileRowEnabled();
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateMarkdownFolderRowEnabled()
    {
        var on = _outputChecks.TryGetValue("MarkdownFile", out var cb) && cb.IsChecked == true;
        _outputFolderRow.IsEnabled    = on;
    }

    private void UpdateTodoFileRowEnabled()
    {
        var on = _outputChecks.TryGetValue("TodoAppend", out var cb) && cb.IsChecked == true;
        TodoFileBox.IsEnabled   = on;
        BrowseTodoBtn.IsEnabled = on;
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

        return s;
    }

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
