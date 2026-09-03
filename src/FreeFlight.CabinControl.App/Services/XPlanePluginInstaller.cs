using System.IO;

namespace FreeFlight.CabinControl.App.Services;

public sealed class XPlanePluginInstaller
{
    private const string RelativeBundledPath = "xplane-plugin/FreeFlightCabinBridge/64/win.xpl";
    private const string RelativeInstalledPath = "Resources/plugins/FreeFlightCabinBridge/64/win.xpl";

    public string BundledPluginPath => Path.Combine(
        AppContext.BaseDirectory,
        RelativeBundledPath.Replace('/', Path.DirectorySeparatorChar));

    public bool CanInstall(string? configuredPath) =>
        File.Exists(BundledPluginPath) && ResolveXPlaneRoot(configuredPath) is not null;

    public string GetStatus(string? configuredPath)
    {
        var root = ResolveXPlaneRoot(configuredPath);
        if (root is null)
        {
            return "Select a valid X-Plane folder or X-Plane.exe to install the bridge.";
        }

        if (!File.Exists(BundledPluginPath))
        {
            return "The plugin binary is not included in this development build.";
        }

        var installedPath = Path.Combine(root, RelativeInstalledPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(installedPath))
        {
            return "FreeFlight Cabin Bridge is ready to install.";
        }

        return FilesMatch(BundledPluginPath, installedPath)
            ? "FreeFlight Cabin Bridge is installed and current."
            : "An older FreeFlight Cabin Bridge is installed; click to update it.";
    }

    public string Install(string? configuredPath)
    {
        var root = ResolveXPlaneRoot(configuredPath) ??
                   throw new InvalidOperationException("Select a valid X-Plane installation first.");
        if (!File.Exists(BundledPluginPath))
        {
            throw new FileNotFoundException("The bundled FreeFlight X-Plane plugin is missing.", BundledPluginPath);
        }

        var destination = Path.Combine(root, RelativeInstalledPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(BundledPluginPath, destination, overwrite: true);
        return $"Plugin installed to {destination}. Restart X-Plane to activate it.";
    }

    internal static string? ResolveXPlaneRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        var fullPath = Path.GetFullPath(configuredPath.Trim());
        var root = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
        return root is not null && File.Exists(Path.Combine(root, "X-Plane.exe")) ? root : null;
    }

    private static bool FilesMatch(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length) return false;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        Span<byte> firstBuffer = stackalloc byte[4096];
        Span<byte> secondBuffer = stackalloc byte[4096];
        while (true)
        {
            var firstRead = firstStream.Read(firstBuffer);
            var secondRead = secondStream.Read(secondBuffer);
            if (firstRead != secondRead || !firstBuffer[..firstRead].SequenceEqual(secondBuffer[..secondRead])) return false;
            if (firstRead == 0) return true;
        }
    }
}
