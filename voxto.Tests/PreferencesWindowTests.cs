using System.Runtime.ExceptionServices;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Voxto;
using Xunit;
using TabControl = System.Windows.Controls.TabControl;

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
    public void Constructor_AddsDedicatedOutputTabs()
    {
        var headers = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputSettingsManager(), updateService);
            var tabs = (TabControl)window.FindName("PreferencesTabs");

            return tabs.Items
                .OfType<TabItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)
                .ToArray();
        });

        Assert.Equal(
            ["General", "Markdown files", "Todo list", "Cursor location", "About"],
            headers);
    }

    [Fact]
    public void Constructor_LoadsOutputTabsBeforeAboutTab()
    {
        var layout = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputSettingsManager(), updateService);
            var tabs = (TabControl)window.FindName("PreferencesTabs");
            var aboutIndex = tabs.Items
                .OfType<TabItem>()
                .ToList()
                .FindIndex(item => Equals(item.Header, "About"));

            return (
                Count: tabs.Items.Count,
                AboutIndex: aboutIndex);
        });

        Assert.Equal(5, layout.Count);
        Assert.Equal(4, layout.AboutIndex);
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
