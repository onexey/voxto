using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Serilog;

namespace Voxto;

/// <summary>
/// Periodically checks GitHub Releases for a newer version of Voxto, downloads the
/// MSI installer in the background, verifies its SHA-256 hash, and orchestrates a
/// silent install + restart when the user confirms.
///
/// <para>
/// Update flow:
/// <list type="number">
///   <item>Background loop wakes hourly; if <see cref="AppSettings.UpdateCheckInterval"/>
///         has elapsed since <see cref="AppSettings.LastUpdateCheck"/>, it queries the
///         GitHub Releases API.</item>
///   <item>If a newer tag is found the matching <c>.msi</c> and <c>.msi.sha256</c>
///         assets are downloaded to <c>%LocalAppData%\Voxto\updates\</c>.</item>
///   <item>SHA-256 of the downloaded file is compared against the sidecar hash.</item>
///   <item>On success <see cref="UpdateReady"/> fires; the caller shows a tray prompt.</item>
///   <item><see cref="ApplyUpdateAndRestart"/> writes a small PowerShell trampoline to
///         <c>%TEMP%</c>, launches it hidden, then shuts the app down. The trampoline
///         runs <c>msiexec /passive</c> and relaunches the new executable.</item>
/// </list>
/// </para>
/// </summary>
public sealed class UpdateService : IDisposable
{
    // ── GitHub coordinates ────────────────────────────────────────────────────

    private const string Owner   = "onexey";
    private const string Repo    = "voxto";
    private const string ApiBase = $"https://api.github.com/repos/{Owner}/{Repo}";

    // Cache directory inside the existing Voxto data folder.
    private static readonly string DefaultUpdateCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxto", "updates");

    // ── Shared HttpClient ─────────────────────────────────────────────────────

    // One instance per process; configure default headers once.
    private static readonly HttpClient Http = BuildHttpClient();

