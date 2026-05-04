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
    public void DisposableResourceCache_ReusesCachedInstance_ForSameKey()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var createCount = 0;

        var first = cache.GetOrCreate("small", _ => new FakeDisposableResource(++createCount));
        var second = cache.GetOrCreate("small", _ => new FakeDisposableResource(++createCount));

        Assert.Same(first, second);
        Assert.Equal(1, createCount);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void DisposableResourceCache_ReplacesAndDisposesPreviousInstance_WhenKeyChanges()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();

        var first = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));
        var second = cache.GetOrCreate("medium", _ => new FakeDisposableResource(2));

        Assert.NotSame(first, second);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void DisposableResourceCache_Clear_DisposesCachedInstance()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var resource = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));

        cache.Clear();

        Assert.True(resource.IsDisposed);
    }

    [Fact]
    public void DisposableResourceCache_WhenReplacementCreationFails_DoesNotReturnDisposedInstance()
    {
        using var cache = new DisposableResourceCache<FakeDisposableResource>();
        var original = cache.GetOrCreate("small", _ => new FakeDisposableResource(1));

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrCreate("medium", _ => throw new InvalidOperationException("boom")));

        var replacement = cache.GetOrCreate("small", _ => new FakeDisposableResource(2));

        Assert.True(original.IsDisposed);
        Assert.NotSame(original, replacement);
        Assert.False(replacement.IsDisposed);
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

    private sealed class FakeDisposableResource(int id) : IDisposable
    {
        public int Id { get; } = id;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
