using Voxto;
using Xunit;

namespace Voxto.Tests;

public class GlobalHotkeyTests
{
    [Fact]
    public void GetRegistrationModifiers_ToggleMode_UsesNoRepeatFlag()
    {
        Assert.Equal(0x4000u, GlobalHotkey.GetRegistrationModifiers(HotkeyMode.Toggle));
    }

    [Fact]
    public void GetRegistrationModifiers_PushToTalkMode_DoesNotUseModifiers()
    {
        Assert.Equal(0u, GlobalHotkey.GetRegistrationModifiers(HotkeyMode.PushToTalk));
    }
}
