using System.IO;
using System.Text.Json;
using Xunit;
using Voxto;

namespace Voxto.Tests;

public class TodoAppendOutputTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string TodoFile => Path.Combine(_tempDir, "todo.md");

    private readonly TodoAppendOutput _output = new();

    public TodoAppendOutputTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AppSettings Settings() => new()
    {
        OutputSettings =
        {
            ["TodoAppend"] = JsonSerializer.SerializeToElement(new TodoAppendOutputSettings
            {
                TodoFilePath = TodoFile
            })
        }
    };

    private static TranscriptionResult Result(string text, DateTime? timestamp = null) =>
        new()
        {
            Timestamp = timestamp ?? new DateTime(2026, 4, 25, 17, 32, 0),
            Segments  = [(TimeSpan.Zero, TimeSpan.FromSeconds(3), text)]
        };

    // ── Format ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ProducesCorrectLineFormat()
    {
        await _output.WriteAsync(Result("Buy milk"), Settings());

        var line = (await File.ReadAllTextAsync(TodoFile)).TrimEnd();
        Assert.Equal("- [ ] Buy milk @25.04.2026 17:32", line);
    }

    [Fact]
    public async Task WriteAsync_LineStartsWithCheckboxSyntax()
    {
        await _output.WriteAsync(Result("Some task"), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.StartsWith("- [ ]", content);
    }

    [Fact]
    public async Task WriteAsync_DateUsesExpectedFormat()
    {
        var ts = new DateTime(2026, 12, 1, 9, 5, 0);
        await _output.WriteAsync(Result("Task", ts), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.Contains("@01.12.2026 09:05", content);
    }

    // ── Append behaviour ──────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CalledTwice_AppendsBothLines()
    {
        var settings = Settings();
        await _output.WriteAsync(Result("First task"),  settings);
        await _output.WriteAsync(Result("Second task"), settings);

        var lines = (await File.ReadAllTextAsync(TodoFile))
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("First task",  lines[0]);
        Assert.Contains("Second task", lines[1]);
    }

    [Fact]
    public async Task WriteAsync_MultipleSegments_UsesTrimmedFullText()
    {
        var result = new TranscriptionResult
        {
            Timestamp = new DateTime(2026, 4, 25, 17, 32, 0),
            Segments =
            new (TimeSpan Start, TimeSpan End, string Text)[]
            {
                (TimeSpan.Zero, TimeSpan.FromSeconds(1), "  First  "),
                (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), " "),
                (TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Second")
            }
        };

        await _output.WriteAsync(result, Settings());

        var line = (await File.ReadAllTextAsync(TodoFile)).TrimEnd();
        Assert.Equal("- [ ] First Second @25.04.2026 17:32", line);
    }

    [Fact]
    public async Task WriteAsync_ExistingContent_IsNotOverwritten()
    {
        await File.WriteAllTextAsync(TodoFile, $"- [ ] Pre-existing task @01.01.2026 10:00{Environment.NewLine}");
        await _output.WriteAsync(Result("New task"), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.Contains("Pre-existing task", content);
        Assert.Contains("New task",          content);
    }

    [Fact]
    public async Task WriteAsync_ExistingFileWithoutTrailingNewLine_PrependsNewLineBeforeAppending()
    {
        await File.WriteAllTextAsync(TodoFile, "- [ ] Pre-existing task @01.01.2026 10:00");

        await _output.WriteAsync(Result("New task"), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.Equal($"- [ ] Pre-existing task @01.01.2026 10:00{Environment.NewLine}- [ ] New task @25.04.2026 17:32{Environment.NewLine}", content);
    }

    [Fact]
    public async Task WriteAsync_ExistingFileWithTrailingNewLine_AppendsDirectly()
    {
        await File.WriteAllTextAsync(TodoFile, $"- [ ] Pre-existing task @01.01.2026 10:00{Environment.NewLine}");

        await _output.WriteAsync(Result("New task"), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.Equal($"- [ ] Pre-existing task @01.01.2026 10:00{Environment.NewLine}- [ ] New task @25.04.2026 17:32{Environment.NewLine}", content);
    }

    [Fact]
    public async Task WriteAsync_ExistingEmptyFile_DoesNotAddLeadingBlankLine()
    {
        await File.WriteAllTextAsync(TodoFile, string.Empty);

        await _output.WriteAsync(Result("New task"), Settings());

        var content = await File.ReadAllTextAsync(TodoFile);
        Assert.Equal($"- [ ] New task @25.04.2026 17:32{Environment.NewLine}", content);
    }

    // ── File / directory creation ─────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_FileDoesNotExist_CreatesIt()
    {
        Assert.False(File.Exists(TodoFile));
        await _output.WriteAsync(Result("Task"), Settings());
        Assert.True(File.Exists(TodoFile));
    }

    [Fact]
    public async Task WriteAsync_DirectoryDoesNotExist_CreatesIt()
    {
        var settings = new AppSettings
        {
            OutputSettings =
            {
                ["TodoAppend"] = JsonSerializer.SerializeToElement(new TodoAppendOutputSettings
                {
                    TodoFilePath = Path.Combine(_tempDir, "nested", "deep", "todo.md")
                })
            }
        };
        await _output.WriteAsync(Result("Task"), settings);
        Assert.True(File.Exists(Path.Combine(_tempDir, "nested", "deep", "todo.md")));
    }

    [Fact]
    public async Task WriteAsync_UsesStoredOutputSettingsWhenPresent()
    {
        var configuredFile = Path.Combine(_tempDir, "configured", "todo.md");
        var settings = new AppSettings
        {
            OutputSettings =
            {
                ["TodoAppend"] = JsonSerializer.SerializeToElement(new TodoAppendOutputSettings
                {
                    TodoFilePath = configuredFile
                })
            }
        };

        await _output.WriteAsync(Result("Task"), settings);

        Assert.True(File.Exists(configuredFile));
    }

    // ── Settings page metadata ────────────────────────────────────────────────

    [Fact]
    public void SettingsPage_IdMatchesOutputId()
    {
        var pageId = RunInSta(() => _output.SettingsPage.Id);
        Assert.Equal("TodoAppend", pageId);
    }

    [Fact]
    public void SettingsPage_DisplayName_IsNotEmpty()
    {
        var displayName = RunInSta(() => _output.SettingsPage.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(displayName));
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();

        return result!;
    }
}
