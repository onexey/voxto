using System.IO;

namespace Voxto;

/// <summary>
/// Creates one timestamped Markdown file per transcription inside the configured output folder.
/// </summary>
internal sealed class MarkdownFileOutput : ITranscriptionOutput
{
    public IOutputSettings SettingsPage { get; } = new MarkdownFileOutputSettingsPage();

    public Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var outputSettings = new OutputSettingsAdapter(settings).Get<MarkdownFileOutputSettings>(SettingsPage.Id);

        Directory.CreateDirectory(outputSettings.OutputFolder);

        var timestamp = result.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
        var path      = Path.Combine(outputSettings.OutputFolder, $"transcription_{timestamp}.md");

        return File.WriteAllTextAsync(path,
            MarkdownFormatter.Format(result.Segments, result.Timestamp));
    }
}
