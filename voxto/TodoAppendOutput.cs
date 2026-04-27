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

    public async Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var path = settings.TodoFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var date = result.Timestamp.ToString("dd.MM.yyyy HH:mm");
        var line = $"- [ ] {result.FullText} @{date}{Environment.NewLine}";
        var prefix = NeedsLeadingNewLine(path)
            ? Environment.NewLine
            : string.Empty;

        await File.AppendAllTextAsync(path, prefix + line);
    }

    private static bool NeedsLeadingNewLine(string path)
    {
        if (!File.Exists(path))
            return false;

        using var stream = File.OpenRead(path);
        if (stream.Length == 0)
            return false;

        stream.Seek(-1, SeekOrigin.End);
        var lastByte = stream.ReadByte();
        return lastByte is not ('\n' or '\r');
    }
}
