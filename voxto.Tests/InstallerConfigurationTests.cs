using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Voxto.Tests;

public class InstallerConfigurationTests
{
    private static readonly XNamespace WixNamespace = "http://wixtoolset.org/schemas/v4/wxs";

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var installerDirectory = Path.Combine(current.FullName, "installer");
            var workflowPath = Path.Combine(current.FullName, ".github", "workflows", "publish.yml");

            if (Directory.Exists(installerDirectory) && File.Exists(workflowPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root by walking upward from '{AppContext.BaseDirectory}'. " +
            "Expected to find a directory containing both 'installer' and '.github/workflows/publish.yml'.");
    }

    private static XDocument LoadXmlDocument(params string[] relativePathSegments)
    {
        var path = Path.Combine(RepositoryRoot, Path.Combine(relativePathSegments));
        return XDocument.Load(path);
    }

    [Fact]
    public void PackageWxs_UsesMsBuildBackedWixVersionVariable()
    {
        var package = LoadXmlDocument("installer", "Package.wxs")
            .Descendants(WixNamespace + "Package")
            .Single();

        Assert.Equal("$(var.MsiVersion)", package.Attribute("Version")?.Value);
    }

    [Fact]
    public void InstallerProject_DefinesWixVersionConstantFromMsBuildVersion()
    {
        var installerProject = LoadXmlDocument("installer", "installer.wixproj");
        var defineConstants = installerProject.Descendants("DefineConstants").Single().Value;

        Assert.Contains("MsiVersion=$(MsiVersion)", defineConstants, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerProject_DefinesPublishDirectoryForWixAuthoring()
    {
        var installerProject = LoadXmlDocument("installer", "installer.wixproj");
        var defineConstants = installerProject.Descendants("DefineConstants").Single().Value;

        Assert.Contains("PublishDir=$(PublishDir)", defineConstants, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerProject_SuppressesIce38ForPerUserHarvestedFiles()
    {
        var installerProject = LoadXmlDocument("installer", "installer.wixproj");
        var suppressIces = installerProject.Descendants("SuppressIces").Single().Value;

        Assert.Contains("ICE38", suppressIces, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageWxs_HarvestsPublishedFilesFromConfiguredDirectory()
    {
        var publishComponents = LoadXmlDocument("installer", "Package.wxs")
            .Descendants(WixNamespace + "ComponentGroup")
            .Single(element => string.Equals(element.Attribute("Id")?.Value, "PublishComponents", StringComparison.Ordinal));

        var files = publishComponents.Elements(WixNamespace + "Files").Single();

        Assert.Equal("$(var.PublishDir)", publishComponents.Attribute("Source")?.Value);
        Assert.Equal(@"**\*", files.Attribute("Include")?.Value);
    }

    [Fact]
    public void PackageWxs_UsesLocalAppDataProgramsFolderForPerUserInstall()
    {
        var package = LoadXmlDocument("installer", "Package.wxs");
        var localAppDataFolder = package
            .Descendants(WixNamespace + "StandardDirectory")
            .Single(element => string.Equals(element.Attribute("Id")?.Value, "LocalAppDataFolder", StringComparison.Ordinal));

        var programsFolder = localAppDataFolder
            .Elements(WixNamespace + "Directory")
            .Single(element => string.Equals(element.Attribute("Id")?.Value, "ProgramsFolder", StringComparison.Ordinal));

        var installDirectory = programsFolder
            .Elements(WixNamespace + "Directory")
            .Single(element => string.Equals(element.Attribute("Id")?.Value, "INSTALLDIR", StringComparison.Ordinal));

        Assert.Equal("Programs", programsFolder.Attribute("Name")?.Value);
        Assert.Equal("Voxto", installDirectory.Attribute("Name")?.Value);
    }

    [Fact]
    public void PackageWxs_DoesNotUseProgramFilesFolderForPerUserInstall()
    {
        var standardDirectoryIds = LoadXmlDocument("installer", "Package.wxs")
            .Descendants(WixNamespace + "StandardDirectory")
            .Select(element => element.Attribute("Id")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.DoesNotContain("ProgramFiles64Folder", standardDirectoryIds, StringComparer.Ordinal);
    }

    [Fact]
    public void PackageWxs_RemovesInstallDirectoryOnUninstall()
    {
        var cleanupComponent = LoadXmlDocument("installer", "Package.wxs")
            .Descendants(WixNamespace + "Component")
            .Single(element => string.Equals(element.Attribute("Id")?.Value, "InstallDirCleanup", StringComparison.Ordinal));

        var removeFolder = cleanupComponent.Element(WixNamespace + "RemoveFolder");
        var registryValue = cleanupComponent.Element(WixNamespace + "RegistryValue");

        Assert.Equal("uninstall", removeFolder?.Attribute("On")?.Value);
        Assert.Equal("HKCU", registryValue?.Attribute("Root")?.Value);
        Assert.Equal("yes", registryValue?.Attribute("KeyPath")?.Value);
    }

    [Fact]
    public void PublishWorkflow_PassesCompatibleMsiVersionToInstallerBuild()
    {
        var workflowPath = Path.Combine(RepositoryRoot, ".github", "workflows", "publish.yml");
        var workflow     = File.ReadAllText(workflowPath);

        Assert.Matches(new Regex(@"^\s+msi_version:\s+\$\{\{\s*steps\.calver\.outputs\.msi_version\s*\}\}\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^\s+MSI_VERSION=""\$\(date -u \+'%y\.%-m'\)\.\$\{\{\s*github\.run_number\s*\}\}""\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^\s+-p:MsiVersion=""\$msiVersion""\s*`\s*$", RegexOptions.Multiline), workflow);
    }
}
