using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace CodexLanguageFix.Infrastructure;

internal static class CodexExecutableLocator
{
    private const string PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    private const string PackageName = "OpenAI.Codex";
    private const string ManifestFileName = "AppxManifest.xml";
    private const string ExecutableRelativePath = "app/resources/codex.exe";
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public static string? Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var packagePaths = GetRegisteredPackagePaths();
            return packagePaths is null ? null : SelectExecutableFromPackagePaths(packagePaths);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    internal static string? SelectExecutableFromPackagePaths(IEnumerable<string> packagePaths)
    {
        ArgumentNullException.ThrowIfNull(packagePaths);

        var candidates = new List<(Version Version, string Executable)>();
        foreach (var packagePath in packagePaths)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                continue;
            }

            try
            {
                var manifestPath = Path.Combine(packagePath, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var document = XDocument.Load(manifestPath, LoadOptions.None);
                var identity = document.Root?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Identity");
                if (identity is null
                    || !string.Equals((string?)identity.Attribute("Name"), PackageName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        (string?)identity.Attribute("ProcessorArchitecture"),
                        "x64",
                        StringComparison.OrdinalIgnoreCase)
                    || !TryReadPackageVersion((string?)identity.Attribute("Version"), out var version))
                {
                    continue;
                }

                var executable = Path.GetFullPath(Path.Combine(packagePath, ExecutableRelativePath));
                if (File.Exists(executable))
                {
                    candidates.Add((version, executable));
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or XmlException
                or NotSupportedException)
            {
                // Ungültige oder nicht lesbare Paketpfade sind keine verwendbaren Kandidaten.
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Executable)
            .FirstOrDefault();
    }

    private static bool TryReadPackageVersion(string? value, out Version version)
    {
        if (Version.TryParse(value, out var parsed)
            && parsed.Major >= 0
            && parsed.Minor >= 0
            && parsed.Build >= 0
            && parsed.Revision >= 0)
        {
            version = parsed;
            return true;
        }

        version = null!;
        return false;
    }

    private static IReadOnlyList<string>? GetRegisteredPackagePaths()
    {
        uint packageCount = 0;
        uint bufferLength = 0;
        var status = GetPackagesByPackageFamily(
            PackageFamilyName,
            ref packageCount,
            IntPtr.Zero,
            ref bufferLength,
            IntPtr.Zero);

        if (status == AppModelErrorNoPackage)
        {
            return [];
        }

        if (status != ErrorInsufficientBuffer || packageCount == 0 || bufferLength == 0)
        {
            return null;
        }

        var packageNamesBytes = checked((int)packageCount * IntPtr.Size);
        var bufferBytes = checked((int)bufferLength * sizeof(char));
        var packageNames = IntPtr.Zero;
        var packageNameBuffer = IntPtr.Zero;
        try
        {
            packageNames = Marshal.AllocHGlobal(packageNamesBytes);
            packageNameBuffer = Marshal.AllocHGlobal(bufferBytes);
            var filledPackageCount = packageCount;
            var filledBufferLength = bufferLength;
            status = GetPackagesByPackageFamily(
                PackageFamilyName,
                ref filledPackageCount,
                packageNames,
                ref filledBufferLength,
                packageNameBuffer);
            if (status != ErrorSuccess
                || filledPackageCount > packageCount
                || filledBufferLength > bufferLength)
            {
                return null;
            }

            var packagePaths = new List<string>(checked((int)filledPackageCount));
            for (var index = 0; index < filledPackageCount; index++)
            {
                var fullNamePointer = Marshal.ReadIntPtr(packageNames, checked((int)index * IntPtr.Size));
                var fullName = Marshal.PtrToStringUni(fullNamePointer);
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    return null;
                }

                var packagePath = GetPackagePath(fullName);
                if (packagePath is null)
                {
                    return null;
                }

                packagePaths.Add(packagePath);
            }

            return packagePaths;
        }
        finally
        {
            if (packageNameBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(packageNameBuffer);
            }

            if (packageNames != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(packageNames);
            }
        }
    }

    private static string? GetPackagePath(string packageFullName)
    {
        uint pathLength = 0;
        var status = GetPackagePathByFullName(packageFullName, ref pathLength, IntPtr.Zero);
        if (status != ErrorInsufficientBuffer || pathLength == 0)
        {
            return null;
        }

        var pathBuffer = Marshal.AllocHGlobal(checked((int)pathLength * sizeof(char)));
        try
        {
            var filledPathLength = pathLength;
            status = GetPackagePathByFullName(packageFullName, ref filledPathLength, pathBuffer);
            if (status != ErrorSuccess || filledPathLength > pathLength)
            {
                return null;
            }

            return Marshal.PtrToStringUni(pathBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal(pathBuffer);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(
        string packageFamilyName,
        ref uint count,
        IntPtr packageFullNames,
        ref uint bufferLength,
        IntPtr buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(
        string packageFullName,
        ref uint pathLength,
        IntPtr path);
}
