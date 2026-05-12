using System.IO;
using NAudio.Wave;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Voxto;

/// <summary>
/// Manages audio capture via NAudio and speech-to-text via Whisper.net.
/// After transcription completes the result is forwarded to every enabled
/// <see cref="ITranscriptionOutput"/> via <see cref="OutputManager"/>.
/// </summary>
public class RecorderService : IDisposable
{
    private static readonly TimeSpan RecordingStoppedTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumTranscribableAudioDuration = TimeSpan.FromMilliseconds(500);

    private AppSettings _settings;
    private readonly OutputManager _outputManager;
    private readonly Func<string, Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>>? _transcribeSegmentsAsync;
    private readonly Func<IAudioRecorder> _audioRecorderFactory;
    private readonly DisposableResourceCache<WhisperFactory> _whisperFactoryCache = new();
    private readonly object _recordingStateSync = new();
    private readonly object _transcriptionSync = new();
    private readonly TimeSpan _recordingStoppedTimeout;

    private IAudioRecorder? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _tempWavPath;
    private RecordingCaptureState _recordingState;
    private string? _activeRecordingTrigger;
    private long _activeCaptureId;
    private long _nextCaptureId;
    private int _recordingFailureHandled;
    private TaskCompletionSource<StoppedEventArgs>? _recordingStoppedSource;

    /// <summary>Raised when all enabled outputs have been written successfully; argument is the originating capture id.</summary>
    public event Action<long>? TranscriptionCompleted;

    /// <summary>Raised when transcription or any output fails; arguments are the originating capture id and error message.</summary>
    public event Action<long, string>? TranscriptionFailed;

    /// <summary>
    /// Raised just before a model file is downloaded for the first time.
    /// The arguments are the originating capture id and human-readable model name (e.g. <c>"Small"</c>).
    /// Not raised on subsequent runs when the cached file already exists.
    /// </summary>
    public event Action<long, string>? ModelDownloadStarted;

    /// <summary>Raised once the model file has finished downloading; argument is the originating capture id.</summary>
    public event Action<long>? ModelDownloadFinished;

    internal long ActiveCaptureId
    {
        get
        {
            lock (_recordingStateSync)
                return _activeCaptureId;
        }
    }

    /// <summary>Initialises the service with the provided settings and output pipeline.</summary>
    public RecorderService(AppSettings settings, OutputManager outputManager)
        : this(settings, outputManager, null, null)
    {
    }

    internal RecorderService(
        AppSettings settings,
        OutputManager outputManager,
        Func<string, Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>>>? transcribeSegmentsAsync,
        Func<IAudioRecorder>? audioRecorderFactory = null,
        TimeSpan? recordingStoppedTimeout = null)
    {
        _settings      = settings;
        _outputManager = outputManager;
        _transcribeSegmentsAsync = transcribeSegmentsAsync;
        _audioRecorderFactory = audioRecorderFactory ?? CreateAudioRecorder;
        _recordingStoppedTimeout = recordingStoppedTimeout ?? RecordingStoppedTimeout;
    }

    /// <summary>Applies updated settings (e.g. model type or output folder) without restarting.</summary>
    public void UpdateSettings(AppSettings settings) => _settings = settings;

