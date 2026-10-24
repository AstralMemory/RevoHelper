using System.Text.Json;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.Hardware.Windows.Tests.PowerPolicy;

public sealed class JsonDynamicPowerLeaseStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RevoHelper-dynamic-tests-" + Guid.NewGuid().ToString("N"));
    private string Journal => Path.Combine(directory, "dynamic.json");
    private static DynamicPowerLease Lease => new(1, new(Guid.NewGuid(), true), new(0, 2000, 5000), new(3000, 2000, 3000));

    [Fact]
    public async Task MissingJournalReturnsNullAndAtomicSaveCanReplaceThenClear()
    {
        var store = new JsonDynamicPowerLeaseStore(Journal);
        Assert.Null(await store.LoadAsync(default));
        var lease = Lease;
        await store.SaveAsync(lease, default);
        Assert.Equal(lease, await store.LoadAsync(default));
        var next = Lease;
        await store.SaveAsync(next, default);
        Assert.Equal(next, await store.LoadAsync(default));
        Assert.Single(Directory.GetFiles(directory));
        await store.ClearAsync(default);
        Assert.Null(await store.LoadAsync(default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{bad json")]
    [InlineData("{\"schemaVersion\":1,\"context\":{\"scheme\":\"invalid\",\"isAc\":true},\"original\":{},\"expected\":{}}")]
    public async Task MalformedJournalIsRetainedAndRejected(string json)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Journal, json);
        var store = new JsonDynamicPowerLeaseStore(Journal);
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync(default));
        Assert.Equal(json, await File.ReadAllTextAsync(Journal));
    }

    [Fact]
    public async Task InvalidSchemaAndUnexpectedTargetNeverSave()
    {
        var store = new JsonDynamicPowerLeaseStore(Journal);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Lease with { SchemaVersion = 99 }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Lease with { Expected = new(64000, 64000, 64000) }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Lease with { Context = new(Guid.Empty, true) }, default));
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task CanceledSavePreservesExistingJournal()
    {
        var store = new JsonDynamicPowerLeaseStore(Journal);
        var lease = Lease;
        await store.SaveAsync(lease, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Lease, new(true)));
        Assert.Equal(lease, await store.LoadAsync(default));
    }

    [Fact]
    public async Task ValidJsonWithUnrecognizedPropertiesIsRejected()
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(Lease, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json = json[..^1] + ",\"settingPath\":\"arbitrary\"}";
        await File.WriteAllTextAsync(Journal, json);
        await Assert.ThrowsAnyAsync<Exception>(() => new JsonDynamicPowerLeaseStore(Journal).LoadAsync(default));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
