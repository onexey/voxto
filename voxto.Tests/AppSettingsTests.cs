using System;
using System.IO;
using Xunit;
using Voxto;

namespace Voxto.Tests;

public class AppSettingsTests : IDisposable
{
    // Use a temp directory so tests never touch the real settings file.
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string TempFile => Path.Combine(_tempDir, "settings.json");

    public AppSettingsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void NewInstance_DefaultModel_IsSmall()
    {
        var settings = new AppSettings();
        Assert.Equal("Small", settings.ModelType);
    }

    [Fact]
    public void NewInstance_DefaultHotkeyMode_IsToggle()
    {
        var settings = new AppSettings();
        Assert.Equal(HotkeyMode.Toggle, settings.HotkeyMode);
    }

    [Fact]
    public void NewInstance_DefaultHotkeyVirtualKey_IsF9()
    {
        var settings = new AppSettings();
        Assert.Equal(0x78, settings.HotkeyVirtualKey);
    }

    [Fact]
    public void NewInstance_DefaultOutputFolder_EndsWithVoxto()
    {
        var settings = new AppSettings();
        Assert.EndsWith("Voxto", settings.OutputFolder, StringComparison.OrdinalIgnoreCase);
    }

    // ── Load with no file ─────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var settings = AppSettings.Load(TempFile);  // file does not exist yet
        Assert.Equal("Small", settings.ModelType);
        Assert.Equal(HotkeyMode.Toggle, settings.HotkeyMode);
    }

    // ── Save / Load round-trip ────────────────────────────────────────────────

    [Fact]
    public void SaveThenLoad_PreservesModelType()
    {
        var original = new AppSettings { ModelType = "LargeV3Turbo" };
        original.Save(TempFile);

        var loaded = AppSettings.Load(TempFile);
        Assert.Equal("LargeV3Turbo", loaded.ModelType);
    }

    [Fact]
    public void SaveThenLoad_PreservesHotkeyMode()
    {
        var original = new AppSettings { HotkeyMode = HotkeyMode.PushToTalk };
        original.Save(TempFile);

        var loaded = AppSettings.Load(TempFile);
        Assert.Equal(HotkeyMode.PushToTalk, loaded.HotkeyMode);
    }

    [Fact]
    public void SaveThenLoad_PreservesOutputFolder()
    {
        var path = @"C:\Users\Test\Recordings";
        var original = new AppSettings { OutputFolder = path };
        original.Save(TempFile);

        var loaded = AppSettings.Load(TempFile);
        Assert.Equal(path, loaded.OutputFolder);
    }

    [Fact]
    public void SaveThenLoad_PreservesCustomHotkeyVirtualKey()
    {
        var original = new AppSettings { HotkeyVirtualKey = 0x70 }; // F1
        original.Save(TempFile);

        var loaded = AppSettings.Load(TempFile);
        Assert.Equal(0x70, loaded.HotkeyVirtualKey);
    }

    [Fact]
    public void Save_CreatesFile()
    {
        var settings = new AppSettings();
        settings.Save(TempFile);
        Assert.True(File.Exists(TempFile));
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        var settings = new AppSettings();
        settings.Save(TempFile);

        var json = File.ReadAllText(TempFile);
        Assert.False(string.IsNullOrWhiteSpace(json));
        // Basic structural check — real validation is done by the round-trip tests above.
        Assert.StartsWith("{", json.TrimStart());
    }

    // ── Corrupt file ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(TempFile, "not valid json {{{{");
        var settings = AppSettings.Load(TempFile);
        Assert.Equal("Small", settings.ModelType);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(TempFile, string.Empty);
        var settings = AppSettings.Load(TempFile);
        Assert.Equal("Small", settings.ModelType);
    }
}
