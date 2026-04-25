namespace Voxto;

/// <summary>
/// Registry of all available <see cref="ITranscriptionOutput"/> implementations.
/// Runs every output whose <see cref="ITranscriptionOutput.Id"/> is listed in
/// <see cref="AppSettings.EnabledOutputs"/>.
/// <para>
/// To add a new output: create a class that implements <see cref="ITranscriptionOutput"/>
/// and add an instance to the array in the constructor — nothing else needs to change.
/// </para>
/// </summary>
public sealed class OutputManager
{
    private readonly IReadOnlyList<ITranscriptionOutput> _all;

    public OutputManager()
    {
        _all = new ITranscriptionOutput[]
        {
            new MarkdownFileOutput(),
            new TodoAppendOutput(),
            // ← register future outputs here
        };
    }

    /// <summary>All registered outputs, in the order they will be executed.</summary>
    public IReadOnlyList<ITranscriptionOutput> All => _all;

    /// <summary>
    /// Runs every output whose ID appears in <see cref="AppSettings.EnabledOutputs"/>.
    /// Failures from individual outputs are collected and re-thrown as an
    /// <see cref="AggregateException"/> after all outputs have been attempted,
    /// so a single failing output never silently blocks the others.
    /// </summary>
    public async Task WriteAsync(TranscriptionResult result, AppSettings settings)
    {
        var errors = new List<Exception>();

        foreach (var output in _all)
        {
            if (!settings.EnabledOutputs.Contains(output.Id))
                continue;

            try
            {
                await output.WriteAsync(result, settings);
            }
            catch (Exception ex)
            {
                errors.Add(new Exception($"[{output.DisplayName}] {ex.Message}", ex));
            }
        }

        if (errors.Count > 0)
            throw new AggregateException("One or more outputs failed.", errors);
    }
}
