using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

/// <summary>
/// Shared scaffold for output settings tabs.
/// </summary>
internal abstract class OutputSettingsPageBase<TSettings> : IOutputSettings
    where TSettings : class, new()
{
    private readonly string _id;
    private readonly string _displayName;
    private readonly string _tabTitle;
    private readonly string _description;
    private FrameworkElement? _view;
    private CheckBox? _enabledCheck;
    private Border? _contentHost;
    private TSettings _settings = new();

    protected OutputSettingsPageBase(string id, string displayName, string tabTitle, string description)
    {
        _id = id;
        _displayName = displayName;
        _tabTitle = tabTitle;
        _description = description;
    }

    public string Id => _id;

    public string DisplayName => _displayName;

    public string TabTitle => _tabTitle;

    public string Description => _description;

    public FrameworkElement View => _view ??= BuildView();

    public void Load(AppSettings settings, OutputSettingsAdapter adapter)
    {
        EnsureView();
        _enabledCheck!.IsChecked = settings.EnabledOutputs.Contains(Id);
        _settings = adapter.Get<TSettings>(Id);
        _contentHost!.DataContext = _settings;
        UpdateContentState();
    }

    public void Save(AppSettings settings, OutputSettingsAdapter adapter)
    {
        EnsureView();

        if (_enabledCheck!.IsChecked == true)
        {
            if (!settings.EnabledOutputs.Contains(Id))
                settings.EnabledOutputs.Add(Id);
        }
        else
        {
            settings.EnabledOutputs.RemoveAll(outputId => outputId == Id);
        }

        adapter.Set(Id, _settings);
    }

    protected abstract FrameworkElement BuildEditor();

    protected FrameworkElement CreateLabel(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 8),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x37, 0x41, 0x51))
    };

    protected FrameworkElement CreateHint(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80)),
        LineHeight = 20
    };

    protected TextBox CreateBoundTextBox(string propertyName) => new TextBox
    {
        Height = 38,
        VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 13,
        Margin = new Thickness(0)
    }.WithBinding(TextBox.TextProperty, propertyName);

    protected CheckBox CreateBoundCheckBox(string content, string propertyName) => new CheckBox
    {
        Content = content,
        FontSize = 13,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x37, 0x41, 0x51))
    }.WithBinding(CheckBox.IsCheckedProperty, propertyName);

    private FrameworkElement BuildView()
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
            DataContext = _settings,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(16),
            Background = WpfBrushes.White,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1)
        };

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
        if (_contentHost is null || _enabledCheck is null)
            return;

        _contentHost.IsEnabled = _enabledCheck.IsChecked == true;
        _contentHost.Opacity = _enabledCheck.IsChecked == true ? 1 : 0.55;
    }

    private void EnsureView()
    {
        _ = View;
    }
}

internal static class OutputSettingsBindingExtensions
{
    public static TControl WithBinding<TControl>(this TControl control, DependencyProperty property, string propertyName)
        where TControl : FrameworkElement
    {
        control.SetBinding(property, new Binding(propertyName)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        return control;
    }
}