    // ── Recording ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts capturing audio from the default microphone at 16 kHz mono (the format Whisper expects).
    /// Returns <see langword="true"/> when microphone capture starts for the supplied
    /// <paramref name="trigger"/>, or <see langword="false"/> when the request is ignored
    /// or startup fails because another capture transition is already in progress.
    /// </summary>
    /// <param name="trigger">Human-readable source of the capture request, used for diagnostics.</param>
    /// <returns><see langword="true"/> when microphone capture begins; otherwise <see langword="false"/>.</returns>
    public Task<bool> StartRecordingAsync(string trigger = "unknown")
    {
        lock (_recordingStateSync)
        {
            if (_recordingState is not RecordingCaptureState.Idle)
            {
                Log.Information(
                    "Recording start ignored because capture state is {State} (trigger={Trigger})",
                    _recordingState,
                    trigger);
                return Task.FromResult(false);
            }

            _recordingState = RecordingCaptureState.Recording;
            _activeRecordingTrigger = trigger;
            _activeCaptureId = Interlocked.Increment(ref _nextCaptureId);
            _tempWavPath = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid()}.wav");
            _recordingFailureHandled = 0;
            _recordingStoppedSource = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            _waveIn = _audioRecorderFactory();
            _waveWriter = new WaveFileWriter(_tempWavPath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
        }
        catch (Exception ex)
        {
            HandleRecordingFailure(ex, deleteTempFile: true, "Failed to start recording");
            return Task.FromResult(false);
        }

        Log.Information("Recording started (trigger={Trigger}, model={Model})", trigger, _settings.ModelType);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Requests microphone capture to stop for the supplied <paramref name="trigger"/>.
    /// Returns <see langword="true"/> when the stop transition is accepted and capture has
    /// finished shutting down; transcription and output continue asynchronously afterwards and
    /// report completion or failure through <see cref="TranscriptionCompleted"/> and
    /// <see cref="TranscriptionFailed"/>.
    /// </summary>
    /// <param name="trigger">Human-readable source of the stop request, used for diagnostics.</param>
    /// <returns>
    /// <see langword="true"/> when capture stop is accepted and recording resources are released;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public async Task<bool> StopAndTranscribeAsync(string trigger = "unknown")
    {
        Task<StoppedEventArgs>? stoppedTask;
        string? startedBy;
        long captureId;
        lock (_recordingStateSync)
        {
            if (_recordingState is RecordingCaptureState.Idle)
            {
                Log.Information("Recording stop ignored because no capture is active (trigger={Trigger})", trigger);
                return false;
            }

            if (_recordingState is RecordingCaptureState.Stopping)
            {
                Log.Information(
                    "Recording stop already in progress (trigger={Trigger}, startedBy={StartedBy})",
                    trigger,
                    _activeRecordingTrigger ?? "unknown");
                return true;
            }

            _recordingState = RecordingCaptureState.Stopping;
            startedBy = _activeRecordingTrigger;
            captureId = _activeCaptureId;
            stoppedTask = _recordingStoppedSource?.Task;
        }

        try
        {
            _waveIn?.StopRecording();
        }
        catch (Exception ex)
        {
            HandleRecordingFailure(ex, deleteTempFile: true, "Failed to stop recording");
            return false;
        }

        if (stoppedTask is not null)
        {
            var completedTask = await Task.WhenAny(stoppedTask, Task.Delay(_recordingStoppedTimeout));
            if (completedTask != stoppedTask)
            {
                HandleRecordingFailure(
                    new TimeoutException($"RecordingStopped was not raised within {_recordingStoppedTimeout.TotalSeconds:0.#} seconds."),
                    deleteTempFile: true,
                    "Timed out waiting for recording to stop");
                return false;
            }

            var stoppedArgs = await stoppedTask;
            if (stoppedArgs.Exception is not null)
                return false;
        }

        CleanupRecordingResources(deleteTempFile: false);

        string? wavPath;
        lock (_recordingStateSync)
        {
            _recordingState = RecordingCaptureState.Idle;
            _activeRecordingTrigger = null;
            wavPath = _tempWavPath;
            _tempWavPath = null;
        }

        Log.Information(
            "Recording stopped — starting transcription (trigger={Trigger}, startedBy={StartedBy})",
            trigger,
            startedBy ?? "unknown");

        if (wavPath == null || !File.Exists(wavPath))
        {
            Log.Warning("Transcription skipped: no audio was captured");
            TranscriptionFailed?.Invoke(captureId, "No audio was captured.");
            return true;
        }

        StartTranscription(captureId, wavPath);
        return true;
    }

    internal async Task TranscribeFileAsync(long captureId, string wavPath, bool deleteAfterTranscribe = false)
    {
        try
        {
            if (!TryValidateRecordedAudio(wavPath, MinimumTranscribableAudioDuration, out var validationFailure))
            {
                Log.Warning("Transcription skipped: {Reason}", validationFailure);
                TranscriptionFailed?.Invoke(captureId, validationFailure);
                return;
            }

            var result = await TranscribeAsync(captureId, wavPath);
            await WriteOutputsAsync(result);
            TranscriptionCompleted?.Invoke(captureId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transcription failed: {Message}", ex.Message);
            TranscriptionFailed?.Invoke(captureId, ex.Message);
        }
        finally
        {
            if (deleteAfterTranscribe)
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

                if (string.Equals(_tempWavPath, wavPath, StringComparison.Ordinal))
                    _tempWavPath = null;
            }
        }
    }

    internal static bool TryValidateRecordedAudio(string wavPath, TimeSpan minimumDuration, out string failureMessage)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            if (reader.Length <= 0 || reader.TotalTime < minimumDuration)
            {
                failureMessage = "Recording was too short. Hold the hotkey a little longer and try again.";
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is FormatException)
        {
            failureMessage = "Recorded audio could not be read. Please try again.";
            return false;
        }

        failureMessage = string.Empty;
        return true;
    }

    // ── Transcription ────────────────────────────────────────────────────────

    private async Task<TranscriptionResult> TranscribeAsync(long captureId, string wavPath)
    {
        var segments = _transcribeSegmentsAsync is null
            ? await TranscribeSegmentsAsync(captureId, wavPath)
            : await _transcribeSegmentsAsync(wavPath);
        Log.Information("Transcription complete ({Segments} segments)", segments.Count);

        return new TranscriptionResult
        {
            Timestamp = DateTime.Now,
            Segments  = segments.ToList()
        };
    }

