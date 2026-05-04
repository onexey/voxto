using System.IO;
using System.Text.Json;

namespace Voxto;

/// <summary>
/// Hotkey behaviour modes available to the user.
/// </summary>
public enum HotkeyMode
{
    /// <summary>Press the hotkey once to start recording; press again to stop.</summary>
    Toggle,

    /// <summary>Hold the hotkey to record; release to stop and transcribe.</summary>
    PushToTalk
}

/// <summary>
/// How often Voxto should check for updates in the background.
/// </summary>
public enum UpdateCheckInterval
{
    /// <summary>Check once per day.</summary>
    Daily,

    /// <summary>Check once per week.</summary>
    Weekly
}

/// <summary>
/// Persisted user preferences for Voxto.
/// Settings are stored as JSON in <c>%LocalAppData%\Voxto\settings.json</c>.
/// </summary>
public class AppSettings
{
    internal static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxto", "settings.json");

    /// <summary>
    /// Folder where transcription Markdown files are saved.
    /// Defaults to <c>Documents\Voxto</c>.
    /// </summary>
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Voxto");

    /// <summary>
    /// Whisper model to use for transcription.
    /// Valid values: <c>"Tiny"</c>, <c>"Small"</c>, <c>"Medium"</c>, <c>"LargeV3Turbo"</c>.
    /// Defaults to <c>"Small"</c>.
    /// </summary>
    public string ModelType { get; set; } = "Small";

    /// <summary>Hotkey behaviour: Toggle or Push-to-talk. Defaults to Toggle.</summary>
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;

    /// <summary>
    /// Virtual-key code for the global hotkey.
    /// Defaults to <c>0x78</c> (F9).
    /// </summary>
    public int HotkeyVirtualKey { get; set; } = 0x78;

    /// <summary>
    /// IDs of the <see cref="ITranscriptionOutput"/> implementations that are currently enabled.
    /// Defaults to <c>["MarkdownFile"]</c> (one file per recording).
    /// </summary>
    public List<string> EnabledOutputs { get; set; } = ["MarkdownFile"];

    /// <summary>
    /// Path of the single Markdown file used by the Todo output.
    /// Defaults to <c>Documents\Voxto\todo.md</c>.
    /// </summary>
    public string TodoFilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Voxto", "todo.md");

    /// <summary>
    /// Whether the cursor insertion output should press Enter after inserting text.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool CursorInsertPressEnter { get; set; }

    // ── Auto-update ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether Voxto should check for updates automatically in the background.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// Whether a discovered update should be downloaded, installed, and restarted
    /// automatically after a background check. Defaults to <c>false</c>.
    /// </summary>
    public bool AutoDownloadInstallRestartEnabled { get; set; }

    /// <summary>
    /// How often the background update check should run.
    /// Defaults to <see cref="UpdateCheckInterval.Weekly"/>.
    /// </summary>
    public UpdateCheckInterval UpdateCheckInterval { get; set; } = UpdateCheckInterval.Weekly;

    /// <summary>
    /// UTC timestamp of the last successful update check.
    /// <c>null</c> means never checked.
    /// </summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>
    /// Loads settings from disk, returning defaults if the file does not exist or cannot be parsed.
    /// </summary>
    /// <param name="path">
    /// Optional override path — used by unit tests to avoid touching the real settings file.
    /// Pass <c>null</c> (default) to use the standard <c>%LocalAppData%</c> location.
    /// </param>
    public static AppSettings Load(string? path = null)
    {
        var settingsPath = path ?? DefaultSettingsPath;
        try
        {
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    /// <summary>
    /// Persists the current settings to disk as indented JSON.
    /// </summary>
    /// <param name="path">
    /// Optional override path — used by unit tests to avoid touching the real settings file.
    /// Pass <c>null</c> (default) to use the standard <c>%LocalAppData%</c> location.
    /// </param>
    public void Save(string? path = null)
    {
        var settingsPath = path ?? DefaultSettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }
}
