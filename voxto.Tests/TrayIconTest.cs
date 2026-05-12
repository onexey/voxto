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

    [Fact]
    public void ShouldIgnoreCaptureCallback_WhenRecordingAndCallbackIsFromOlderCapture_ReturnsTrue()
    {
        var shouldIgnore = TrayIcon.ShouldIgnoreCaptureCallback(isRecording: true, activeCaptureId: 2, callbackCaptureId: 1);

        Assert.True(shouldIgnore);
    }

    [Fact]
    public void ShouldIgnoreCaptureCallback_WhenRecordingAndCallbackMatchesActiveCapture_ReturnsFalse()
    {
        var shouldIgnore = TrayIcon.ShouldIgnoreCaptureCallback(isRecording: true, activeCaptureId: 2, callbackCaptureId: 2);

        Assert.False(shouldIgnore);
    }

    [Fact]
    public void ShouldIgnoreCaptureCallback_WhenIdle_ReturnsFalse()
    {
        var shouldIgnore = TrayIcon.ShouldIgnoreCaptureCallback(isRecording: false, activeCaptureId: 2, callbackCaptureId: 1);

        Assert.False(shouldIgnore);
    }

    [Fact]
    public void ShouldShowTranscribingState_WhenCaptureIsNotSettled_ReturnsTrue()
    {
        var shouldShow = TrayIcon.ShouldShowTranscribingState(captureId: 2, lastSettledCaptureId: 1);

        Assert.True(shouldShow);
    }

    [Fact]
    public void ShouldShowTranscribingState_WhenCaptureAlreadySettled_ReturnsFalse()
    {
        var shouldShow = TrayIcon.ShouldShowTranscribingState(captureId: 2, lastSettledCaptureId: 2);

        Assert.False(shouldShow);
    }

    [Fact]
    public void ShouldShowTranscribingState_WhenCaptureIdIsZero_ReturnsFalse()
    {
        var shouldShow = TrayIcon.ShouldShowTranscribingState(captureId: 0, lastSettledCaptureId: 0);

        Assert.False(shouldShow);
    }

    [Fact]
    public void GetUiState_WhenTranscribing_ShowsStatusWithoutBlockingStartRecording()
    {
        var uiState = TrayIcon.GetUiState(transcribing: true);

        Assert.Equal("Voxto – Transcribing…", uiState.NotifyText);
        Assert.Equal("▶  Start Recording", uiState.RecordItemText);
        Assert.True(uiState.RecordItemEnabled);
        Assert.True(uiState.PreferencesEnabled);
    }

    [Fact]
    public void GetUiState_WhenRecording_ShowsStopActionAndDisablesPreferences()
    {
        var uiState = TrayIcon.GetUiState(recording: true);

        Assert.Equal("Voxto – Recording…", uiState.NotifyText);
        Assert.Equal("⏹  Stop Recording", uiState.RecordItemText);
        Assert.True(uiState.RecordItemEnabled);
        Assert.False(uiState.PreferencesEnabled);
    }
}
