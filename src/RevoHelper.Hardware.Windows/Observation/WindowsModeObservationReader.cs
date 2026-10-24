using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RevoHelper.Core.Observation;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Observation;

public sealed class WindowsModeObservationReader(
    IWmiQuery wmiQuery,
    IHardwareTelemetrySource? telemetrySource = null)
{
    public async ValueTask<ModeObservationSnapshot> CaptureAsync(
        string label,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var profileDirectory = FindProfileDirectory();
        var mainOption = ReadMainOption(profileDirectory);
        HardwareTelemetrySnapshot? telemetry = null;
        if (telemetrySource is not null)
        {
            try
            {
                telemetry = await telemetrySource.ReadAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return new(
            label,
            DateTimeOffset.UtcNow,
            await ReadActivePowerSchemeAsync(cancellationToken),
            ReadAcOnline(),
            mainOption.OperatingMode,
            mainOption.Selections,
            ReadProfileHashes(profileDirectory),
            ReadServiceStates(),
            ReadDeviceStates(),
            telemetry);
    }

    private static string? FindProfileDirectory()
    {
        try
        {
            var oemRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "OEM");
            return Directory.EnumerateDirectories(oemRoot, "UserPofiles", SearchOption.AllDirectories)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "MainOption.json")));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static OemMainOptionState ReadMainOption(string? profileDirectory)
    {
        if (profileDirectory is null)
        {
            return new(null, new Dictionary<string, int?>());
        }

        try
        {
            return OemMainOptionParser.Parse(File.ReadAllText(
                Path.Combine(profileDirectory, "MainOption.json")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, new Dictionary<string, int?>());
        }
    }

    private static IReadOnlyDictionary<string, string> ReadProfileHashes(string? profileDirectory)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profileDirectory is null)
        {
            return hashes;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(profileDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                using var stream = File.OpenRead(path);
                hashes[Path.GetFileName(path)] = Convert.ToHexString(SHA256.HashData(stream));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return hashes;
    }

    private static IReadOnlyDictionary<string, string> ReadServiceStates()
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "GCUBridge", "UWACPIDriver" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{name}");
                states[name] = key is null ? "NotInstalled" : "Installed";
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                states[name] = "Unavailable";
            }
        }

        states["GCUServiceProcess"] = Process.GetProcessesByName("GCUService").Length > 0
            ? "Running"
            : "Stopped";
        states["GCUBridgeProcess"] = Process.GetProcessesByName("GCUBridge").Length > 0
            ? "Running"
            : "Stopped";
        return states;
    }

    private IReadOnlyDictionary<string, string> ReadDeviceStates()
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var endpoint = wmiQuery.ReadAll("Win32_PnPEntity").FirstOrDefault(row =>
                row.TryGetValue("DeviceID", out var id) &&
                id.StartsWith("ACPI\\INOU0000", StringComparison.OrdinalIgnoreCase));
            states["ACPI\\INOU0000"] = endpoint?.GetValueOrDefault("Status") ?? "NotFound";
        }
        catch (Exception exception)
        {
            states["ACPI\\INOU0000"] = $"Unavailable:{exception.GetType().Name}";
        }

        return states;
    }

    private static async Task<string?> ReadActivePowerSchemeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/getactivescheme",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var guid = Regex.Match(
                output,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return guid.Success ? guid.Value.ToLowerInvariant() : null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool? ReadAcOnline()
    {
        try
        {
            return GetSystemPowerStatus(out var status)
                ? status.ACLineStatus switch
                {
                    0 => false,
                    1 => true,
                    _ => null
                }
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
