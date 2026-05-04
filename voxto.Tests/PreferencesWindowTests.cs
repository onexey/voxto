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
    public void GetCursorInsertPressEnter_NullCheckBox_ReturnsFalse()
    {
        Assert.False(PreferencesWindow.GetCursorInsertPressEnter(null));
    }

    [Fact]
    public void GetCursorInsertPressEnter_CheckedCheckBox_ReturnsTrue()
    {
        var checkBox = new System.Windows.Controls.CheckBox { IsChecked = true };

        Assert.True(PreferencesWindow.GetCursorInsertPressEnter(checkBox));
    }
}
