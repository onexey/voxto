using System.IO;

namespace Voxto;

/// <summary>
/// Creates one timestamped Markdown file per transcription inside
/// <see cref="AppSettings.OutputFolder"/>.
/// </summary>
internal sealed class MarkdownFileOutput : ITranscriptionOutput
{
    public string Id          => "MarkdownFile";
    public string DisplayName => "Markdown files (one per recording)";

    public Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        Directory.CreateDirectory(settings.OutputFolder);

        var timestamp = result.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
        var path      = Path.Combine(settings.OutputFolder, $"transcription_{timestamp}.md");

        return File.WriteAllTextAsync(path,
            MarkdownFormatter.Format(result.Segments, result.Timestamp));
    }
}
