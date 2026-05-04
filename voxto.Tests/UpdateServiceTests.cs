using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Voxto;

namespace Voxto.Tests;

/// <summary>
/// Unit tests for <see cref="UpdateService"/> helpers and state transitions.
/// </summary>
public class UpdateServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public UpdateServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ParseVersionFromTag ───────────────────────────────────────────────────

    [Theory]
    [InlineData("v2026.4.25.1",  2026, 4, 25, 1)]
    [InlineData("V2026.12.1.42", 2026, 12, 1, 42)]
    [InlineData("2026.4.25.1",   2026, 4, 25, 1)]   // no prefix
    [InlineData("1.0.0.0",       1,    0,  0, 0)]   // classic semver-style
    public void ParseVersionFromTag_ValidTag_ReturnsExpectedVersion(
        string tag, int major, int minor, int build, int revision)
    {
        var expected = new Version(major, minor, build, revision);
        var actual   = UpdateService.ParseVersionFromTag(tag);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("vNOT_A_VERSION")]
    [InlineData("latest")]
    [InlineData("  ")]
    public void ParseVersionFromTag_InvalidTag_ReturnsNull(string tag)
    {
        var result = UpdateService.ParseVersionFromTag(tag);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("v2026.4.26")]
    [InlineData("2026.4.26")]
    public void ParseVersionFromTag_ThreePartTag_ReturnsVersionWithNegativeRevision(string tag)
    {
        // A 3-part tag parses successfully but has Revision == -1.
        // CheckAndDownloadAsync guards against this to avoid ToString(4) throwing.
        var result = UpdateService.ParseVersionFromTag(tag);
        Assert.NotNull(result);
        Assert.True(result!.Revision < 0, "Expected Revision to be negative for a 3-part version tag");
    }

    // ── VerifySha256 ──────────────────────────────────────────────────────────

    [Fact]
    public void VerifySha256_CorrectHash_ReturnsTrue()
    {
        using var tmp = new TempFile();
        File.WriteAllText(tmp.Path, "voxto test content");

        var expected = ComputeSha256Hex(tmp.Path);
        Assert.True(UpdateService.VerifySha256(tmp.Path, expected));
    }

    [Fact]
    public void VerifySha256_CorrectHashLowercase_ReturnsTrue()
    {
        // The service must accept lowercase hex (some sha256sum tools output lowercase).
        using var tmp = new TempFile();
        File.WriteAllText(tmp.Path, "voxto test content");

        var expected = ComputeSha256Hex(tmp.Path).ToLowerInvariant();
        Assert.True(UpdateService.VerifySha256(tmp.Path, expected));
    }

    [Fact]
    public void VerifySha256_WrongHash_ReturnsFalse()
    {
        using var tmp = new TempFile();
        File.WriteAllText(tmp.Path, "voxto test content");

        const string wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
        Assert.False(UpdateService.VerifySha256(tmp.Path, wrongHash));
    }

    [Fact]
    public void VerifySha256_TamperedFile_ReturnsFalse()
    {
        using var tmp = new TempFile();
        File.WriteAllText(tmp.Path, "original content");
        var hash = ComputeSha256Hex(tmp.Path);

        // Tamper with the file after computing the expected hash.
        File.WriteAllText(tmp.Path, "tampered content");

        Assert.False(UpdateService.VerifySha256(tmp.Path, hash));
    }

    // ── IsDueForCheck ─────────────────────────────────────────────────────────

    [Fact]
    public void IsDueForCheck_NeverChecked_ReturnsTrue()
    {
        // null lastCheck means the user has never run an update check.
        Assert.True(UpdateService.IsDueForCheck(null, UpdateCheckInterval.Weekly));
        Assert.True(UpdateService.IsDueForCheck(null, UpdateCheckInterval.Daily));
    }

    [Theory]
    [InlineData(0,  false)] // checked 0 h ago → not due
    [InlineData(23, false)] // checked 23 h ago → not due
    [InlineData(25, true)]  // checked 25 h ago → due
    public void IsDueForCheck_DailyWithLastCheckHoursAgo_ReturnsExpected(
        int hoursAgo, bool expectedDue)
    {
        var lastCheck = DateTime.UtcNow - TimeSpan.FromHours(hoursAgo);
        Assert.Equal(expectedDue, UpdateService.IsDueForCheck(lastCheck, UpdateCheckInterval.Daily));
    }

    [Theory]
    [InlineData(6, false)] // checked 6 days ago → not due
    [InlineData(8, true)]  // checked 8 days ago → due
    public void IsDueForCheck_WeeklyWithLastCheckDaysAgo_ReturnsExpected(
        int daysAgo, bool expectedDue)
    {
        var lastCheck = DateTime.UtcNow - TimeSpan.FromDays(daysAgo);
        Assert.Equal(expectedDue, UpdateService.IsDueForCheck(lastCheck, UpdateCheckInterval.Weekly));
    }

    [Fact]
    public void IsDueForCheck_ExactlyAtDailyThreshold_ReturnsDue()
    {
        // Exactly 24 hours ago should be considered due (>= threshold).
        var lastCheck = DateTime.UtcNow - TimeSpan.FromDays(1);
        Assert.True(UpdateService.IsDueForCheck(lastCheck, UpdateCheckInterval.Daily));
    }

    [Fact]
    public void IsDueForCheck_ExactlyAtWeeklyThreshold_ReturnsDue()
    {
        var lastCheck = DateTime.UtcNow - TimeSpan.FromDays(7);
        Assert.True(UpdateService.IsDueForCheck(lastCheck, UpdateCheckInterval.Weekly));
    }

    [Fact]
    public void GetInstalledExecutablePath_UsesLocalAppDataVoxtoDirectory()
    {
        var path = UpdateService.GetInstalledExecutablePath();

        Assert.EndsWith(Path.Combine("Voxto", "voxto.exe"), path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}Programs{Path.DirectorySeparatorChar}", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DiscoverOnly_PopulatesPendingStateWithoutDownloading()
    {
        const string version = "9999.1.1.1";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version)
        };

        using var service = harness.CreateService();

        await service.CheckForUpdatesAsync(downloadAndApply: false);

        Assert.Equal(version, service.PendingVersion);
        Assert.Equal(UpdateService.BuildMsiFileName(version, RuntimeInformation.ProcessArchitecture), service.PendingMsiFileName);
        Assert.NotNull(service.PendingMsiDownloadUrl);
        Assert.NotNull(service.PendingHashDownloadUrl);
        Assert.Null(service.PendingMsiPath);
        Assert.Equal([version], harness.AvailableVersions);
        Assert.Empty(harness.FailedMessages);
        Assert.Equal(0, harness.DownloadCallCount);
        Assert.Equal(0, harness.ApplyCallCount);
        Assert.Equal(0, harness.ReadyCount);
        Assert.Equal(1, harness.SaveCallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DownloadAndApplyTrue_DownloadsResolvedPendingAssetAndApplies()
    {
        const string version = "9999.2.2.2";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version)
        };
        harness.HashResponse = $"{ComputeSha256HexFromContent(harness.DownloadPayload)}  {UpdateService.BuildMsiFileName(version, RuntimeInformation.ProcessArchitecture)}";

        using var service = harness.CreateService();

        await service.CheckForUpdatesAsync(downloadAndApply: true);

        Assert.Equal(version, service.PendingVersion);
        Assert.Equal(UpdateService.BuildMsiFileName(version, RuntimeInformation.ProcessArchitecture), service.PendingMsiFileName);
        Assert.NotNull(service.PendingMsiPath);
        Assert.EndsWith(service.PendingMsiFileName, service.PendingMsiPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.DownloadCallCount);
        Assert.Equal(1, harness.ApplyCallCount);
        Assert.Equal(0, harness.ReadyCount);
        Assert.Empty(harness.FailedMessages);
        Assert.Equal(1, harness.SaveCallCount);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DefaultBehavior_WithAutoInstallEnabled_DownloadsAndApplies()
    {
        const string version = "9999.2.2.3";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version),
            AutoDownloadInstallRestartEnabled = true
        };
        harness.HashResponse = $"{ComputeSha256HexFromContent(harness.DownloadPayload)}  {UpdateService.BuildMsiFileName(version, RuntimeInformation.ProcessArchitecture)}";

        using var service = harness.CreateService();

        await service.CheckForUpdatesAsync();

        Assert.Equal(version, service.PendingVersion);
        Assert.NotNull(service.PendingMsiPath);
        Assert.Equal(1, harness.DownloadCallCount);
        Assert.Equal(1, harness.ApplyCallCount);
        Assert.Empty(harness.FailedMessages);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DefaultBehavior_WithAutoInstallDisabled_OnlyDiscoversUpdate()
    {
        const string version = "9999.2.2.4";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version),
            AutoDownloadInstallRestartEnabled = false
        };

        using var service = harness.CreateService();

        await service.CheckForUpdatesAsync();

        Assert.Equal(version, service.PendingVersion);
        Assert.Null(service.PendingMsiPath);
        Assert.Equal(0, harness.DownloadCallCount);
        Assert.Equal(0, harness.ApplyCallCount);
        Assert.Empty(harness.FailedMessages);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ExplicitFalse_WithAutoInstallEnabled_OnlyDiscoversUpdate()
    {
        const string version = "9999.2.2.5";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version),
            AutoDownloadInstallRestartEnabled = true
        };

        using var service = harness.CreateService();

        await service.CheckForUpdatesAsync(downloadAndApply: false);

        Assert.Equal(version, service.PendingVersion);
        Assert.Null(service.PendingMsiPath);
        Assert.Equal(0, harness.DownloadCallCount);
        Assert.Equal(0, harness.ApplyCallCount);
        Assert.Empty(harness.FailedMessages);
    }

    [Fact]
    public async Task DownloadAndApplyPendingUpdateAsync_WithoutPendingUpdate_RaisesActionableFailureMessage()
    {
        var harness = new UpdateServiceHarness(_tempDir);

        using var service = harness.CreateService();

        await service.DownloadAndApplyPendingUpdateAsync();

        Assert.Equal(["No pending update is available to install. Run 'Check for Updates' first."], harness.FailedMessages);
    }

    [Fact]
    public async Task DownloadAndApplyPendingUpdateAsync_WhenDownloadFails_RaisesUpdateFailedInsteadOfThrowing()
    {
        const string version = "9999.3.3.3";
        var harness = new UpdateServiceHarness(_tempDir)
        {
            ReleaseToReturn = CreateRelease(version)
        };

        using var service = harness.CreateService();
        await service.CheckForUpdatesAsync(downloadAndApply: false);

        harness.DownloadException = new IOException("download failed");

        await service.DownloadAndApplyPendingUpdateAsync();

        Assert.Equal(["Update download/install failed. Please try again."], harness.FailedMessages);
        Assert.Equal(0, harness.ApplyCallCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeSha256Hex(string path)
    {
        using var sha  = SHA256.Create();
        using var file = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(file));
    }

    private static string ComputeSha256HexFromContent(string content)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(content)));
    }

    private static UpdateService.ReleaseInfo CreateRelease(string version)
    {
        var msiFileName = UpdateService.BuildMsiFileName(version, RuntimeInformation.ProcessArchitecture);
        return new UpdateService.ReleaseInfo(
            $"v{version}",
            [
                new UpdateService.ReleaseAssetInfo(msiFileName, "https://example.test/" + msiFileName),
                new UpdateService.ReleaseAssetInfo($"{msiFileName}.sha256", "https://example.test/" + msiFileName + ".sha256")
            ]);
    }

    // ── Temp file helper (IDisposable) ────────────────────────────────────────

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch { /* best-effort cleanup */ }
        }
    }

    private sealed class UpdateServiceHarness
    {
        private readonly string _tempDir;

        public UpdateServiceHarness(string tempDir) => _tempDir = tempDir;

        public UpdateService.ReleaseInfo? ReleaseToReturn { get; set; }
        public bool AutoDownloadInstallRestartEnabled { get; set; }
        public Exception? DownloadException { get; set; }
        public string DownloadPayload { get; set; } = "voxto update payload";
        public string HashResponse { get; set; } = string.Empty;
        public int DownloadCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }
        public int SaveCallCount { get; private set; }
        public int ReadyCount { get; private set; }
        public List<string> AvailableVersions { get; } = [];
        public List<string> FailedMessages { get; } = [];

        private UpdateService CreateService(AppSettings settings)
        {
            var service = new UpdateService(
                settings,
                FetchLatestReleaseAsync,
                DownloadAsync,
                GetRemoteTextAsync,
                PersistSettings,
                () => ApplyCallCount++,
                _tempDir);

            service.UpdateAvailable += version => AvailableVersions.Add(version);
            service.UpdateReady += () => ReadyCount++;
            service.UpdateFailed += message => FailedMessages.Add(message);

            return service;
        }

        public UpdateService CreateService()
        {
            var settings = new AppSettings
            {
                AutoDownloadInstallRestartEnabled = AutoDownloadInstallRestartEnabled
            };

            return CreateService(settings);
        }

        private Task<UpdateService.ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ReleaseToReturn);

        private Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            DownloadCallCount++;

            if (DownloadException is not null)
                return Task.FromException(DownloadException);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, DownloadPayload);
            return Task.CompletedTask;
        }

        private Task<string> GetRemoteTextAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(HashResponse);

        private void PersistSettings(AppSettings settings) => SaveCallCount++;
    }
}
