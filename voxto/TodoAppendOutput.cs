using System.IO;

namespace Voxto;

/// <summary>
/// Appends each transcription as a single Markdown task line to one shared file.
/// Format: <c>[ ] text @dd.MM.yyyy HH:mm</c>
/// </summary>
internal sealed class TodoAppendOutput : ITranscriptionOutput
{
    public string Id          => "TodoAppend";
    public string DisplayName => "Todo list (append to single file)";

    public Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var path = settings.TodoFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var date = result.Timestamp.ToString("dd.MM.yyyy HH:mm");
        var line = $"[ ] {result.FullText} @{date}{Environment.NewLine}";

        return File.AppendAllTextAsync(path, line);
    }
}
