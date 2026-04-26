using System;
using System.IO;
using Xunit;

namespace Voxto.Tests;

public class InstallerConfigurationTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void PackageWxs_UsesMsBuildBackedWixVersionVariable()
    {
        var packagePath = Path.Combine(RepositoryRoot, "installer", "Package.wxs");
        var packageWxs  = File.ReadAllText(packagePath);

        Assert.Contains("Version=\"$(var.Version)\"", packageWxs, StringComparison.Ordinal);
        Assert.DoesNotContain("Version=\"$(Version)\"", packageWxs, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerProject_DefinesWixVersionConstantFromMsBuildVersion()
    {
        var projectPath      = Path.Combine(RepositoryRoot, "installer", "installer.wixproj");
        var installerProject = File.ReadAllText(projectPath);

        Assert.Contains("Version=$(Version)", installerProject, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerProject_DefinesPublishDirectoryForWixAuthoring()
    {
        var projectPath      = Path.Combine(RepositoryRoot, "installer", "installer.wixproj");
        var installerProject = File.ReadAllText(projectPath);

        Assert.Contains("PublishDir=$(PublishDir)", installerProject, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageWxs_HarvestsPublishedFilesFromConfiguredDirectory()
    {
        var packagePath = Path.Combine(RepositoryRoot, "installer", "Package.wxs");
        var packageWxs  = File.ReadAllText(packagePath);

        Assert.Contains("ComponentGroup Id=\"PublishComponents\"", packageWxs, StringComparison.Ordinal);
        Assert.Contains("Source=\"$(var.PublishDir)\"", packageWxs, StringComparison.Ordinal);
        Assert.Contains("<Files Include=\"**\" />", packageWxs, StringComparison.Ordinal);
    }
}
