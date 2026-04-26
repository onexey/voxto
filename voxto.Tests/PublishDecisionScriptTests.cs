using System.Diagnostics;
using System.IO;
using Xunit;

namespace Voxto.Tests;

public sealed class PublishDecisionScriptTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "voxto-tests", Guid.NewGuid().ToString("N"));

    public PublishDecisionScriptTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void RunScript_DocsAndGithubOnlyChanges_SkipsPublish()
    {
        var output = RunScript(
        [
            "docs\\installer.md",
            ".github\\workflows\\publish.yml",
            ".github\\scripts\\should-publish.ps1"
        ]);

        Assert.Contains("should_publish=false", output);
        Assert.Contains("Only docs/ and .github/ changes were detected.", output);
    }

    [Fact]
    public void RunScript_ProjectChange_Publishes()
    {
        var output = RunScript(
        [
            "docs\\installer.md",
            "voxto\\RecorderService.cs"
        ]);

        Assert.Contains("should_publish=true", output);
        Assert.Contains("Relevant changes detected outside docs/ and .github/.", output);
    }

    [Fact]
    public void RunScript_NoChanges_SkipsPublish()
    {
        var output = RunScript([]);

        Assert.Contains("should_publish=false", output);
        Assert.Contains("No changed files were detected.", output);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string RunScript(string[] changedFiles)
    {
        var changedFilesPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.txt");
        File.WriteAllLines(changedFilesPath, changedFiles);

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".github", "scripts", "should-publish.ps1");
        scriptPath = Path.GetFullPath(scriptPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-ChangedFilesFile");
        process.StartInfo.ArgumentList.Add(changedFilesPath);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Script failed with exit code {process.ExitCode}:{Environment.NewLine}{standardError}");
        return standardOutput;
    }
}
