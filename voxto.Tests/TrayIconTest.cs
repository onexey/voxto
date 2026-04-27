using Voxto;
using Xunit;

namespace Voxto.Tests;

public class TrayIconTest
{
    [Fact]
    public void TryUseSingleOpenWindow_WhenNoWindowIsTracked_ReturnsTrueAndTracksNewWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var newWindow = new object();
        var activationCount = 0;

        var shouldShow = TrayIcon.TryUseSingleOpenWindow(gate, newWindow, _ => activationCount++);

        Assert.True(shouldShow);
        Assert.Equal(0, activationCount);
        Assert.True(gate.TryGetExisting(out var trackedWindow));
        Assert.Same(newWindow, trackedWindow);
    }

    [Fact]
    public void TryUseSingleOpenWindow_WhenWindowIsAlreadyTracked_ActivatesExistingWindowAndReturnsFalse()
    {
        var gate = new SingleOpenWindowGate<object>();
        var existingWindow = new object();
        var secondWindow = new object();
        object? activatedWindow = null;
        var activationCount = 0;

        gate.TrySet(existingWindow);

        var shouldShow = TrayIcon.TryUseSingleOpenWindow(gate, secondWindow, window =>
        {
            activatedWindow = window;
            activationCount++;
        });

        Assert.False(shouldShow);
        Assert.Equal(1, activationCount);
        Assert.Same(existingWindow, activatedWindow);
        Assert.True(gate.TryGetExisting(out var trackedWindow));
        Assert.Same(existingWindow, trackedWindow);
    }

    [Fact]
    public void SingleOpenWindowGate_ClearCurrentWindow_RemovesTrackedWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var window = new object();

        gate.TrySet(window);
        gate.Clear(window);

        Assert.False(gate.TryGetExisting(out _));
    }

    [Fact]
    public void SingleOpenWindowGate_ClearDifferentWindow_KeepsTrackedWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var trackedWindow = new object();

        gate.TrySet(trackedWindow);
        gate.Clear(new object());

        Assert.True(gate.TryGetExisting(out var existingWindow));
        Assert.Same(trackedWindow, existingWindow);
    }

    [Fact]
    public void SingleOpenWindowGate_TrySetAfterClear_AllowsReplacementWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var firstWindow = new object();
        var replacementWindow = new object();

        gate.TrySet(firstWindow);
        gate.Clear(firstWindow);

        var added = gate.TrySet(replacementWindow);

        Assert.True(added);
        Assert.True(gate.TryGetExisting(out var existingWindow));
        Assert.Same(replacementWindow, existingWindow);
    }
}
