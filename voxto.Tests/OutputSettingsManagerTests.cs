using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using Voxto;
using Xunit;

namespace Voxto.Tests;

public class OutputSettingsManagerTests
{
    [Fact]
    public void DefaultConstructor_RegistersBuiltInOutputSettingsPages()
    {
        var titles = RunInSta(() =>
        {
            var manager = new OutputSettingsManager();
            return manager.All.Select(page => page.TabTitle).ToArray();
        });

        Assert.Equal(
            ["Markdown files", "Todo list", "Cursor location"],
            titles);
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
