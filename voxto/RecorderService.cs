using System.IO;
using NAudio.Wave;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;

namespace Voxto;

/// <summary>
/// Manages audio capture via NAudio and speech-to-text via Whisper.net.
/// After transcription completes the result is forwarded to every enabled
/// <see cref="ITranscriptionOutput"/> via <see cref="OutputManager"/>.
/// </summary>
public class RecorderService : IDisposable
{
    private AppSettings _settings;
    private readonly OutputManager _outputManager;
    private readonly Func<string, Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>> _transcribeSegmentsAsync;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _tempWavPath;
    private bool _isRecording;

    /// <summary>Raised when all enabled outputs have been written successfully.</summary>
    public event Action? TranscriptionCompleted;

    /// <summary>Raised when transcription or any output fails; argument is the error message.</summary>
    public event Action<string>? TranscriptionFailed;

    /// <summary>
    /// Raised just before a model file is downloaded for the first time.
    /// The argument is the human-readable model name (e.g. <c>"Small"</c>).
    /// Not raised on subsequent runs when the cached file already exists.
    /// </summary>
    public event Action<string>? ModelDownloadStarted;

    /// <summary>Raised once the model file has finished downloading.</summary>
    public event Action? ModelDownloadFinished;

    /// <summary>Initialises the service with the provided settings and output pipeline.</summary>
    public RecorderService(AppSettings settings, OutputManager outputManager)
        : this(settings, outputManager, null)
    {
    }

    internal RecorderService(
        AppSettings settings,
        OutputManager outputManager,
        Func<string, Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>>? transcribeSegmentsAsync)
    {
        _settings      = settings;
        _outputManager = outputManager;
        _transcribeSegmentsAsync = transcribeSegmentsAsync ?? TranscribeSegmentsAsync;
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

        Log.Information("Recording started (model={Model})", _settings.ModelType);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops recording, runs Whisper transcription, and forwards the result to all enabled outputs.
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

        Log.Information("Recording stopped — starting transcription");

        if (_tempWavPath == null || !File.Exists(_tempWavPath))
        {
            Log.Warning("Transcription skipped: no audio was captured");
            TranscriptionFailed?.Invoke("No audio was captured.");
            return;
        }

        await StopAndTranscribeFileAsync(_tempWavPath);
    }

    internal async Task StopAndTranscribeFileAsync(string wavPath)
    {
        try
        {
            await TranscribeAsync(wavPath);
            TranscriptionCompleted?.Invoke();
        }
        catch (AggregateException aex)
        {
            var msg = string.Join("; ", aex.InnerExceptions.Select(e => e.Message));
            Log.Error(aex, "Transcription output(s) failed: {Message}", msg);
            TranscriptionFailed?.Invoke(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transcription failed: {Message}", ex.Message);
            TranscriptionFailed?.Invoke(ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(wavPath))
                    File.Delete(wavPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete temporary audio file {Path}", wavPath);
            }

            if (string.Equals(_tempWavPath, wavPath, StringComparison.OrdinalIgnoreCase))
                _tempWavPath = null;
        }
    }

    // ── Transcription ────────────────────────────────────────────────────────

    private async Task TranscribeAsync(string wavPath)
    {
        var segments = await _transcribeSegmentsAsync(wavPath);
        Log.Information("Transcription complete ({Segments} segments)", segments.Count);

        var result = new TranscriptionResult
        {
            Timestamp = DateTime.Now,
            Segments  = segments.ToList()
        };

        await _outputManager.WriteAsync(result, _settings);
    }

    private async Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>> TranscribeSegmentsAsync(string wavPath)
    {
        var modelPath = await EnsureModelDownloadedAsync();

        return await Task.Run(() =>
        {
            var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>();

            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder()
                .WithLanguageDetection()
                .WithSegmentEventHandler(segment =>
                    segments.Add((segment.Start, segment.End, segment.Text.Trim())))
                .Build();
            using var fileStream = File.OpenRead(wavPath);

            processor.Process(fileStream);
            return (IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>)segments;
        });
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
            Log.Information("Model file not found — downloading {Model}", _settings.ModelType);
            ModelDownloadStarted?.Invoke(_settings.ModelType);
            try
            {
                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
                using var fileStream  = File.OpenWrite(modelPath);
                await modelStream.CopyToAsync(fileStream);
                Log.Information("Model downloaded successfully: {Model}", _settings.ModelType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Model download failed for {Model}", _settings.ModelType);
                // Remove partial file so the next run retries the download.
                if (File.Exists(modelPath)) File.Delete(modelPath);
                throw;
            }
            ModelDownloadFinished?.Invoke();
        }

        return modelPath;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _waveIn?.Dispose();
        _waveWriter?.Dispose();
    }
}
