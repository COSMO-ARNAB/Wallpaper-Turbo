using System.Xml.Linq;

namespace WallpaperTurbo.Tests;

public sealed class ReleaseVersionConsistencyTests
{
    [Fact]
    public void ExecutableAndLibraryProjects_DoNotOverrideCentralVersion()
    {
        var root = FindRepositoryRoot();
        var projects = new[]
        {
            "src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj",
            "src/WallpaperTurbo.Core/WallpaperTurbo.Core.csproj",
            "src/WallpaperTurbo.Updater/WallpaperTurbo.Updater.csproj",
            "src/WallpaperTurbo.AppRunner/WallpaperTurbo.AppRunner.csproj"
        };

        foreach (var relativePath in projects)
        {
            var document = XDocument.Load(Path.Combine(root, relativePath));
            var overrides = document.Descendants()
                .Where(element => element.Name.LocalName is "Version" or "AssemblyVersion" or "FileVersion" or "InformationalVersion")
                .ToArray();

            Assert.True(overrides.Length == 0, $"{relativePath} overrides centrally managed version properties.");
        }
    }

    [Fact]
    public void Installer_RequiresVersionInjectionInsteadOfDefiningFallback()
    {
        var installer = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src/WallpaperTurbo.Installer/installer.iss"));

        Assert.Contains("#ifndef MyAppVersion", installer);
        Assert.Contains("#error MyAppVersion must be supplied", installer);
        Assert.DoesNotMatch("#define\\s+MyAppVersion\\s+\"", installer);
    }

    [Fact]
    public void ReleaseWorkflow_ValidatesArtifactsBeforeUpload()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github/workflows/release.yml"));

        Assert.Contains("get-release-version.ps1", workflow);
        Assert.Contains("validate-release.ps1", workflow);
        Assert.Contains("already exists; refusing to overwrite release assets", workflow);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
