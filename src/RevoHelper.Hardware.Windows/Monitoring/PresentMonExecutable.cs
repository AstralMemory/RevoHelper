using System.Buffers;

namespace RevoHelper.Hardware.Windows.Monitoring;

public static class PresentMonExecutable
{
    public const string Version = "2.6.0";
    public const string FileName = "PresentMon-2.6.0-x64.exe";
    public const string ResourceName = "RevoHelper.Resources.PresentMon.PresentMon-2.6.0-x64.exe";

    /// <summary>Extracts the bundled upstream CLI into a versioned private cache, not beside the portable EXE.</summary>
    public static async Task<string> EnsureExtractedAsync(CancellationToken cancellationToken = default)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevoHelper", "Components", "PresentMon", Version);
        Directory.CreateDirectory(cacheDirectory);
        var executablePath = Path.Combine(cacheDirectory, FileName);
        await using var resource = typeof(PresentMonExecutable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException("未找到内嵌的 FPS 采集组件。");
        // Do not overwrite a component that is currently executing in another RevoHelper instance.
        if (await MatchesResourceAsync(executablePath, resource, cancellationToken).ConfigureAwait(false))
        {
            return executablePath;
        }
        resource.Position = 0;

        var temporaryPath = executablePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await resource.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, executablePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return executablePath;
    }

    private static async Task<bool> MatchesResourceAsync(string path, Stream resource, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != resource.Length) return false;
        await using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var expectedBuffer = ArrayPool<byte>.Shared.Rent(65536);
        var existingBuffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            // Compare to the trusted embedded bytes, not just file size; a replaced cache must never execute.
            int count;
            while ((count = await resource.ReadAsync(expectedBuffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                var read = await existing.ReadAtLeastAsync(existingBuffer.AsMemory(0, count), count, false, cancellationToken).ConfigureAwait(false);
                if (read != count || !expectedBuffer.AsSpan(0, count).SequenceEqual(existingBuffer.AsSpan(0, count))) return false;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(expectedBuffer);
            ArrayPool<byte>.Shared.Return(existingBuffer);
        }
    }
}
