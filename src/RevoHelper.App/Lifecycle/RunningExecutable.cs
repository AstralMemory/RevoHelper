using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RevoHelper.App.Lifecycle;

/// <summary>
/// Where the running RevoHelper.exe is now. Windows lets a running program file be moved or
/// renamed on the same drive: Environment.ProcessPath keeps the place it started from, while the
/// process image name follows the file. Anything that starts or copies this program asks here
/// each time. It never falls back to the old place, where another file may be by now.
/// </summary>
public static class RunningExecutable
{
    public const string NotFoundMessage =
        "找不到 RevoHelper 程序文件现在的位置，它可能已被移动或删除。请退出 RevoHelper，再从程序文件所在的位置重新打开。";
    public const string RecycledMessage =
        "RevoHelper 程序文件已被移到回收站。请退出 RevoHelper，再从保留的程序文件重新打开。";

    /// <summary>This program's file as it is now.</summary>
    /// <exception cref="InvalidOperationException">It cannot be confirmed; the message says what to do.</exception>
    public static string Locate() => Locate(CurrentImage(), File.Exists);

    public static string Locate(string? image, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(image) || !Path.IsPathFullyQualified(image))
            throw new InvalidOperationException(NotFoundMessage);
        // A file in the Recycle Bin is on its way out; nothing is started or registered from there.
        if (InRecycleBin(image)) throw new InvalidOperationException(RecycledMessage);
        if (!exists(image)) throw new InvalidOperationException(NotFoundMessage);
        return image;
    }

    /// <summary>This program's file as it is now, or null when that cannot be confirmed.</summary>
    public static string? TryLocate() => TryLocate(CurrentImage(), File.Exists);

    public static string? TryLocate(string? image, Func<string, bool> exists)
    {
        try { return Path.GetFullPath(Locate(image, exists)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>How a helper copy of this program is started: that file, in its own folder, hidden.</summary>
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? throw new InvalidOperationException(NotFoundMessage)
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static bool InRecycleBin(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase));

    private static string? CurrentImage()
    {
        var buffer = new char[512];
        var length = buffer.Length;
        if (QueryFullProcessImageName(GetCurrentProcess(), 0, buffer, ref length)) return new string(buffer, 0, length);
        if (Marshal.GetLastPInvokeError() != InsufficientBuffer) return null;
        buffer = new char[32768];
        length = buffer.Length;
        return QueryFullProcessImageName(GetCurrentProcess(), 0, buffer, ref length) ? new string(buffer, 0, length) : null;
    }

    private const int InsufficientBuffer = 122;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, [Out] char[] name, ref int size);
}
