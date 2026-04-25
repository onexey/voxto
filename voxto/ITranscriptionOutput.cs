namespace Voxto;

/// <summary>
/// Contract for a transcription output destination.
/// Implement this interface to add a new output type — no other plumbing required.
/// Register the instance in <see cref="OutputManager"/> and it will automatically
/// appear in the tray Output submenu and be run when enabled.
/// </summary>
public interface ITranscriptionOutput
{
    /// <summary>
    /// Stable identifier stored in <see cref="AppSettings.EnabledOutputs"/>.
    /// Must be unique across all registered outputs. Never change after shipping.
    /// </summary>
    string Id { get; }

    /// <summary>Human-readable label shown in the tray Output submenu.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Writes (or appends) the transcription to the output destination.
    /// Implementations should be idempotent where possible.
    /// </summary>
    Task WriteAsync(TranscriptionResult result, AppSettings settings);
}
