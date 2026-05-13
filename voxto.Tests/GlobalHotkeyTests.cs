using System.Windows.Input;
using Voxto;
using Xunit;

namespace Voxto.Tests;

public class GlobalHotkeyTests
{
    [Fact]
    public void GetRegistrationModifiers_ToggleMode_UsesNoRepeatFlag()
    {
        Assert.Equal(0x4000u, GlobalHotkey.GetRegistrationModifiers(HotkeyMode.Toggle, ModifierKeys.None));
    }

    [Fact]
    public void GetRegistrationModifiers_PushToTalkMode_UsesConfiguredModifierFlags()
    {
        Assert.Equal(0x0006u, GlobalHotkey.GetRegistrationModifiers(HotkeyMode.PushToTalk, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void TryBuildShortcut_Combination_ReturnsVirtualKeyAndModifiers()
    {
        var captured = GlobalHotkey.TryBuildShortcut(Key.R, ModifierKeys.Control | ModifierKeys.Shift, out var virtualKey, out var modifiers);

        Assert.True(captured);
        Assert.Equal(KeyInterop.VirtualKeyFromKey(Key.R), virtualKey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, modifiers);
    }

    [Fact]
    public void TryBuildShortcut_ModifierOnly_ReturnsFalse()
    {
        var captured = GlobalHotkey.TryBuildShortcut(Key.LeftCtrl, ModifierKeys.Control, out var virtualKey, out var modifiers);

        Assert.False(captured);
        Assert.Equal(0, virtualKey);
        Assert.Equal(ModifierKeys.Control, modifiers);
    }

    [Fact]
    public void FormatShortcut_FormatsCombinationText()
    {
        var shortcut = GlobalHotkey.FormatShortcut(ModifierKeys.Control | ModifierKeys.Shift, KeyInterop.VirtualKeyFromKey(Key.R));

        Assert.Equal("Ctrl+Shift+R", shortcut);
    }

    [Fact]
    public void FormatShortcut_UnknownVirtualKey_UsesVirtualKeyFallback()
    {
        var shortcut = GlobalHotkey.FormatShortcut(ModifierKeys.Control, 0x07);

        Assert.Equal("Ctrl+VK 0x07", shortcut);
    }
}
