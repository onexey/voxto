using System.IO;
using System.Security.Cryptography;
using Xunit;
using Voxto;

namespace Voxto.Tests;

/// <summary>
/// Unit tests for the pure/static helper methods of <see cref="UpdateService"/>.
///
/// HTTP-dependent paths (FetchLatestRelease, DownloadWithProgress) are not tested
/// here because they require a live network; integration coverage for those paths
/// is out of scope for the unit-test project.
/// </summary>
public class UpdateServiceTests
{
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeSha256Hex(string path)
    {
        using var sha  = SHA256.Create();
        using var file = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(file));
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
}
