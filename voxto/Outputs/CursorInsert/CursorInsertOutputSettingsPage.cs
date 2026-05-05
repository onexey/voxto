using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

internal sealed class CursorInsertOutputSettingsPage : OutputSettingsPageBase<CursorInsertOutputSettings>
{
    public CursorInsertOutputSettingsPage()
        : base(
            id: CursorInsertOutput.OutputId,
            displayName: "Insert at cursor location",
            tabTitle: "Cursor",
            description: "Send the transcription into the currently focused app without switching windows.")
    {
    }

    protected override FrameworkElement BuildEditor()
    {
        var stack = new StackPanel();
        stack.Children.Add(CreateBoundCheckBox("Press Enter after inserting text", nameof(CursorInsertOutputSettings.PressEnterAfterInsert)));
        stack.Children.Add(new TextBlock
        {
            Text = "Useful for chats, editors, AI prompts, and other text fields.",
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80)),
            LineHeight = 20
        });
        return stack;
    }
}
