using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using System.Text.Json;

namespace RevoHelper.App.Tests.Settings;

public sealed class JsonProcessorFrequencyLimitLeaseStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"RevoHelper.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoad_RoundTripsLeaseAtomically()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        var lease = new ProcessorFrequencyLimitLease(
            1,
            Guid.NewGuid(),
            ProcessorFrequencyLimitPolicy.Uniform(0),
            ProcessorFrequencyLimitPolicy.Uniform(3100));

        await store.SaveAsync(lease, CancellationToken.None);

        Assert.Equal(lease, await store.LoadAsync(CancellationToken.None));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Clear_RemovesLeaseAndTemporaryFile()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        await store.SaveAsync(new ProcessorFrequencyLimitLease(
            1, Guid.NewGuid(), ProcessorFrequencyLimitPolicy.Uniform(0),
            ProcessorFrequencyLimitPolicy.Uniform(3100)), CancellationToken.None);
        File.WriteAllText(path + ".tmp", "stale");

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.LoadAsync(CancellationToken.None));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Load_MalformedJournal_ThrowsInsteadOfDiscardingRecoveryEvidence()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "not-json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":1,\"Scheme\":\"11111111-2222-3333-4444-555555555555\",\"Original\":{},\"Expected\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Scheme\":\"11111111-2222-3333-4444-555555555555\",\"Original\":null,\"Expected\":null}")]
    public async Task Load_MissingOrNullRecoveryFields_RejectsAndPreservesBytes(string json)
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, json);
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.LoadAsync(default));

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(2, false, 3100u)]
    [InlineData(1, true, 3100u)]
    [InlineData(1, false, 99u)]
    [InlineData(1, false, 64001u)]
    public async Task Load_InvalidRecoveryValues_RejectsWithoutDiscardingJournal(
        int version, bool emptyScheme, uint frequency)
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        var lease = new ProcessorFrequencyLimitLease(version,
            emptyScheme ? Guid.Empty : Guid.NewGuid(),
            ProcessorFrequencyLimitPolicy.Uniform(0), ProcessorFrequencyLimitPolicy.Uniform(frequency));
        var json = JsonSerializer.Serialize(lease);
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new JsonProcessorFrequencyLimitLeaseStore(path).LoadAsync(default));

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Load_OversizedOtherwiseValidJournal_RejectsWithoutDiscardingIt()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(NewLease()) + new string(' ', 16384);
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new JsonProcessorFrequencyLimitLeaseStore(path).LoadAsync(default));

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Save_InvalidLease_LeavesExistingJournalUntouched()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        var original = NewLease();
        await store.SaveAsync(original, default);
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.SaveAsync(original with { Original = null! }, default));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task Save_Cancelled_LeavesExistingJournalUntouched()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        var original = NewLease();
        await store.SaveAsync(original, default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.SaveAsync(original with { Expected = ProcessorFrequencyLimitPolicy.Uniform(4200) },
                cancellation.Token));

        Assert.Equal(original, await store.LoadAsync(default));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task Save_ReplacementDenied_PreservesPreviousJournalAndRemovesOwnTemporaryFile()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        var original = NewLease();
        await store.SaveAsync(original, default);
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(async () =>
                await store.SaveAsync(original with { Expected = ProcessorFrequencyLimitPolicy.Uniform(4200) }, default));
        }

        Assert.Equal(original, await store.LoadAsync(default));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ArchiveCorrupt_ZeroFilledJournal_CopiesExactBytesAndLeavesActiveJournalIntact()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        var bytes = new byte[459];
        await File.WriteAllBytesAsync(path, bytes);
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);

        await store.ArchiveCorruptAsync(default);
        await store.ArchiveCorruptAsync(default);

        var archives = Directory.GetFiles(directory, "frequency-lease.json.corrupt-*");
        Assert.Equal(2, archives.Length);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        foreach (var archive in archives)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(archive));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.LoadAsync(default));

        await store.ClearAsync(default);
        Assert.All(archives, archive => Assert.True(File.Exists(archive)));
    }

    [Fact]
    public async Task ArchiveCorrupt_ValidJournal_RejectsWithoutModifyingOrCopyingIt()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        var lease = NewLease();
        await store.SaveAsync(lease, default);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ArchiveCorruptAsync(default));

        Assert.Equal(lease, await store.LoadAsync(default));
        Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
    }

    [Fact]
    public async Task ArchiveCorrupt_MissingJournal_RejectsWithoutCreatingDirectory()
    {
        var store = new JsonProcessorFrequencyLimitLeaseStore(Path.Combine(directory, "frequency-lease.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ArchiveCorruptAsync(default));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ArchiveCorrupt_Cancelled_PreservesJournalWithoutCreatingCopy()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "invalid-journal");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.ArchiveCorruptAsync(cancellation.Token));

        Assert.Equal("invalid-journal", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
    }

    [Fact]
    public async Task ArchiveCorrupt_InaccessibleJournal_PropagatesFailureWithoutCopying()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "invalid-journal");
        var store = new JsonProcessorFrequencyLimitLeaseStore(path);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAnyAsync<IOException>(async () => await store.ArchiveCorruptAsync(default));

        Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
    }

    [Fact]
    public async Task Load_CorruptJournal_DoesNotFallBackToOldTemporaryOrArchive()
    {
        var path = Path.Combine(directory, "frequency-lease.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "invalid-journal");
        var json = JsonSerializer.Serialize(NewLease());
        await File.WriteAllTextAsync(path + ".tmp", json);
        await File.WriteAllTextAsync(path + ".corrupt-old", json);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new JsonProcessorFrequencyLimitLeaseStore(path).LoadAsync(default));

        Assert.Equal("invalid-journal", await File.ReadAllTextAsync(path));
    }

    private static ProcessorFrequencyLimitLease NewLease() => new(
        1, Guid.NewGuid(), ProcessorFrequencyLimitPolicy.Uniform(0),
        ProcessorFrequencyLimitPolicy.Uniform(3100));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