    private static HttpClient BuildHttpClient()
    {
        var ver    = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", $"Voxto/{ver}");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    // ── Public events ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when a newer release is detected on GitHub. Argument is the new
    /// version string (e.g. <c>"2026.4.26.1"</c>). Depending on settings, the
    /// update either remains pending for the user to install or proceeds into
    /// automatic download/install.
    /// </summary>
    public event Action<string>? UpdateAvailable;

    /// <summary>Download progress (0–100).</summary>
    public event Action<int>? DownloadProgress;

    /// <summary>
    /// Fired when the MSI has been downloaded and its SHA-256 hash verified.
    /// The caller should prompt the user and then call
    /// <see cref="ApplyUpdateAndRestart"/> when ready.
    /// </summary>
    public event Action? UpdateReady;

    /// <summary>Fired when the update check or download fails.</summary>
    public event Action<string>? UpdateFailed;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Version string of the pending update (e.g. <c>"2026.4.26.1"</c>), or
    /// <c>null</c> if no update has been found yet.
    /// </summary>
    public string? PendingVersion { get; private set; }

    /// <summary>
    /// Full path to the downloaded and hash-verified MSI, or <c>null</c> if not
    /// yet ready.
    /// </summary>
    public string? PendingMsiPath { get; private set; }

    internal string? PendingMsiFileName { get; private set; }
    internal string? PendingMsiDownloadUrl { get; private set; }
    internal string? PendingHashDownloadUrl { get; private set; }

    // ── Internal state ────────────────────────────────────────────────────────

    private readonly Func<CancellationToken, Task<ReleaseInfo?>> _fetchLatestReleaseAsync;
    private readonly Func<string, string, CancellationToken, Task> _downloadAssetAsync;
    private readonly Func<string, CancellationToken, Task<string>> _getRemoteTextAsync;
    private readonly Action<AppSettings> _persistSettings;
    private readonly Action _applyPendingUpdateAndRestart;
    private readonly string _updateCacheDir;
    private AppSettings              _settings;
    private CancellationTokenSource? _cts;
    private Task?                    _backgroundLoop;

    /// <summary>Creates the service with the current application settings.</summary>
    public UpdateService(AppSettings settings)
        : this(settings, null, null, null, null, null, null)
    {
    }

    internal UpdateService(
        AppSettings settings,
        Func<CancellationToken, Task<ReleaseInfo?>>? fetchLatestReleaseAsync,
        Func<string, string, CancellationToken, Task>? downloadAssetAsync,
        Func<string, CancellationToken, Task<string>>? getRemoteTextAsync,
        Action<AppSettings>? persistSettings,
        Action? applyPendingUpdateAndRestart,
        string? updateCacheDir)
    {
        _settings                    = settings;
        _fetchLatestReleaseAsync     = fetchLatestReleaseAsync ?? FetchLatestReleaseAsync;
        _downloadAssetAsync          = downloadAssetAsync ?? DownloadWithProgressAsync;
        _getRemoteTextAsync          = getRemoteTextAsync ?? ((url, ct) => Http.GetStringAsync(url, ct));
        _persistSettings             = persistSettings ?? (savedSettings => savedSettings.Save());
        _applyPendingUpdateAndRestart = applyPendingUpdateAndRestart ?? ApplyUpdateAndRestart;
        _updateCacheDir              = updateCacheDir ?? DefaultUpdateCacheDir;
    }

    /// <summary>
    /// Updates the settings reference. Call this after the user saves preferences
    /// so the service picks up the new interval/enabled state immediately.
    /// </summary>
    public void UpdateSettings(AppSettings settings) => _settings = settings;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Starts the background hourly poll loop.</summary>
    public void Start()
    {
        Stop(); // cancel any existing loop before creating a new one
        _cts            = new CancellationTokenSource();
        _backgroundLoop = Task.Run(() => PeriodicCheckLoopAsync(_cts.Token));
    }

    /// <summary>Cancels the background loop and waits for it to finish.</summary>
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    /// <summary>Cancels the background loop and waits for it to finish.</summary>
    public async Task StopAsync()
    {
        var cts            = _cts;
        var backgroundLoop = _backgroundLoop;

        if (cts is null && backgroundLoop is null)
            return;

        _cts            = null;
        _backgroundLoop = null;

        try
        {
            cts?.Cancel();

            if (backgroundLoop is not null)
            {
                try
                {
                    await backgroundLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
                {
                    // Expected when the loop observes the cancellation we requested.
                }
            }
        }
        finally
        {
            cts?.Dispose();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs an immediate update check regardless of the scheduled interval.
    /// Safe to call from any thread.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool? downloadAndApply = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await CheckAndDownloadAsync(
            cts.Token,
            downloadAndApply ?? _settings.AutoDownloadInstallRestartEnabled).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads, verifies, and installs the already-detected pending update.
    /// </summary>
    public async Task DownloadAndApplyPendingUpdateAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await DownloadPendingUpdateAsync(cts.Token, applyAfterDownload: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            const string timeoutError = "Update download/install timed out. Please try again.";
            Log.Warning(timeoutError);
            UpdateFailed?.Invoke(timeoutError);
        }
        catch (Exception ex)
        {
            const string installError = "Update download/install failed. Please try again.";
            Log.Error(ex, "Pending update install failed");
            UpdateFailed?.Invoke(installError);
        }
    }

    /// <summary>
    /// Applies the downloaded and verified update by launching a PowerShell
    /// trampoline that runs <c>msiexec /passive</c>, then shuts the application
    /// down so the installer can replace the running executable.
    ///
    /// Must be called on the WPF dispatcher thread (or any thread — the shutdown
    /// is dispatched internally).
    /// </summary>
    public void ApplyUpdateAndRestart()
    {
        if (PendingMsiPath is null)
        {
            Log.Warning("ApplyUpdateAndRestart called but no pending MSI");
            return;
        }

        Log.Information("Applying update from {Msi}", PendingMsiPath);

        var newExe     = GetInstalledExecutablePath();
        var logPath    = Path.Combine(Path.GetTempPath(), "voxto_update.log");
        var scriptPath = Path.Combine(Path.GetTempPath(), "voxto_update.ps1");

        // Write a self-deleting PowerShell trampoline:
        //   1. Give the current process a moment to exit cleanly.
        //   2. Run msiexec silently, wait for completion.
        //   3. Relaunch the newly installed executable.
        //   4. Remove the script from disk.
        // $$""" requires {{expr}} for C# interpolation; bare { } are literal,
        // which is exactly what PowerShell needs for its if-block braces.
        var script = $$"""
            Start-Sleep -Seconds 3
            $result = Start-Process -FilePath 'msiexec.exe' `
                -ArgumentList '/i "{{PendingMsiPath}}" /passive /norestart /l*v "{{logPath}}"' `
                -Wait -PassThru
            if ($result.ExitCode -eq 0 -and (Test-Path '{{newExe}}')) {
                Start-Process -FilePath '{{newExe}}'
            }
            Remove-Item -Path $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(scriptPath, script);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -NonInteractive -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
        });

        // Shut down the WPF app so msiexec can overwrite our executable.
        System.Windows.Application.Current.Dispatcher.Invoke(
            System.Windows.Application.Current.Shutdown);
    }

    // ── Background loop ───────────────────────────────────────────────────────

    private async Task PeriodicCheckLoopAsync(CancellationToken ct)
    {
        // Short delay on startup so we don't slow down initial launch.
        await Task.Delay(TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            if (_settings.AutoUpdateEnabled && IsDueForCheck(_settings.LastUpdateCheck, _settings.UpdateCheckInterval))
                await CheckAndDownloadAsync(ct, _settings.AutoDownloadInstallRestartEnabled).ConfigureAwait(false);

            // Wake up hourly; the actual check is gated by IsDueForCheck.
            await Task.Delay(TimeSpan.FromHours(1), ct).ConfigureAwait(false);
        }
    }

    // ── Core check logic ──────────────────────────────────────────────────────

    private async Task CheckAndDownloadAsync(CancellationToken ct, bool downloadAndApply)
    {
        try
        {
            Log.Information("Checking for Voxto updates…");

            // ── 1. Query GitHub Releases API ───────────────────────────────
            var release = await _fetchLatestReleaseAsync(ct).ConfigureAwait(false);
            if (release is null)
            {
                Log.Warning("No releases found on GitHub — skipping update check");
                return;
            }

            // ── 2. Compare versions ────────────────────────────────────────
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            var remote  = ParseVersionFromTag(release.TagName);

            if (remote is null || current is null || remote <= current)
            {
                Log.Information("Already on latest version ({Current})", current);
                ClearPendingUpdate();
                PersistLastCheckTime();
                return;
            }

            if (remote.Revision < 0)
            {
                Log.Warning(
                    "Skipping update check because release tag {TagName} does not contain a 4-part version",
                    release.TagName);
                ClearPendingUpdate();
                PersistLastCheckTime();
                return;
            }

            var remoteStr = remote.ToString(4);
            Log.Information("Update available: {Remote} (current: {Current})", remoteStr, current);

            // ── 3. Resolve architecture-specific assets ────────────────────
            var rid      = GetRuntimeIdentifier(RuntimeInformation.ProcessArchitecture);
            var msiName  = BuildMsiFileName(remoteStr, RuntimeInformation.ProcessArchitecture);
            var hashName = $"{msiName}.sha256";

            var msiAsset  = release.Assets.FirstOrDefault(a => a.Name == msiName);
            var hashAsset = release.Assets.FirstOrDefault(a => a.Name == hashName);

            if (msiAsset is null || hashAsset is null)
            {
                ClearPendingUpdate();
                var msg = $"No installer found for {rid} in release {remoteStr}.";
                Log.Warning("Update asset not found — {Message}", msg);
                UpdateFailed?.Invoke(msg);
                return;
            }

            PendingVersion         = remoteStr;
            PendingMsiFileName     = msiName;
            PendingMsiDownloadUrl  = msiAsset.BrowserDownloadUrl;
            PendingHashDownloadUrl = hashAsset.BrowserDownloadUrl;
            PendingMsiPath         = null;

            UpdateAvailable?.Invoke(remoteStr);

            if (downloadAndApply)
            {
                await DownloadPendingUpdateAsync(ct, applyAfterDownload: true).ConfigureAwait(false);
                return;
            }

            PersistLastCheckTime();
        }
        catch (OperationCanceledException)
        {
            // Normal on app shutdown — no log needed.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update check failed");
            UpdateFailed?.Invoke(ex.Message);
        }
    }

    private async Task DownloadPendingUpdateAsync(CancellationToken ct, bool applyAfterDownload)
    {
        if (PendingVersion is null || PendingMsiFileName is null || PendingMsiDownloadUrl is null || PendingHashDownloadUrl is null)
        {
            const string noPendingUpdateError = "No pending update is available to install. Run 'Check for Updates' first.";
            Log.Warning(noPendingUpdateError);
            UpdateFailed?.Invoke(noPendingUpdateError);
            return;
        }

        Directory.CreateDirectory(_updateCacheDir);
        var destPath = Path.Combine(_updateCacheDir, PendingMsiFileName);

        Log.Information("Downloading {Asset} to {Path}", PendingMsiFileName, destPath);
        await _downloadAssetAsync(PendingMsiDownloadUrl, destPath, ct).ConfigureAwait(false);

        var rawHash = await _getRemoteTextAsync(PendingHashDownloadUrl, ct).ConfigureAwait(false);
        var expectedHash = rawHash.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        if (!VerifySha256(destPath, expectedHash))
        {
            File.Delete(destPath);
            const string integrityError = "Update integrity check failed — downloaded file deleted. Please try again.";
            Log.Error("SHA-256 mismatch for {File}", PendingMsiFileName);
            UpdateFailed?.Invoke(integrityError);
            return;
        }

        Log.Information("Update {Version} downloaded and verified", PendingVersion);
        PendingMsiPath = destPath;
        PersistLastCheckTime();

        if (applyAfterDownload)
        {
            _applyPendingUpdateAndRestart();
            return;
        }

        UpdateReady?.Invoke();
    }

    // ── GitHub API ────────────────────────────────────────────────────────────

    // ── Download with progress ────────────────────────────────────────────────

    private async Task DownloadWithProgressAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total    = response.Content.Headers.ContentLength ?? -1L;
        var received = 0L;
        var lastPct  = -1;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dest   = File.Create(destPath);

        var buffer = new byte[81_920]; // 80 KB chunks
        int read;

        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;

            if (total > 0)
            {
                var pct = (int)(received * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    DownloadProgress?.Invoke(pct);
                }
            }
        }
    }

    // ── Static helpers (internal for unit tests) ──────────────────────────────

    /// <summary>
    /// Parses a GitHub release tag (e.g. <c>"v2026.4.25.1"</c> or
    /// <c>"2026.4.25.1"</c>) into a <see cref="Version"/>.
    /// Returns <c>null</c> if the tag cannot be parsed.
    /// </summary>
    internal static Version? ParseVersionFromTag(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>
    /// Returns <c>true</c> if enough time has elapsed since <paramref name="lastCheck"/>
    /// to warrant a new check, given the configured <paramref name="interval"/>.
    /// A <c>null</c> last-check time means never checked → always due.
    /// </summary>
    internal static bool IsDueForCheck(DateTime? lastCheck, UpdateCheckInterval interval)
    {
        if (lastCheck is null) return true;
        var threshold = interval == UpdateCheckInterval.Daily
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromDays(7);
        return DateTime.UtcNow - lastCheck.Value >= threshold;
    }

    internal static string GetInstalledExecutablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxto",
        "voxto.exe");

    internal static string GetRuntimeIdentifier(Architecture architecture) =>
        architecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    internal static string BuildMsiFileName(string version, Architecture architecture) =>
        $"voxto-{version}-{GetRuntimeIdentifier(architecture)}.msi";

    /// <summary>
    /// Computes the SHA-256 of <paramref name="filePath"/> and compares it
    /// (case-insensitively) against <paramref name="expectedHex"/>.
    /// </summary>
    internal static bool VerifySha256(string filePath, string expectedHex)
    {
        using var sha  = SHA256.Create();
        using var file = File.OpenRead(filePath);
        var hash       = sha.ComputeHash(file);
        var actual     = Convert.ToHexString(hash);
        return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void PersistLastCheckTime()
    {
        _settings.LastUpdateCheck = DateTime.UtcNow;
        _persistSettings(_settings);
    }

    private void ClearPendingUpdate()
    {
        PendingVersion         = null;
        PendingMsiFileName     = null;
        PendingMsiPath         = null;
        PendingMsiDownloadUrl  = null;
        PendingHashDownloadUrl = null;
    }

    internal sealed record ReleaseInfo(string TagName, IReadOnlyList<ReleaseAssetInfo> Assets);

    internal sealed record ReleaseAssetInfo(string Name, string BrowserDownloadUrl);

    // ── GitHub API DTOs ───────────────────────────────────────────────────────

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string         TagName,
        [property: JsonPropertyName("assets")]   List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")]                  string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            var release = await Http
                .GetFromJsonAsync<GitHubRelease>($"{ApiBase}/releases/latest", ct)
                .ConfigureAwait(false);

            return release is null
                ? null
                : new ReleaseInfo(
                    release.TagName,
                    release.Assets.Select(asset => new ReleaseAssetInfo(asset.Name, asset.BrowserDownloadUrl)).ToArray());
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "GitHub API request failed");
            return null;
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose() => Stop();
}
