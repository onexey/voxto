using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Voxto;

internal sealed class MarkdownFileOutputSettingsPage : OutputSettingsPageBase<MarkdownFileOutputSettings>
{
    private TextBox? _outputFolderBox;

    public MarkdownFileOutputSettingsPage()
        : base(
            id: "MarkdownFile",
            displayName: "Markdown files (one per recording)",
            tabTitle: "Markdown",
            description: "Create one Markdown file per recording and choose where those files are saved.")
    {
    }

    protected override FrameworkElement BuildEditor()
    {
        _outputFolderBox = CreateBoundTextBox(nameof(MarkdownFileOutputSettings.OutputFolder));

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

    private void OnBrowseOutputFolder(object? sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where transcription files are saved",
            SelectedPath = _outputFolderBox?.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _outputFolderBox!.Text = dialog.SelectedPath;
    }
}
