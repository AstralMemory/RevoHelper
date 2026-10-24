using System.IO;
using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed class JsonProcessorFrequencyLimitLeaseStore(string path)
    : IProcessorFrequencyLimitLeaseStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
            var lease = await JsonSerializer.DeserializeAsync<ProcessorFrequencyLimitLease>(
                stream, Options, cancellationToken).ConfigureAwait(false);
            return lease ?? throw new InvalidDataException("处理器频率恢复记录为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("处理器频率恢复记录已损坏。", exception);
        }
    }

    public async ValueTask SaveAsync(
        ProcessorFrequencyLimitLease lease,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream, lease, Options, cancellationToken).ConfigureAwait(false);
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
