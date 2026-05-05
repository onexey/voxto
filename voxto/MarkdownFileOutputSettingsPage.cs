using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

internal sealed class MarkdownFileOutputSettingsPage : OutputSettingsPageBase<MarkdownFileOutputSettings>
{
    private readonly TextBox _outputFolderBox = new()
    {
        Height = 38,
        VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 13
    };

    public override string OutputId => MarkdownFileOutput.OutputId;

    public override string TabTitle => "Markdown files";

    protected override string Description =>
        "Create one Markdown file per recording and choose where those files are saved.";

    protected override FrameworkElement BuildEditor()
    {
        var browseButton = new Button
        {
            Content = "Browse…",
            Width = 92,
            Height = 38,
            Margin = new Thickness(12, 0, 0, 0)
        };
        browseButton.Click += OnBrowseOutputFolder;

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition());
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.Children.Add(_outputFolderBox);
        pathRow.Children.Add(browseButton);
        Grid.SetColumn(browseButton, 1);

        var stack = new StackPanel();
        stack.Children.Add(CreateLabel("Save recordings to"));
        stack.Children.Add(pathRow);
        stack.Children.Add(CreateHint("Each recording is written as its own timestamped .md file."));
        return stack;
    }

    protected override MarkdownFileOutputSettings CreateDefaultSettings() => new();

    protected override MarkdownFileOutputSettings MigrateLegacySettings(AppSettings settings) => new()
    {
        OutputFolder = string.IsNullOrWhiteSpace(settings.OutputFolder)
            ? new MarkdownFileOutputSettings().OutputFolder
            : settings.OutputFolder
    };

    protected override void LoadSettings(MarkdownFileOutputSettings settings) =>
        _outputFolderBox.Text = settings.OutputFolder;

    protected override MarkdownFileOutputSettings CollectSettings() => new()
    {
        OutputFolder = _outputFolderBox.Text.Trim()
    };

    protected override void SyncLegacySettings(AppSettings settings, MarkdownFileOutputSettings outputSettings) =>
        settings.OutputFolder = outputSettings.OutputFolder;

    private void OnBrowseOutputFolder(object? sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where transcription files are saved",
            SelectedPath = _outputFolderBox.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _outputFolderBox.Text = dialog.SelectedPath;
    }

    private static FrameworkElement CreateLabel(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 8),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x37, 0x41, 0x51))
    };

    private static FrameworkElement CreateHint(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80)),
        LineHeight = 20
    };
}
