namespace Voxto;

/// <summary>
/// Immutable snapshot of a completed transcription, passed to every
/// <see cref="ITranscriptionOutput"/> implementation.
/// </summary>
public sealed class TranscriptionResult
{
    /// <summary>When the recording was stopped and transcription began.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Individual time-stamped segments returned by Whisper.</summary>
    public IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)> Segments { get; init; } = [];

    /// <summary>
    /// All non-empty segment texts joined with a single space — useful for
    /// single-line output formats such as the todo appender.
    /// </summary>
    public string FullText =>
        string.Join(" ", Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => s.Text.Trim()));
}
