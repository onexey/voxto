using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;

namespace Voxto;

internal sealed class CursorInsertOutputSettingsPage : OutputSettingsPageBase<CursorInsertOutputSettings>
{
    private readonly CheckBox _pressEnterCheck = new()
    {
        Content = "Press Enter after inserting text",
        FontSize = 13,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(0x37, 0x41, 0x51))
    };

    public override string OutputId => CursorInsertOutput.OutputId;

    public override string TabTitle => "Cursor location";

    protected override string Description =>
        "Send the transcription into the currently focused app without switching windows.";

    protected override FrameworkElement BuildEditor()
    {
        var stack = new StackPanel();
        stack.Children.Add(_pressEnterCheck);
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

    protected override CursorInsertOutputSettings CreateDefaultSettings() => new();

    protected override CursorInsertOutputSettings MigrateLegacySettings(AppSettings settings) => new()
    {
        PressEnterAfterInsert = settings.CursorInsertPressEnter
    };

    protected override void LoadSettings(CursorInsertOutputSettings settings) =>
        _pressEnterCheck.IsChecked = settings.PressEnterAfterInsert;

    protected override CursorInsertOutputSettings CollectSettings() => new()
    {
        PressEnterAfterInsert = _pressEnterCheck.IsChecked == true
    };

    protected override void SyncLegacySettings(AppSettings settings, CursorInsertOutputSettings outputSettings) =>
        settings.CursorInsertPressEnter = outputSettings.PressEnterAfterInsert;
}
