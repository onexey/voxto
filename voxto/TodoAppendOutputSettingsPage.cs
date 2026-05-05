using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

internal sealed class TodoAppendOutputSettingsPage : OutputSettingsPageBase<TodoAppendOutputSettings>
{
    private readonly TextBox _todoFileBox = new()
    {
        Height = 38,
        VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 13
    };

    public override string OutputId => TodoAppendOutput.OutputId;

    public override string TabTitle => "Todo list";

    protected override string Description =>
        "Append each transcription as a new unchecked task inside one shared Markdown file.";

    protected override FrameworkElement BuildEditor()
    {
        var browseButton = new Button
        {
            Content = "Browse…",
            Width = 92,
            Height = 38,
            Margin = new Thickness(12, 0, 0, 0)
        };
        browseButton.Click += OnBrowseTodoFile;

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition());
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.Children.Add(_todoFileBox);
        pathRow.Children.Add(browseButton);
        Grid.SetColumn(browseButton, 1);

        var stack = new StackPanel();
        stack.Children.Add(CreateLabel("Todo file"));
        stack.Children.Add(pathRow);
        stack.Children.Add(CreateHint("Voxto adds a new “- [ ] …” line for every transcription."));
        return stack;
    }

    protected override TodoAppendOutputSettings CreateDefaultSettings() => new();

    protected override TodoAppendOutputSettings MigrateLegacySettings(AppSettings settings) => new()
    {
        TodoFilePath = string.IsNullOrWhiteSpace(settings.TodoFilePath)
            ? new TodoAppendOutputSettings().TodoFilePath
            : settings.TodoFilePath
    };

    protected override void LoadSettings(TodoAppendOutputSettings settings) =>
        _todoFileBox.Text = settings.TodoFilePath;

    protected override TodoAppendOutputSettings CollectSettings() => new()
    {
        TodoFilePath = _todoFileBox.Text.Trim()
    };

    protected override void SyncLegacySettings(AppSettings settings, TodoAppendOutputSettings outputSettings) =>
        settings.TodoFilePath = outputSettings.TodoFilePath;

    private void OnBrowseTodoFile(object? sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Choose Todo file",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            FileName = Path.GetFileName(_todoFileBox.Text),
            InitialDirectory = Path.GetDirectoryName(_todoFileBox.Text)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _todoFileBox.Text = dialog.FileName;
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
