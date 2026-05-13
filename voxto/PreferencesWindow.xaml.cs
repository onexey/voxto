using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Voxto;

/// <summary>
/// Full preferences dialog with a general tab, one tab per output, and an about tab.
/// On confirmation, the updated <see cref="AppSettings"/> is exposed via <see cref="Result"/>.
/// </summary>
public partial class PreferencesWindow : Window
{
    private readonly OutputManager _outputManager;
    private readonly UpdateService _updateService;
    private readonly AppSettings _currentSettings;
    private readonly List<IOutputSettings> _outputSettingsPages = [];
    private int _hotkeyVirtualKey;
    private ModifierKeys _hotkeyModifiers;
    private bool _isRecordingHotkey;

    /// <summary>
    /// The settings produced when the user clicks Save.
    /// Only valid after <see cref="ShowDialog"/> returns <c>true</c>.
    /// </summary>
    public AppSettings Result { get; private set; }

    /// <summary>Opens the preferences window pre-populated with <paramref name="current"/> settings.</summary>
    internal PreferencesWindow(AppSettings current, OutputManager outputManager, UpdateService updateService)
    {
        _outputManager = outputManager;
        _updateService = updateService;
        _currentSettings = new AppSettings(current);
        Result = new AppSettings(_currentSettings);

        InitializeComponent();

        PopulateVersionLabel();
        InsertOutputTabs();
        LoadSettings(_currentSettings);
    }

    private void PopulateVersionLabel()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = FormatVersionText(ver);
    }

    internal static string FormatVersionText(Version? version) =>
        version is not null ? $"Version {version}" : "Version 1.0";

    private void InsertOutputTabs()
    {
        foreach (var outputSettingsPage in _outputManager.AllSettingsPages)
        {
            _outputSettingsPages.Add(outputSettingsPage);
            PreferencesTabs.Items.Insert(PreferencesTabs.Items.Count - 1, new TabItem
            {
                Header = outputSettingsPage.TabTitle,
                Content = outputSettingsPage.View
            });
        }
    }

    private void LoadSettings(AppSettings settings)
    {
        foreach (ComboBoxItem item in ModelCombo.Items)
        {
            if ((string)item.Tag == settings.ModelType)
            {
                ModelCombo.SelectedItem = item;
                break;
            }
        }

        if (ModelCombo.SelectedItem is null)
            ModelCombo.SelectedIndex = 1;

        RadioToggle.IsChecked = settings.HotkeyMode == HotkeyMode.Toggle;
        RadioPushToTalk.IsChecked = settings.HotkeyMode == HotkeyMode.PushToTalk;
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        _hotkeyVirtualKey = settings.HotkeyVirtualKey;
        _hotkeyModifiers = settings.HotkeyModifiers;
        RefreshHotkeyEditor();

        AutoUpdateCheck.IsChecked = settings.AutoUpdateEnabled;
        AutoInstallUpdateCheck.IsChecked = settings.AutoDownloadInstallRestartEnabled;

        foreach (ComboBoxItem item in UpdateIntervalCombo.Items)
        {
            if ((string)item.Tag == settings.UpdateCheckInterval.ToString())
            {
                UpdateIntervalCombo.SelectedItem = item;
                break;
            }
        }

        if (UpdateIntervalCombo.SelectedItem is null)
            UpdateIntervalCombo.SelectedIndex = 1;

        var adapter = new OutputSettingsAdapter(settings);
        foreach (var outputSettingsPage in _outputSettingsPages)
            outputSettingsPage.Load(settings, adapter);

        RefreshLastCheckedLabel(settings);
        UpdateFrequencyRowEnabled();
    }

    private void RefreshLastCheckedLabel(AppSettings settings)
    {
        if (settings.LastUpdateCheck is null)
        {
            LastCheckedText.Text = "Never checked";
            return;
        }

        var ago = DateTime.UtcNow - settings.LastUpdateCheck.Value;
        LastCheckedText.Text = ago.TotalMinutes < 2 ? "Checked just now"
                             : ago.TotalHours < 1 ? $"Checked {(int)ago.TotalMinutes} min ago"
                             : ago.TotalDays < 1 ? $"Checked {(int)ago.TotalHours} h ago"
                             : ago.TotalDays < 2 ? "Checked yesterday"
                             : $"Checked {(int)ago.TotalDays} days ago";
    }

    private AppSettings BuildSettings()
    {
        var settings = new AppSettings(_currentSettings);

        settings.ModelType = ModelCombo.SelectedItem is ComboBoxItem selected
            ? (string)selected.Tag
            : "Small";

        settings.HotkeyMode = RadioToggle.IsChecked == true
            ? HotkeyMode.Toggle
            : HotkeyMode.PushToTalk;
        settings.HotkeyVirtualKey = _hotkeyVirtualKey;
        settings.HotkeyModifiers = _hotkeyModifiers;

        settings.AutoUpdateEnabled = AutoUpdateCheck.IsChecked == true;
        settings.AutoDownloadInstallRestartEnabled = AutoInstallUpdateCheck.IsChecked == true;

        if (UpdateIntervalCombo.SelectedItem is ComboBoxItem intervalItem)
        {
            settings.UpdateCheckInterval = (string)intervalItem.Tag == "Daily"
                ? UpdateCheckInterval.Daily
                : UpdateCheckInterval.Weekly;
        }

        var adapter = new OutputSettingsAdapter(settings);
        foreach (var outputSettingsPage in _outputSettingsPages)
            outputSettingsPage.Save(settings, adapter);

        return settings;
    }

    private void UpdateFrequencyRowEnabled()
    {
        var isEnabled = AutoUpdateCheck.IsChecked == true;
        UpdateFrequencyRow.IsEnabled = isEnabled;
        AutoInstallUpdateCheck.IsEnabled = isEnabled;
    }

    private void OnAutoUpdateChanged(object sender, RoutedEventArgs e) =>
        UpdateFrequencyRowEnabled();

    private void RefreshHotkeyEditor()
    {
        HotkeyTextBox.Text = GlobalHotkey.FormatShortcut(_hotkeyModifiers, _hotkeyVirtualKey);
        RecordHotkeyButton.Content = _isRecordingHotkey ? "Cancel recording" : "Record shortcut";
        HotkeyRecordingHintText.Visibility = _isRecordingHotkey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRecordHotkey(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = !_isRecordingHotkey;
        RefreshHotkeyEditor();

        if (_isRecordingHotkey)
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
        }
    }

    private void OnPreviewHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (GlobalHotkey.TryBuildShortcut(key, Keyboard.Modifiers, out var virtualKey, out var modifiers))
        {
            _hotkeyVirtualKey = virtualKey;
            _hotkeyModifiers = modifiers;
            _isRecordingHotkey = false;
            RefreshHotkeyEditor();
        }

        e.Handled = true;
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowBtn.IsEnabled = false;
        LastCheckedText.Text = "Checking…";

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

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        Result = BuildSettings();
        Result.Save();

        if (StartupCheck.IsChecked == true)
            StartupManager.Enable();
        else
            StartupManager.Disable();

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnOpenRepo(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
