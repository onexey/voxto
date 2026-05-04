using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
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

    [Fact]
    public void Constructor_MovesTodoFileRowBeforeCursorInsertOption()
    {
        var layout = RunInSta(() =>
        {
            var settings = new AppSettings
            {
                EnabledOutputs = ["MarkdownFile", "TodoAppend", CursorInsertOutput.OutputId]
            };

            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(settings, new OutputManager(), updateService);

            var outputsPanel = (StackPanel)window.FindName("OutputsPanel");
            var todoFileRow = (DockPanel)window.FindName("TodoFileRow");
            var cursorInsertCheck = outputsPanel.Children
                .OfType<System.Windows.Controls.CheckBox>()
                .Single(check => Equals(check.Tag, CursorInsertOutput.OutputId));

            return (
                ParentIsOutputsPanel: ReferenceEquals(todoFileRow.Parent, outputsPanel),
                TodoFileRowIndex: outputsPanel.Children.IndexOf(todoFileRow),
                CursorInsertIndex: outputsPanel.Children.IndexOf(cursorInsertCheck));
        });

        Assert.True(layout.ParentIsOutputsPanel);
        Assert.True(layout.TodoFileRowIndex >= 0);
        Assert.True(layout.CursorInsertIndex > layout.TodoFileRowIndex);
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completed = thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(completed, "The STA test thread did not complete within 30 seconds.");
        if (capturedException is not null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();

        return result!;
    }
}
