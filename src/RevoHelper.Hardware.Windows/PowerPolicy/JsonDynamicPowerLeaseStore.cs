using RevoHelper.Core.Control.DynamicPower;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

public sealed class JsonDynamicPowerLeaseStore : IDynamicPowerLeaseStore
{
    private readonly string path;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8
    };

    public JsonDynamicPowerLeaseStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public async Task<DynamicPowerLease?> LoadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length > 16384) throw new InvalidDataException("动态功耗恢复记录过大。");
            var lease = await JsonSerializer.DeserializeAsync<DynamicPowerLease>(stream, Options, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("动态功耗恢复记录为空。");
            DynamicPowerLeaseValidator.Validate(lease);
            return lease;
        }
    }

    public async Task SaveAsync(DynamicPowerLease lease, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DynamicPowerLeaseValidator.Validate(lease);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, lease, Options, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            // Same-directory rename/replace is atomic. Never truncate the existing recovery journal.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public Task ClearAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }
}
