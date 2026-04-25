using System.IO;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace Voxto;

/// <summary>
/// Manages audio capture via NAudio and speech-to-text via Whisper.net.
/// Call <see cref="StartRecordingAsync"/> to begin recording and
/// <see cref="StopAndTranscribeAsync"/> to stop and produce a Markdown file.
/// </summary>
public class RecorderService : IDisposable
{
    private AppSettings _settings;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _tempWavPath;
    private bool _isRecording;

    /// <summary>Raised when transcription succeeds; argument is the output file path.</summary>
    public event Action<string>? TranscriptionCompleted;

    /// <summary>Raised when transcription fails; argument is the error message.</summary>
    public event Action<string>? TranscriptionFailed;

    /// <summary>Initialises the service with the provided settings.</summary>
    public RecorderService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Applies updated settings (e.g. model type or output folder) without restarting.</summary>
    public void UpdateSettings(AppSettings settings) => _settings = settings;

    // ── Recording ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts capturing audio from the default microphone at 16 kHz mono (the format Whisper expects).
    /// Does nothing if recording is already in progress.
    /// </summary>
    public Task StartRecordingAsync()
    {
        if (_isRecording)
            return Task.CompletedTask;

        _isRecording = true;
        Directory.CreateDirectory(_settings.OutputFolder);

        _tempWavPath = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid()}.wav");

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 1), // Whisper expects 16 kHz mono
            BufferMilliseconds = 50
        };

        _waveWriter = new WaveFileWriter(_tempWavPath, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) =>
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);

        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops recording, runs Whisper transcription on the captured audio, and writes a Markdown file.
    /// Raises either <see cref="TranscriptionCompleted"/> or <see cref="TranscriptionFailed"/> when done.
    /// </summary>
    public async Task StopAndTranscribeAsync()
    {
        _isRecording = false;

        // StopRecording() is synchronous — no DataAvailable events fire after it returns.
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _waveWriter?.Flush();
        _waveWriter?.Dispose();
        _waveWriter = null;

        if (_tempWavPath == null || !File.Exists(_tempWavPath))
        {
            TranscriptionFailed?.Invoke("No audio was captured.");
            return;
        }

        try
        {
            var outputPath = await TranscribeAsync(_tempWavPath!);
            TranscriptionCompleted?.Invoke(outputPath);
        }
        catch (Exception ex)
        {
            TranscriptionFailed?.Invoke(ex.Message);
        }
        finally
        {
            if (_tempWavPath != null && File.Exists(_tempWavPath))
                File.Delete(_tempWavPath);
            _tempWavPath = null;
        }
    }

    // ── Transcription ────────────────────────────────────────────────────────

    private async Task<string> TranscribeAsync(string wavPath)
    {
        var modelPath = await EnsureModelDownloadedAsync();

        using var factory   = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguageDetection()
            .Build();

        var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>();

        using var fileStream = File.OpenRead(wavPath);
        await foreach (var seg in processor.ProcessAsync(fileStream))
            segments.Add((seg.Start, seg.End, seg.Text.Trim()));

        var outputPath = BuildOutputPath();
        File.WriteAllText(outputPath, MarkdownFormatter.Format(segments, DateTime.Now));
        return outputPath;
    }

    private async Task<string> EnsureModelDownloadedAsync()
    {
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voxto", "models");

        Directory.CreateDirectory(modelDir);

        var ggmlType = _settings.ModelType switch
        {
            "Tiny"         => GgmlType.Tiny,
            "Medium"       => GgmlType.Medium,
            "LargeV3Turbo" => GgmlType.LargeV3Turbo,
            _              => GgmlType.Small
        };

        var fileName  = $"ggml-{_settings.ModelType.ToLowerInvariant()}.bin";
        var modelPath = Path.Combine(modelDir, fileName);

        if (!File.Exists(modelPath))
        {
            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ggmlType);
            using var fileStream  = File.OpenWrite(modelPath);
            await modelStream.CopyToAsync(fileStream);
        }

        return modelPath;
    }

    // ── Output path ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a timestamped output path inside the configured output folder.
    /// Exposed as <c>internal</c> so it can be exercised by unit tests.
    /// </summary>
    internal string BuildOutputPath()
    {
        Directory.CreateDirectory(_settings.OutputFolder);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(_settings.OutputFolder, $"transcription_{timestamp}.md");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _waveIn?.Dispose();
        _waveWriter?.Dispose();
    }
}
