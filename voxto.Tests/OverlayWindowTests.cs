using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Voxto;
using Xunit;

namespace Voxto.Tests;

public class OverlayWindowTests
{
    [Fact]
    public void Constructor_StartsHiddenAndUsesManualStartupLocation()
    {
        var state = RunInSta(() =>
        {
            var window = new OverlayWindow();
            return (window.Opacity, window.WindowStartupLocation);
        });

        Assert.Equal(0, state.Opacity);
        Assert.Equal(WindowStartupLocation.Manual, state.WindowStartupLocation);
    }

    [Fact]
    public void CalculateBottomRightPosition_UsesWorkAreaSizeAndMargin()
    {
        var position = OverlayWindow.CalculateBottomRightPosition(
            new Rect(100, 200, 1920, 1040),
            width: 180,
            height: 40);

        Assert.Equal(1824, position.X);
        Assert.Equal(1184, position.Y);
    }

    [Fact]
    public void CalculateBottomRightPosition_RespectsCustomMargin()
    {
        var position = OverlayWindow.CalculateBottomRightPosition(
            new Rect(0, 0, 1920, 1080),
            width: 180,
            height: 40,
            margin: 24);

        Assert.Equal(1716, position.X);
        Assert.Equal(1016, position.Y);
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
