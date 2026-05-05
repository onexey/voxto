using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

/// <summary>
/// Shared scaffold for output settings tabs.
/// </summary>
internal abstract class OutputSettingsPageBase<TSettings> : IOutputSettings
{
    private readonly FrameworkElement _view;
    private readonly CheckBox _enabledCheck;
    private readonly Border _contentHost;

    protected OutputSettingsPageBase()
    {
        _enabledCheck = new CheckBox
        {
            Content = "Enable this output",
            Margin = new Thickness(0, 0, 0, 20),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1F, 0x29, 0x37))
        };
        _enabledCheck.Checked += (_, _) => UpdateContentState();
        _enabledCheck.Unchecked += (_, _) => UpdateContentState();

        _contentHost = new Border
        {
            Child = BuildEditor(),
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(16),
            Background = WpfBrushes.White,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1)
        };

        _view = BuildView();
    }

    public abstract string OutputId { get; }

    public abstract string TabTitle { get; }

    public FrameworkElement View => _view;

    public void Load(AppSettings settings, OutputSettingsAdapter adapter)
    {
        _enabledCheck.IsChecked = settings.EnabledOutputs.Contains(OutputId);
        LoadSettings(adapter.Get(OutputId, CreateDefaultSettings, legacyFactory: MigrateLegacySettings));
        UpdateContentState();
    }

    public void Save(AppSettings settings, OutputSettingsAdapter adapter)
    {
        if (_enabledCheck.IsChecked == true)
        {
            if (!settings.EnabledOutputs.Contains(OutputId))
                settings.EnabledOutputs.Add(OutputId);
        }
        else
        {
            settings.EnabledOutputs.RemoveAll(id => id == OutputId);
        }

        var outputSettings = CollectSettings();
        adapter.Set(OutputId, outputSettings);
        SyncLegacySettings(settings, outputSettings);
    }

    protected abstract string Description { get; }

    protected abstract FrameworkElement BuildEditor();

    protected abstract TSettings CreateDefaultSettings();

    protected abstract TSettings MigrateLegacySettings(AppSettings settings);

    protected abstract void LoadSettings(TSettings settings);

    protected abstract TSettings CollectSettings();

    protected virtual void SyncLegacySettings(AppSettings settings, TSettings outputSettings)
    {
    }

    private FrameworkElement BuildView()
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 28)
        };

        stack.Children.Add(new TextBlock
        {
            Text = TabTitle,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x11, 0x18, 0x27))
        });

        stack.Children.Add(new TextBlock
        {
            Text = Description,
            Margin = new Thickness(0, 8, 0, 24),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80)),
            LineHeight = 22
        });

        stack.Children.Add(_enabledCheck);
        stack.Children.Add(_contentHost);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };
    }

    private void UpdateContentState()
    {
        _contentHost.IsEnabled = _enabledCheck.IsChecked == true;
        _contentHost.Opacity = _enabledCheck.IsChecked == true ? 1 : 0.55;
    }
}
