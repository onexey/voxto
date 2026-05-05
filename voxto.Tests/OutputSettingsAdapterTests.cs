using System.Text.Json;
using Voxto;
using Xunit;

namespace Voxto.Tests;

public class OutputSettingsAdapterTests
{
    [Fact]
    public void Get_StoredConfiguration_ReturnsTypedSettings()
    {
        var appSettings = new AppSettings();
        appSettings.OutputSettings["MarkdownFile"] = JsonSerializer.SerializeToElement(new MarkdownFileOutputSettings
        {
            OutputFolder = @"C:\Archive"
        });
        var adapter = new OutputSettingsAdapter(appSettings);

        var settings = adapter.Get<MarkdownFileOutputSettings>("MarkdownFile");

        Assert.Equal(@"C:\Archive", settings.OutputFolder);
    }

    [Fact]
    public void Get_MissingConfiguration_ReturnsDefaultSettings()
    {
        var appSettings = new AppSettings();
        var adapter = new OutputSettingsAdapter(appSettings);

        var settings = adapter.Get<MarkdownFileOutputSettings>("MarkdownFile");

        Assert.EndsWith("Voxto", settings.OutputFolder);
    }

    [Fact]
    public void Get_CorruptConfiguration_FallsBackToDefaults()
    {
        var appSettings = new AppSettings
        {
            OutputSettings =
            {
                ["CursorInsert"] = JsonDocument.Parse("{\"PressEnterAfterInsert\":\"not-a-bool\"}").RootElement.Clone()
            }
        };
        var adapter = new OutputSettingsAdapter(appSettings);

        var settings = adapter.Get<CursorInsertOutputSettings>("CursorInsert");

        Assert.False(settings.PressEnterAfterInsert);
    }

    [Fact]
    public void Set_WritesSerializedConfiguration()
    {
        var appSettings = new AppSettings();
        var adapter = new OutputSettingsAdapter(appSettings);

        adapter.Set("TodoAppend", new TodoAppendOutputSettings { TodoFilePath = @"C:\Notes\todo.md" });

        var settings = appSettings.OutputSettings["TodoAppend"].Deserialize<TodoAppendOutputSettings>();
        Assert.NotNull(settings);
        Assert.Equal(@"C:\Notes\todo.md", settings.TodoFilePath);
    }
}
