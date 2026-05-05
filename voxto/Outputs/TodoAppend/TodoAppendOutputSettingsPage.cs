using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Voxto;

internal sealed class TodoAppendOutputSettingsPage : OutputSettingsPageBase<TodoAppendOutputSettings>
{
    private TextBox? _todoFileBox;

    public TodoAppendOutputSettingsPage()
        : base(
            id: "TodoAppend",
            displayName: "Todo list (append to single file)",
            tabTitle: "Todo",
            description: "Append each transcription as a new unchecked task inside one shared Markdown file.")
    {
    }

    protected override FrameworkElement BuildEditor()
    {
        _todoFileBox = CreateBoundTextBox(nameof(TodoAppendOutputSettings.TodoFilePath));

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

    private void OnBrowseTodoFile(object? sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Choose Todo file",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            FileName = Path.GetFileName(_todoFileBox?.Text),
            InitialDirectory = Path.GetDirectoryName(_todoFileBox?.Text)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _todoFileBox!.Text = dialog.FileName;
    }
}
