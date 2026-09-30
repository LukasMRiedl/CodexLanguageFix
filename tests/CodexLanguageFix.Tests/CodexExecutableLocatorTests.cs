using System.Xml.Linq;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class CodexExecutableLocatorTests : IDisposable
{
    private const string PackageName = "OpenAI.Codex";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clf-codex-package-{Guid.NewGuid():N}");

    [Fact]
    public void SelectExecutableFromPackagePaths_ReturnsNullWhenNoPackageIsRegistered()
    {
        Assert.Null(CodexExecutableLocator.SelectExecutableFromPackagePaths([]));
    }

    [Fact]
    public void SelectExecutableFromPackagePaths_RequiresMatchingX64ManifestAndExecutable()
    {
        var missingManifest = Path.Combine(_root, "missing-manifest");
        Directory.CreateDirectory(missingManifest);
        var missingExecutable = CreatePackage("missing-executable", "0.159.2.0", includeExecutable: false);
        var wrongArchitecture = CreatePackage("arm64", "0.999.0.0", architecture: "arm64");
        var wrongPackage = CreatePackage("other-package", "0.999.0.0", packageName: "Other.Package");

        Assert.Null(CodexExecutableLocator.SelectExecutableFromPackagePaths(
            [missingManifest, missingExecutable, wrongArchitecture, wrongPackage]));
    }

    [Fact]
    public void SelectExecutableFromPackagePaths_SelectsHighestValidX64Version()
    {
        var older = CreatePackage("older", "0.146.1.0");
        var current = CreatePackage("current", "0.159.2.0");
        var newerArm64 = CreatePackage("newer-arm64", "0.999.0.0", architecture: "arm64");
        var invalidVersion = CreatePackage("invalid-version", "0.160.preview", architecture: "x64");

        var selected = CodexExecutableLocator.SelectExecutableFromPackagePaths(
        [
            older,
            newerArm64,
            invalidVersion,
            current
        ]);

        Assert.Equal(Path.Combine(current, "app", "resources", "codex.exe"), selected);
    }

    [Fact]
    public void SelectExecutableFromPackagePaths_ComparesVersionComponentsNumerically()
    {
        var lower = CreatePackage("version-9", "1.9.0.0");
        var higher = CreatePackage("version-10", "1.10.0.0");

        var selected = CodexExecutableLocator.SelectExecutableFromPackagePaths([lower, higher]);

        Assert.Equal(Path.Combine(higher, "app", "resources", "codex.exe"), selected);
    }

    [Fact]
    public void SelectExecutableFromPackagePaths_ChangesWhenNewerPackageIsRegistered()
    {
        var installedVersion = CreatePackage("installed", "0.146.1.0");
        var availableVersion = CreatePackage("available", "0.159.2.0");

        var beforeUpdate = CodexExecutableLocator.SelectExecutableFromPackagePaths([installedVersion]);
        var afterUpdate = CodexExecutableLocator.SelectExecutableFromPackagePaths([installedVersion, availableVersion]);

        Assert.Equal(Path.Combine(installedVersion, "app", "resources", "codex.exe"), beforeUpdate);
        Assert.Equal(Path.Combine(availableVersion, "app", "resources", "codex.exe"), afterUpdate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreatePackage(
        string directoryName,
        string version,
        string architecture = "x64",
        bool includeExecutable = true,
        string packageName = PackageName)
    {
        var packagePath = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(packagePath);
        var manifest = new XDocument(
            new XElement("Package",
                new XElement("Identity",
                    new XAttribute("Name", packageName),
                    new XAttribute("ProcessorArchitecture", architecture),
                    new XAttribute("Version", version))));
        manifest.Save(Path.Combine(packagePath, "AppxManifest.xml"));

        if (includeExecutable)
        {
            var executable = Path.Combine(packagePath, "app", "resources", "codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, []);
        }

        return packagePath;
    }
}
