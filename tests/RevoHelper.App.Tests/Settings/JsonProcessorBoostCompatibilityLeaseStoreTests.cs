using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class JsonProcessorBoostCompatibilityLeaseStoreTests
{
    [Fact]
    public async Task RoundTripAndClear_PreserveOnlyCompatibilityRecoveryState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"RevoHelper.BoostLease.Tests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "boost-lease.json");
        try
        {
            var lease = new ProcessorBoostCompatibilityLease(1, Guid.NewGuid(), 100, 70, 99, 70);
            var store = new JsonProcessorBoostCompatibilityLeaseStore(path);
            await store.SaveAsync(lease, CancellationToken.None);
            Assert.Equal(lease, await new JsonProcessorBoostCompatibilityLeaseStore(path).LoadAsync(CancellationToken.None));
            Assert.False(File.Exists(path + ".tmp"));
            await store.ClearAsync(CancellationToken.None);
            Assert.Null(await store.LoadAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
