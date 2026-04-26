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

        Assert.Contains("<DefineConstants>$(DefineConstants);Version=$(Version)</DefineConstants>", installerProject, StringComparison.Ordinal);
    }
}
