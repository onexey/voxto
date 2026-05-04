namespace Voxto;

/// <summary>
/// The three runtime states the app can be in.
/// Used to select the correct brand icon across the tray indicator and overlay bubble.
/// </summary>
public enum AppState
{
    /// <summary>Idle — ready to record. Maps to the green bubble icon.</summary>
    Ready,

    /// <summary>Capturing audio. Maps to the red bubble icon with animated bars.</summary>
    Recording,

    /// <summary>Whisper is processing or a model is downloading. Maps to the amber bubble icon.</summary>
    Transcribing,
}
