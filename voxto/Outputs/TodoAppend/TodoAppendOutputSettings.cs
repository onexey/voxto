using System.IO;

namespace Voxto;

internal sealed class TodoAppendOutputSettings
{
    public string TodoFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Voxto",
        "todo.md");
}
