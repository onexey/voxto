using System.IO;
using NAudio.Wave;
using Voxto;
using Xunit;

namespace Voxto.Tests;

public sealed class RecorderServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public RecorderServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task TranscribeFileAsync_WhenTranscriptionFails_RaisesFailureAndDeletesTemporaryAudio()
    {
        var outputManager = new OutputManager(new SpyOutput());
        var service = new RecorderService(
            new AppSettings { EnabledOutputs = ["spy"] },
            outputManager,
            _ => throw new InvalidOperationException("Whisper exploded"));
        var audioPath = CreateTempAudioFile();
        string? failureMessage = null;
        var completed = false;

        service.TranscriptionFailed += error => failureMessage = error;
        service.TranscriptionCompleted += () => completed = true;

        await service.TranscribeFileAsync(audioPath, deleteAfterTranscribe: true);

        Assert.Equal("Whisper exploded", failureMessage);
        Assert.False(completed);
        Assert.False(File.Exists(audioPath));
    }

    [Fact]
    public async Task TranscribeFileAsync_ByDefault_DoesNotDeleteAudioFile()
    {
        var service = new RecorderService(
            new AppSettings(),
            new OutputManager(new SpyOutput()),
            _ => Task.FromResult<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello world")
            ]));
        var audioPath = CreateTempAudioFile();

        await service.TranscribeFileAsync(audioPath);

        Assert.True(File.Exists(audioPath));
    }

    [Fact]
    public async Task TranscribeFileAsync_WhenOutputFails_RaisesFailureAndDoesNotComplete()
    {
        var service = new RecorderService(
            new AppSettings { EnabledOutputs = ["fail"] },
            new OutputManager(new FailingOutput()),
            _ => Task.FromResult<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello world")
            ]));
        var audioPath = CreateTempAudioFile();
        string? failureMessage = null;
        var completed = false;

        service.TranscriptionFailed += error => failureMessage = error;
        service.TranscriptionCompleted += () => completed = true;

        await service.TranscribeFileAsync(audioPath, deleteAfterTranscribe: true);

        Assert.Contains("[fail]", failureMessage);
        Assert.False(completed);
        Assert.False(File.Exists(audioPath));
    }

    [Fact]
    public async Task TranscribeFileAsync_AfterRecoverableFailure_AllowsNextTranscriptionToSucceed()
    {
        var output = new SpyOutput();
        var outputManager = new OutputManager(output);
        var attempt = 0;
        var service = new RecorderService(
            new AppSettings { EnabledOutputs = ["spy"] },
            outputManager,
            _ =>
            {
                attempt++;
                if (attempt == 1)
                    throw new InvalidOperationException("temporary failure");

                IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)> segments =
                [
                    (TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello world")
                ];

                return Task.FromResult(segments);
            });
        var firstAudioPath = CreateTempAudioFile();
        var secondAudioPath = CreateTempAudioFile();
        var failures = new List<string>();
        var completedCount = 0;

        service.TranscriptionFailed += failures.Add;
        service.TranscriptionCompleted += () => completedCount++;

        await service.TranscribeFileAsync(firstAudioPath, deleteAfterTranscribe: true);
        await service.TranscribeFileAsync(secondAudioPath, deleteAfterTranscribe: true);

        Assert.Equal(["temporary failure"], failures);
        Assert.Equal(1, completedCount);
        Assert.Equal(1, output.CallCount);
        Assert.Equal("hello world", output.LastResult?.FullText);
        Assert.False(File.Exists(firstAudioPath));
        Assert.False(File.Exists(secondAudioPath));
    }

    [Fact]
    public async Task StopAndTranscribeAsync_WaitsForRecordingStoppedBeforeDisposingRecorder()
    {
        var recorder = new FakeAudioRecorder();
        var output = new SpyOutput();
        var service = new RecorderService(
            new AppSettings { EnabledOutputs = ["spy"] },
            new OutputManager(output),
            _ => Task.FromResult<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello world")
            ]),
            () => recorder);

        await service.StartRecordingAsync();

        var stopTask = service.StopAndTranscribeAsync();

        Assert.Equal(1, recorder.StopRecordingCallCount);
        Assert.False(stopTask.IsCompleted);
        Assert.False(recorder.IsDisposed);

        recorder.RaiseRecordingStopped();
        await stopTask;

        Assert.True(recorder.IsDisposed);
        Assert.Equal(1, output.CallCount);
        Assert.False(recorder.WasDisposedBeforeRecordingStopped);
    }

    [Fact]
    public async Task StartRecordingAsync_WhenRecordingStopsUnexpectedly_RaisesFailureAndCleansUp()
    {
        var recorder = new FakeAudioRecorder();
        var service = new RecorderService(
            new AppSettings(),
            new OutputManager(new SpyOutput()),
            _ => Task.FromResult<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>([]),
            () => recorder);
        string? failureMessage = null;
        var completed = false;

        service.TranscriptionFailed += error => failureMessage = error;
        service.TranscriptionCompleted += () => completed = true;

        await service.StartRecordingAsync();
        recorder.RaiseRecordingStopped(new InvalidOperationException("waveInPrepareHeader failed"));

        Assert.Equal("waveInPrepareHeader failed", failureMessage);
        Assert.False(completed);
        Assert.True(recorder.IsDisposed);
    }

    private string CreateTempAudioFile()
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private sealed class SpyOutput : ITranscriptionOutput
    {
        public string Id => "spy";

        public string DisplayName => "spy";

        public int CallCount { get; private set; }

        public TranscriptionResult? LastResult { get; private set; }

        public Task WriteAsync(TranscriptionResult result, AppSettings settings)
        {
            CallCount++;
            LastResult = result;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOutput : ITranscriptionOutput
    {
        public string Id => "fail";

        public string DisplayName => "fail";

        public Task WriteAsync(TranscriptionResult result, AppSettings settings) =>
            throw new InvalidOperationException("Output failed");
    }

    private sealed class FakeAudioRecorder : IAudioRecorder
    {
        public WaveFormat WaveFormat { get; } = new(16000, 1);

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public bool IsDisposed { get; private set; }

        public bool HasRaisedRecordingStopped { get; private set; }

        public bool WasDisposedBeforeRecordingStopped { get; private set; }

        public int StopRecordingCallCount { get; private set; }

        public void StartRecording()
        {
        }

        public void StopRecording() => StopRecordingCallCount++;

        public void Dispose()
        {
            WasDisposedBeforeRecordingStopped = !HasRaisedRecordingStopped;
            IsDisposed = true;
        }

        public void RaiseRecordingStopped(Exception? exception = null)
        {
            HasRaisedRecordingStopped = true;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));
        }
    }
}
