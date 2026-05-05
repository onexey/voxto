using System.IO;
using System.Text;

namespace Voxto;

/// <summary>
/// Appends each transcription as a single Markdown task line to one shared file.
/// Format: <c>- [ ] text @dd.MM.yyyy HH:mm</c>
/// </summary>
internal sealed class TodoAppendOutput : ITranscriptionOutput
{
    private IOutputSettings? _settingsPage;

    public string Id => "TodoAppend";
    public string DisplayName => "Todo list (append to single file)";
    public IOutputSettings SettingsPage => _settingsPage ??= new TodoAppendOutputSettingsPage();

    public async Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var outputSettings = new OutputSettingsAdapter(settings).Get<TodoAppendOutputSettings>(Id);

        var path = outputSettings.TodoFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var date = result.Timestamp.ToString("dd.MM.yyyy HH:mm");
        var line = $"- [ ] {result.FullText} @{date}{Environment.NewLine}";
        await using var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);

        var prefix = string.Empty;
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);

            var lastByte = new byte[1];
            var bytesRead = await stream.ReadAsync(lastByte);
            if (bytesRead == 1 && lastByte[0] is not ((byte)'\n' or (byte)'\r'))
                prefix = Environment.NewLine;
        }

        stream.Seek(0, SeekOrigin.End);

        var content = Encoding.UTF8.GetBytes(prefix + line);
        await stream.WriteAsync(content);
    }
}