    private async Task<IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>> TranscribeSegmentsAsync(long captureId, string wavPath)
    {
        var modelPath = await EnsureModelDownloadedAsync(captureId);

        return await Task.Factory.StartNew(() =>
        {
            lock (_transcriptionSync)
            {
                var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>();

                var factory = _whisperFactoryCache.GetOrCreate(modelPath, CreateWhisperFactory);
                using var processor = factory.CreateBuilder()
                    .WithLanguageDetection()
                    .WithSegmentEventHandler(segment =>
                        segments.Add((segment.Start, segment.End, segment.Text.Trim())))
                    .Build();
                using var fileStream = File.OpenRead(wavPath);

                processor.Process(fileStream);
                return (IReadOnlyList<(TimeSpan Start, TimeSpan End, string Text)>)segments;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static WhisperFactory CreateWhisperFactory(string modelPath)
    {
        var factory = WhisperFactory.FromPath(modelPath);
        Log.Information(
            "Whisper runtime initialized ({Runtime})",
            RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown");
        return factory;
    }

    private async Task WriteOutputsAsync(TranscriptionResult result)
    {
        try
        {
            await _outputManager.WriteAsync(result, _settings);
        }
        catch (AggregateException aex)
        {
            var msg = string.Join("; ", aex.InnerExceptions.Select(e => e.Message));
            Log.Error(aex, "Transcription output(s) failed: {Message}", msg);
            throw new InvalidOperationException(msg, aex);
        }
    }

    private async Task<string> EnsureModelDownloadedAsync(long captureId)
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
            ModelDownloadStarted?.Invoke(captureId, _settings.ModelType);
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
            ModelDownloadFinished?.Invoke(captureId);
        }

        return modelPath;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _whisperFactoryCache.Dispose();
        CleanupRecordingResources(deleteTempFile: true);
    }

    private static IAudioRecorder CreateAudioRecorder() =>
        new WaveInEventRecorder(
            new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1), // Whisper expects 16 kHz mono
                BufferMilliseconds = 50
            });

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            TryStopRecordingAfterWriteFailure();
            HandleRecordingFailure(ex, deleteTempFile: true, "Failed to persist captured audio");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _recordingStoppedSource?.TrySetResult(e);

        if (e.Exception is null)
            return;

        HandleRecordingFailure(e.Exception, deleteTempFile: true, "Recording stopped unexpectedly");
    }

    private void HandleRecordingFailure(Exception ex, bool deleteTempFile, string message)
    {
        if (Interlocked.Exchange(ref _recordingFailureHandled, 1) == 1)
            return;

        var captureId = ActiveCaptureId;
        _recordingStoppedSource?.TrySetResult(new StoppedEventArgs(ex));
        var failureMessage = $"{message}: {ex.Message}";
        Log.Error(ex, "{Message}", message);
        lock (_recordingStateSync)
        {
            _recordingState = RecordingCaptureState.Idle;
            _activeRecordingTrigger = null;
        }
        CleanupRecordingResources(deleteTempFile);
        TranscriptionFailed?.Invoke(captureId, failureMessage);
    }

    private void StartTranscription(long captureId, string wavPath)
    {
        var transcriptionTask = StartTranscriptionAsync(captureId, wavPath);
        _ = ObserveTranscriptionTaskAsync(captureId, transcriptionTask);
    }

    private async Task StartTranscriptionAsync(long captureId, string wavPath)
    {
        await Task.Yield();
        await TranscribeFileAsync(captureId, wavPath, deleteAfterTranscribe: true);
    }

    private static async Task ObserveTranscriptionTaskAsync(long captureId, Task transcriptionTask)
    {
        try
        {
            await transcriptionTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transcription task faulted unexpectedly (captureId={CaptureId})", captureId);
        }
    }

    private void TryStopRecordingAfterWriteFailure()
    {
        try
        {
            _waveIn?.StopRecording();
        }
        catch (Exception stopException)
        {
            Log.Warning(stopException, "Failed to stop recording after a capture write error");
        }
    }

    private void CleanupRecordingResources(bool deleteTempFile)
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
        }

        try
        {
            _waveWriter?.Flush();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to flush recorded audio");
        }

        _waveWriter?.Dispose();
        _waveWriter = null;
        _waveIn?.Dispose();
        _waveIn = null;
        _recordingStoppedSource = null;

        if (deleteTempFile)
            DeleteTemporaryAudioFile();
    }

    private void DeleteTemporaryAudioFile()
    {
        if (_tempWavPath is null)
            return;

        try
        {
            if (File.Exists(_tempWavPath))
                File.Delete(_tempWavPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete temporary audio file {Path}", _tempWavPath);
        }
        finally
        {
            _tempWavPath = null;
        }
    }
}

internal enum RecordingCaptureState
{
    Idle,
    Recording,
    Stopping
}

internal interface IAudioRecorder : IDisposable
{
    WaveFormat WaveFormat { get; }
    event EventHandler<WaveInEventArgs>? DataAvailable;
    event EventHandler<StoppedEventArgs>? RecordingStopped;
    void StartRecording();
    void StopRecording();
}

internal sealed class WaveInEventRecorder(WaveInEvent waveIn) : IAudioRecorder
{
    public WaveFormat WaveFormat => waveIn.WaveFormat;

    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => waveIn.DataAvailable += value;
        remove => waveIn.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => waveIn.RecordingStopped += value;
        remove => waveIn.RecordingStopped -= value;
    }

    public void StartRecording() => waveIn.StartRecording();

    public void StopRecording() => waveIn.StopRecording();

    public void Dispose() => waveIn.Dispose();
}
