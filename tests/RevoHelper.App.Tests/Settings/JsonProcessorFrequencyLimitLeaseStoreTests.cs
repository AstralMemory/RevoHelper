using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

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

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
