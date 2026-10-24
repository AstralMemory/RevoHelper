using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RevoHelper.App.Settings;

/// <summary>Only manages the fixed, packaged RevoHelper boot helper. Never launches it.</summary>
public sealed class WindowsBootWaterTaskPlatform : IBootWaterTaskPlatform
{
    public const string TaskName = "RevoHelper Boot Water AutoConnect";
    public static string InstalledPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RevoHelper", "BootWater", "RevoHelper.BootWater.exe");

    public static Stream OpenPackagedHelper() => typeof(WindowsBootWaterTaskPlatform).Assembly
        .GetManifestResourceStream("RevoHelper.BootWater.exe")
        ?? throw new IOException("内置水冷组件不可用，未安装任务。");

    public string? ReadTaskXml() => WithFolder<string?>(folder =>
    {
        object? task = null;
        try { task = folder.GetTask(TaskName); return (string)((dynamic)task).Xml; }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
        finally { Release(task); }
    });

    public void RegisterTask(string xml)
    {
        RequireAdministrator();
        WithFolder<object?>(folder =>
        {
            object? registered = null;
            try { registered = folder.RegisterTask(TaskName, xml, 6, "SYSTEM", null, 5, null); return null; }
            finally { Release(registered); }
        });
    }

    public void RemoveTask()
    {
        RequireAdministrator();
        WithFolder<object?>(folder => { folder.DeleteTask(TaskName, 0); return null; });
    }

    public bool IsInstalledHelperValid()
    {
        try
        {
            var directory = Path.GetDirectoryName(InstalledPath)!;
            RejectReparsePoints(InstalledPath);
            if (!Directory.Exists(directory) || !File.Exists(InstalledPath)) return false;
            if (!BootWaterFileSecurity.IsProtected(new DirectoryInfo(Path.GetDirectoryName(directory)!).GetAccessControl()) ||
                !BootWaterFileSecurity.IsProtected(new DirectoryInfo(directory).GetAccessControl())) return false;
            VerifyProtectedFile(InstalledPath);
            VerifyExistingLog(directory);
            return VerifyHelper(InstalledPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void DeployVerifiedHelper()
    {
        RequireAdministrator();
        using var source = OpenPackagedHelper();
        var directory = Path.GetDirectoryName(InstalledPath)!;
        RejectReparsePoints(directory);
        foreach (var protectedDirectory in new[] { Path.GetDirectoryName(directory)!, directory })
        {
            var info = new DirectoryInfo(protectedDirectory);
            info.Create(BootWaterFileSecurity.CreateDirectorySecurity());
            RejectReparsePoints(protectedDirectory);
            // Never take over an untrusted pre-existing directory: previously granted
            // DELETE/WRITE_DAC handles survive an ACL change and can undo that change.
            if (!BootWaterFileSecurity.IsProtected(info.GetAccessControl()))
                throw new IOException("现有 RevoHelper 辅助目录权限不安全，未自动接管。请先备份并由管理员检查 ProgramData\\RevoHelper；现有任务与日志未删除。");
        }
        VerifyExistingLog(directory);
        RejectReparsePoints(InstalledPath);
        var temporary = Path.Combine(directory, "helper-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(destination);
            new FileInfo(temporary).SetAccessControl(BootWaterFileSecurity.CreateFileSecurity());
            if (!VerifyHelper(temporary)) throw new IOException("复制后辅助程序校验失败。");
            File.Move(temporary, InstalledPath, true);
            if (!VerifyHelper(InstalledPath)) throw new IOException("已安装辅助程序校验失败。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string? ReadLastConnectionResult()
    {
        var log = Path.Combine(Path.GetDirectoryName(InstalledPath)!, "boot-water.log");
        try
        {
            RejectReparsePoints(log);
            if (!File.Exists(log)) return "尚无开机连接记录。";
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 8192), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Split('\n').LastOrDefault(line => line.Contains("结果：", StringComparison.Ordinal))?.Trim()
                ?? "已有启动记录，尚未找到连接结果。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "连接记录暂不可读。"; }
    }

    private static bool VerifyHelper(string path)
    {
        RejectReparsePoints(path);
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return MatchesPackagedHelper(stream);
    }

    /// <summary>Compares the candidate to this application's embedded trusted helper without taking ownership of the stream.</summary>
    public static bool MatchesPackagedHelper(Stream candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var packaged = OpenPackagedHelper();
        Span<byte> expected = stackalloc byte[8192];
        Span<byte> actual = stackalloc byte[8192];
        int count;
        while ((count = packaged.Read(expected)) > 0)
        {
            try { candidate.ReadExactly(actual[..count]); }
            catch (EndOfStreamException) { return false; }
            if (!expected[..count].SequenceEqual(actual[..count])) return false;
        }
        return candidate.ReadByte() == -1;
    }

    private static void VerifyExistingLog(string directory)
    {
        var log = Path.Combine(directory, "boot-water.log");
        RejectReparsePoints(log);
        if (File.Exists(log)) VerifyProtectedFile(log);
    }

    private static void VerifyProtectedFile(string path)
    {
        RejectReparsePoints(path);
        // No write/delete sharing: validate the same object, not just a pathname.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info) || info.NumberOfLinks != 1 ||
            (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
            !BootWaterFileSecurity.IsProtected(file.GetAccessControl()))
            throw new IOException("辅助文件或历史日志的链接/权限不可信，未注册任务；请先检查 ProgramData 中的 RevoHelper 文件。");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, SizeHigh, SizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    private static void RejectReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("辅助程序路径包含重解析点，未进行操作。");
    }

    private static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("请以管理员身份运行 RevoHelper 后重试。");
    }

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null;
        object? folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder("\\");
            return action(folder);
        }
        finally { Release(folder); Release(service); }
    }

    private static void Release(object? value)
    { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
