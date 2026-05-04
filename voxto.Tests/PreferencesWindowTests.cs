using Voxto;
using Xunit;

namespace Voxto.Tests;

public class PreferencesWindowTests
{
    [Fact]
    public void FormatVersionText_IncludesRevisionWhenPresent()
    {
        var text = PreferencesWindow.FormatVersionText(new Version(2026, 4, 27, 1));

        Assert.Equal("Version 2026.4.27.1", text);
    }

    [Fact]
    public void FormatVersionText_FallsBackWhenVersionMissing()
    {
        var text = PreferencesWindow.FormatVersionText(null);

        Assert.Equal("Version 1.0", text);
    }

    [Fact]
    public void GetCursorInsertPressEnter_NullValue_ReturnsFalse()
    {
        Assert.False(PreferencesWindow.GetCursorInsertPressEnter(null));
    }

    [Fact]
    public void GetCursorInsertPressEnter_TrueValue_ReturnsTrue()
    {
        Assert.True(PreferencesWindow.GetCursorInsertPressEnter(true));
    }
}
