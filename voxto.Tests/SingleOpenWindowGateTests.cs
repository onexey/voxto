using Voxto;
using Xunit;

namespace Voxto.Tests;

public class SingleOpenWindowGateTests
{
    [Fact]
    public void TrySet_FirstWindow_ReturnsTrueAndStoresWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var window = new object();

        var added = gate.TrySet(window);
        var found = gate.TryGetExisting(out var existingWindow);

        Assert.True(added);
        Assert.True(found);
        Assert.Same(window, existingWindow);
    }

    [Fact]
    public void TrySet_SecondWindowWhileFirstIsOpen_ReturnsFalseAndKeepsOriginalWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var firstWindow = new object();
        var secondWindow = new object();

        gate.TrySet(firstWindow);

        var added = gate.TrySet(secondWindow);
        gate.TryGetExisting(out var existingWindow);

        Assert.False(added);
        Assert.Same(firstWindow, existingWindow);
    }

    [Fact]
    public void Clear_CurrentWindow_RemovesTrackedWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var window = new object();

        gate.TrySet(window);
        gate.Clear(window);

        Assert.False(gate.TryGetExisting(out _));
    }

    [Fact]
    public void Clear_DifferentWindow_KeepsTrackedWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var trackedWindow = new object();

        gate.TrySet(trackedWindow);
        gate.Clear(new object());
        gate.TryGetExisting(out var existingWindow);

        Assert.Same(trackedWindow, existingWindow);
    }

    [Fact]
    public void TrySet_AfterClear_AllowsTrackingReplacementWindow()
    {
        var gate = new SingleOpenWindowGate<object>();
        var firstWindow = new object();
        var replacementWindow = new object();

        gate.TrySet(firstWindow);
        gate.Clear(firstWindow);

        var added = gate.TrySet(replacementWindow);
        gate.TryGetExisting(out var existingWindow);

        Assert.True(added);
        Assert.Same(replacementWindow, existingWindow);
    }
}
