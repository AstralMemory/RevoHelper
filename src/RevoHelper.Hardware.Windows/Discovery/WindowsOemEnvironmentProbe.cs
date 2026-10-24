using System.Diagnostics;
using Microsoft.Win32;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class WindowsOemEnvironmentProbe(IWmiQuery query) : IOemEnvironmentProbe
{
    private static readonly string ProgramFiles =
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string FallbackOemRoot = Path.Combine(
        ProgramFiles,
        "OEM",
        "机械革命控制中心",
        "AiStoneService",
        "MyControlCenter");
    private static string OemRoot =>
        OemControlPathResolver.ResolveFromServiceImagePath(ReadServiceImagePath("GCUBridge")) ??
        FallbackOemRoot;

    public OemComponentEvidence Inspect(string probeKey) =>
        probeKey switch
        {
            "package:CCU.WinUI" => InspectPackage(),
            "service:GCUBridge" => InspectService("GCUBridge"),
            "service:UWACPIDriver" => InspectService("UWACPIDriver"),
            "file:GCUService.exe" => InspectFile(Path.Combine(OemRoot, "GCUService.exe")),
            "file:ACPIDriverDll.dll" => InspectFile(Path.Combine(OemRoot, "ACPIDriverDll.dll")),
            "pnp:ACPI\\INOU0000" => InspectPnp("ACPI\\INOU0000"),
            "directory:UserPofiles" => InspectDirectory(Path.Combine(OemRoot, "UserPofiles")),
            "file:LibreHardwareMonitorLib.dll" => InspectPackageFile("LibreHardwareMonitorLib.dll"),
            _ => new(false, null, null, "Unknown probe key.")
        };

    private static OemComponentEvidence InspectPackage()
    {
        var directory = FindPackageDirectory();
        return directory is null
            ? new(false, null, null, "CCU.WinUI package directory was not found.")
            : InspectFile(Path.Combine(directory, "CCUWinUI.dll")) with
            {
                Path = directory,
                Evidence = "CCU.WinUI package directory and primary assembly found."
            };
    }

    private static OemComponentEvidence InspectPackageFile(string fileName)
    {
        var directory = FindPackageDirectory();
        return directory is null
            ? new(false, null, null, "CCU.WinUI package directory was not found.")
            : InspectFile(Path.Combine(directory, fileName));
    }

    private static string? FindPackageDirectory()
    {
        try
        {
            var windowsApps = Path.Combine(ProgramFiles, "WindowsApps");
            return Directory
                .EnumerateDirectories(windowsApps, "CCU.WinUI_*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static OemComponentEvidence InspectService(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{name}");
        if (key is null)
        {
            return new(false, null, null, $"Service {name} was not found.");
        }

        var imagePath = key.GetValue("ImagePath")?.ToString();
        return new(true, imagePath, null, $"Service {name} is registered.");
    }

    private static string? ReadServiceImagePath(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{name}");
            return key?.GetValue("ImagePath")?.ToString();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static OemComponentEvidence InspectFile(string path)
    {
        if (!File.Exists(path))
        {
            return new(false, path, null, "File was not found.");
        }

        string? version = null;
        try
        {
            version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return new(true, path, version, "File exists.");
    }

    private static OemComponentEvidence InspectDirectory(string path) =>
        Directory.Exists(path)
            ? new(true, path, null, "Directory exists.")
            : new(false, path, null, "Directory was not found.");

    private OemComponentEvidence InspectPnp(string instanceIdPrefix)
    {
        try
        {
            var match = query.ReadAll("Win32_PnPEntity").FirstOrDefault(row =>
                row.TryGetValue("DeviceID", out var deviceId) &&
                deviceId.StartsWith(instanceIdPrefix, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? new(false, null, null, "PnP endpoint was not found.")
                : new(
                    true,
                    match.GetValueOrDefault("DeviceID"),
                    null,
                    match.GetValueOrDefault("Name") ?? "PnP endpoint found.");
        }
        catch (Exception exception)
        {
            return new(false, null, null, $"PnP query failed: {exception.GetType().Name}");
        }
    }
}
