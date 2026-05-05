namespace Voxto;

/// <summary>
/// Registry of all output settings pages shown in Preferences.
/// </summary>
internal sealed class OutputSettingsManager
{
    private readonly IReadOnlyList<IOutputSettings> _all;

    public OutputSettingsManager()
    {
        _all = new IOutputSettings[]
        {
            new MarkdownFileOutputSettingsPage(),
            new TodoAppendOutputSettingsPage(),
            new CursorInsertOutputSettingsPage()
        };
    }

    internal OutputSettingsManager(params IOutputSettings[] settingsPages)
    {
        _all = settingsPages;
    }

    public IReadOnlyList<IOutputSettings> All => _all;
}
