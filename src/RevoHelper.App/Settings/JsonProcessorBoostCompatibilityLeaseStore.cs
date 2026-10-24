using System.IO;
using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed class JsonProcessorBoostCompatibilityLeaseStore(string path) : IProcessorBoostCompatibilityLeaseStore
{
    public async ValueTask<ProcessorBoostCompatibilityLease?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<ProcessorBoostCompatibilityLease>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("处理器兼容限制恢复记录为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("处理器兼容限制恢复记录已损坏。", exception);
        }
    }

    public async ValueTask SaveAsync(ProcessorBoostCompatibilityLease lease, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, lease, cancellationToken: cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        File.Delete(path + ".tmp");
        return ValueTask.CompletedTask;
    }
}
