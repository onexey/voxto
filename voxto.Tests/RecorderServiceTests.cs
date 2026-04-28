using System.IO;
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
    public async Task StopAndTranscribeFileAsync_WhenTranscriptionFails_RaisesFailureAndDeletesTemporaryAudio()
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

        await service.StopAndTranscribeFileAsync(audioPath);

        Assert.Equal("Whisper exploded", failureMessage);
        Assert.False(completed);
        Assert.False(File.Exists(audioPath));
    }

    [Fact]
    public async Task StopAndTranscribeFileAsync_AfterRecoverableFailure_AllowsNextTranscriptionToSucceed()
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

        await service.StopAndTranscribeFileAsync(firstAudioPath);
        await service.StopAndTranscribeFileAsync(secondAudioPath);

        Assert.Equal(["temporary failure"], failures);
        Assert.Equal(1, completedCount);
        Assert.Equal(1, output.CallCount);
        Assert.Equal("hello world", output.LastResult?.FullText);
        Assert.False(File.Exists(firstAudioPath));
        Assert.False(File.Exists(secondAudioPath));
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
}
