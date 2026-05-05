using System.IO;

namespace Voxto;

internal sealed class MarkdownFileOutputSettings
{
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Voxto");
}
