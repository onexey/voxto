using System.IO;
using Xunit;
using Voxto;

namespace Voxto.Tests;

public class MarkdownFileOutputTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly MarkdownFileOutput _output = new();

    public MarkdownFileOutputTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AppSettings Settings() => new() { OutputFolder = _tempDir };

    private static TranscriptionResult Result(DateTime? timestamp = null) =>
        new()
        {
            Timestamp = timestamp ?? new DateTime(2026, 4, 25, 14, 32, 10),
            Segments  = [(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Hello world")]
        };

    // ── File creation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesFileInOutputFolder()
    {
        await _output.WriteAsync(Result(), Settings());

        var files = Directory.GetFiles(_tempDir, "*.md");
        Assert.Single(files);
    }

    [Fact]
    public async Task WriteAsync_FileNameContainsTimestamp()
    {
        var ts = new DateTime(2026, 4, 25, 14, 32, 10);
        await _output.WriteAsync(Result(ts), Settings());

        var file = Directory.GetFiles(_tempDir, "*.md").Single();
        Assert.Contains("2026-04-25_14-32-10", Path.GetFileName(file));
    }

    [Fact]
    public async Task WriteAsync_FileNameStartsWithTranscription()
    {
        await _output.WriteAsync(Result(), Settings());

        var file = Directory.GetFiles(_tempDir, "*.md").Single();
        Assert.StartsWith("transcription_", Path.GetFileName(file));
    }

    [Fact]
    public async Task WriteAsync_TwoResults_CreatesTwoFiles()
    {
        var ts1 = new DateTime(2026, 4, 25, 10, 0, 0);
        var ts2 = new DateTime(2026, 4, 25, 10, 0, 1);

        await _output.WriteAsync(Result(ts1), Settings());
        await _output.WriteAsync(Result(ts2), Settings());

        Assert.Equal(2, Directory.GetFiles(_tempDir, "*.md").Length);
    }

    // ── File content ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_FileContentContainsTranscribedText()
    {
        await _output.WriteAsync(Result(), Settings());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.md").Single());
        Assert.Contains("Hello world", content);
    }

    [Fact]
    public async Task WriteAsync_FileContentIsNotEmpty()
    {
        await _output.WriteAsync(Result(), Settings());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.md").Single());
        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public async Task WriteAsync_FileContentMatchesMarkdownFormatterOutput()
    {
        var result = Result();
        await _output.WriteAsync(result, Settings());

        var content = await File.ReadAllTextAsync(Directory.GetFiles(_tempDir, "*.md").Single());
        Assert.Equal(MarkdownFormatter.Format(result.Segments, result.Timestamp), content);
    }

    // ── Directory creation ────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_OutputFolderMissing_CreatesIt()
    {
        var settings = new AppSettings
        {
            OutputFolder = Path.Combine(_tempDir, "nested", "output")
        };

        await _output.WriteAsync(Result(), settings);

        Assert.True(Directory.Exists(settings.OutputFolder));
    }

    // ── ITranscriptionOutput contract ─────────────────────────────────────────

    [Fact]
    public void Id_IsMarkdownFile() => Assert.Equal("MarkdownFile", _output.Id);

    [Fact]
    public void DisplayName_IsNotEmpty() => Assert.False(string.IsNullOrWhiteSpace(_output.DisplayName));
}
