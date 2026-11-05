using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed class JsonProcessorFrequencyLimitLeaseStore(string path)
    : IProcessorFrequencyLimitLeaseStore
{
    private readonly string journalPath = Path.GetFullPath(path);
    private const int MaximumJournalBytes = 16384;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public async ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try { stream = OpenReader(); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        await using (stream.ConfigureAwait(false))
            return await ReadLeaseAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<ProcessorFrequencyLimitLease> ReadLeaseAsync(
        FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length > MaximumJournalBytes)
            throw new InvalidDataException("处理器频率恢复记录过大。");
        try
        {
            var lease = await JsonSerializer.DeserializeAsync<ProcessorFrequencyLimitLease>(
                stream, Options, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("处理器频率恢复记录为空。");
            Validate(lease);
            return lease;
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
        cancellationToken.ThrowIfCancellationRequested();
        Validate(lease);
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);

        var temporaryPath = journalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = OpenDurableWriter(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, lease, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(journalPath)) File.Replace(temporaryPath, journalPath, null);
            else File.Move(temporaryPath, journalPath);
        }
        finally { TryDeleteTemporary(temporaryPath); }
    }

    public async ValueTask ArchiveCorruptAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream source;
        try { source = OpenReader(); }
        catch (FileNotFoundException exception)
        { throw new InvalidOperationException("没有损坏的处理器频率恢复记录可归档。", exception); }
        catch (DirectoryNotFoundException exception)
        { throw new InvalidOperationException("没有损坏的处理器频率恢复记录可归档。", exception); }

        await using (source.ConfigureAwait(false))
        {
            var corrupt = false;
            try { await ReadLeaseAsync(source, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException) { corrupt = true; }
            if (!corrupt)
                throw new InvalidOperationException("有效的处理器频率恢复记录不能作为损坏记录归档。");

            // Validate and copy the same open file. Denying writes/deletes while it is
            // open prevents archiving a different journal after the validity check.
            var archivePath = journalPath + ".corrupt-" + Guid.NewGuid().ToString("N");
            var completed = false;
            try
            {
                source.Position = 0;
                await using (var archive = OpenDurableWriter(archivePath))
                {
                    await source.CopyToAsync(archive, cancellationToken).ConfigureAwait(false);
                    await archive.FlushAsync(cancellationToken).ConfigureAwait(false);
                    archive.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                completed = true;
            }
            finally
            {
                if (!completed) TryDeleteTemporary(archivePath);
            }
        }
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(journalPath);
        File.Delete(journalPath + ".tmp");
        return ValueTask.CompletedTask;
    }

    private FileStream OpenReader() => new(
        journalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
        4096, FileOptions.Asynchronous);

    private static FileStream OpenDurableWriter(string target) => new(
        target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        4096, FileOptions.Asynchronous | FileOptions.WriteThrough);

    private static void Validate(ProcessorFrequencyLimitLease? lease)
    {
        if (lease is null || lease.SchemaVersion != 1 || lease.Scheme == Guid.Empty ||
            !IsValid(lease.Original) || !IsValid(lease.Expected))
            throw new InvalidDataException("处理器频率恢复记录无效。");
    }

    private static bool IsValid(ProcessorFrequencyLimitPolicy? policy) => policy is not null &&
        IsValidValue(policy.AcMaximumMhz) && IsValidValue(policy.DcMaximumMhz) &&
        IsValidValue(policy.AcClass1MaximumMhz) && IsValidValue(policy.DcClass1MaximumMhz) &&
        IsValidValue(policy.AcClass2MaximumMhz) && IsValidValue(policy.DcClass2MaximumMhz);

    private static bool IsValidValue(uint value) => value is 0 or >= 100 and <= 64000;

    private static void TryDeleteTemporary(string temporaryPath)
    {
        try { File.Delete(temporaryPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
