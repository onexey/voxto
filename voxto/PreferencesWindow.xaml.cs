using System.Reflection;
using System.Windows;
using System.Windows.Controls;

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
        _currentSettings = current.Clone();
        Result = _currentSettings.Clone();

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
        var settings = _currentSettings.Clone();

        settings.ModelType = ModelCombo.SelectedItem is ComboBoxItem selected
            ? (string)selected.Tag
            : "Small";

        settings.HotkeyMode = RadioToggle.IsChecked == true
            ? HotkeyMode.Toggle
            : HotkeyMode.PushToTalk;

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
