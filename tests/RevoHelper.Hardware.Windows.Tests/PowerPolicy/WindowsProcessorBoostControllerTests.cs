using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.Hardware.Windows.Tests.PowerPolicy;

public sealed class WindowsProcessorBoostControllerTests
{
    private static readonly Guid ActiveScheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    [Theory]
    [InlineData(ProcessorBoostMode.Disabled, 0u)]
    [InlineData(ProcessorBoostMode.Enabled, 1u)]
    [InlineData(ProcessorBoostMode.Aggressive, 2u)]
    [InlineData(ProcessorBoostMode.EfficientEnabled, 3u)]
    [InlineData(ProcessorBoostMode.EfficientAggressive, 4u)]
    [InlineData(ProcessorBoostMode.AggressiveAtGuaranteed, 5u)]
    [InlineData(ProcessorBoostMode.EfficientAggressiveAtGuaranteed, 6u)]
    public void Mode_UsesWindowsIndex(ProcessorBoostMode mode, uint expected) =>
        Assert.Equal(expected, (uint)mode);

    [Fact]
    public async Task ReadAsync_ReturnsAcAndDcValuesFromActiveScheme()
    {
        var api = new FakePowerProfileApi { AcValue = 3, DcValue = 0 };
        var controller = new WindowsProcessorBoostController(api);

        var policy = await controller.ReadAsync(CancellationToken.None);

        Assert.Equal(new ProcessorBoostPolicy(
            ProcessorBoostMode.EfficientEnabled,
            ProcessorBoostMode.Disabled), policy);
        Assert.Equal(ActiveScheme, api.LastReadScheme);
    }

    [Fact]
    public async Task ApplyAsync_WritesBothValuesAndReactivatesScheme()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy(
                ProcessorBoostMode.EfficientAggressive,
                ProcessorBoostMode.Disabled),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains((WindowsProcessorBoostController.BoostModeSetting, 4u), api.AcSettingWrites);
        Assert.Contains((WindowsProcessorBoostController.BoostModeSetting, 0u), api.DcSettingWrites);
        Assert.Equal(ActiveScheme, api.ReactivatedScheme);
    }

    [Fact]
    public async Task ApplyAsync_DisabledAlsoUsesMaximumProcessorStateCompatibilityLimit()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains((WindowsProcessorBoostController.MaximumProcessorStateSetting, 99u), api.AcSettingWrites);
        Assert.Contains((WindowsProcessorBoostController.MaximumProcessorStateSetting, 99u), api.DcSettingWrites);
    }

    [Fact]
    public async Task ApplyAsync_EnabledPreservesUnownedMaximumProcessorState()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(100u, api.MaximumAc);
        Assert.Equal(100u, api.MaximumDc);
        Assert.DoesNotContain(api.AcSettingWrites, write => write.Setting == WindowsProcessorBoostController.MaximumProcessorStateSetting);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotRaiseExistingUserMaximumProcessorState()
    {
        var api = new FakePowerProfileApi { MaximumAc = 80, MaximumDc = 70 };
        var controller = new WindowsProcessorBoostController(api);

        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None)).Succeeded);
        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), CancellationToken.None)).Succeeded);

        Assert.Equal(80u, api.MaximumAc);
        Assert.Equal(70u, api.MaximumDc);
    }

    [Fact]
    public async Task ApplyAsync_EnabledRestoresOnlyOwnedCompatibilityGuard()
    {
        var api = new FakePowerProfileApi { MaximumDc = 70 };
        var controller = new WindowsProcessorBoostController(api);
        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None)).Succeeded);
        Assert.Equal(99u, api.MaximumAc);
        Assert.Equal(70u, api.MaximumDc);

        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), CancellationToken.None)).Succeeded);
        Assert.Equal(100u, api.MaximumAc);
        Assert.Equal(70u, api.MaximumDc);
    }

    [Fact]
    public async Task ApplyAsync_UserSelected99PercentIsNeverRaisedTo100()
    {
        var api = new FakePowerProfileApi { MaximumAc = 99, MaximumDc = 99 };
        var controller = new WindowsProcessorBoostController(api);
        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), CancellationToken.None)).Succeeded);
        Assert.Equal(99u, api.MaximumAc);
        Assert.Equal(99u, api.MaximumDc);
    }

    [Fact]
    public async Task RecoverAsync_AfterRestart_RestoresGuardButNotBoostChoiceOrExternalCap()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var first = new WindowsProcessorBoostController(api, store);
        Assert.True((await first.ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None)).Succeeded);
        Assert.NotNull(store.Lease);
        api.MaximumDc = 65;

        var recovered = await new WindowsProcessorBoostController(api, store).RecoverAsync(CancellationToken.None);

        Assert.True(recovered.Succeeded, recovered.Reason);
        Assert.Equal(100u, api.MaximumAc);
        Assert.Equal(65u, api.MaximumDc);
        Assert.Equal(0u, api.AcValue);
        Assert.Equal(0u, api.DcValue);
        Assert.Null(store.Lease);
    }

    [Fact]
    public async Task ApplyAsync_AfterExternalCapChange_PreservesExternalValue()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorBoostController(api);
        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None)).Succeeded);
        api.MaximumAc = 60;

        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), CancellationToken.None)).Succeeded);
        Assert.Equal(60u, api.MaximumAc);
        Assert.Equal(100u, api.MaximumDc);
    }

    [Fact]
    public async Task ApplyAsync_RecoveryJournalFailure_PreventsAnyWrites()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore { FailSave = true };
        var result = await new WindowsProcessorBoostController(api, store).ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(api.AcSettingWrites);
        Assert.Empty(api.DcSettingWrites);
    }

    [Fact]
    public async Task RecoverAsync_WhenReadbackDoesNotRestore_KeepsRecoveryJournal()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorBoostController(api, store);
        Assert.True((await controller.ApplyAsync(
            new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled), CancellationToken.None)).Succeeded);
        api.IgnoreMaximumWrites = true;

        var result = await controller.RecoverAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(store.Lease);
        Assert.Equal(99u, api.MaximumAc);
    }

    [Fact]
    public async Task ApplyAsync_ReportsNativeFailureWithoutThrowing()
    {
        var api = new FakePowerProfileApi { WriteFailure = new InvalidOperationException("denied") };
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("denied", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_WhenDcWriteFails_RestoresPreviousAcAndDcValues()
    {
        var api = new FakePowerProfileApi
        {
            AcValue = 2,
            DcValue = 3,
            FailNextDcWrite = true
        };
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal([0u, 2u], api.AcSettingWrites
            .Where(write => write.Setting == WindowsProcessorBoostController.BoostModeSetting)
            .Select(write => write.Value));
        Assert.Equal([3u], api.DcSettingWrites
            .Where(write => write.Setting == WindowsProcessorBoostController.BoostModeSetting)
            .Select(write => write.Value));
        Assert.Equal(ActiveScheme, api.ReactivatedScheme);
        Assert.Contains("已恢复", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnknownModeWithoutWriting()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorBoostController(api);

        var result = await controller.ApplyAsync(
            new ProcessorBoostPolicy((ProcessorBoostMode)7, ProcessorBoostMode.Enabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(api.WrittenAcValue);
        Assert.Null(api.WrittenDcValue);
    }

    private sealed class FakePowerProfileApi : IPowerProfileApi
    {
        public uint AcValue { get; set; } = 2;
        public uint DcValue { get; set; } = 2;
        public uint MaximumAc { get; set; } = 100;
        public uint MaximumDc { get; set; } = 100;
        public bool IgnoreMaximumWrites { get; set; }
        public Guid LastReadScheme { get; private set; }
        public uint? WrittenAcValue { get; private set; }
        public uint? WrittenDcValue { get; private set; }
        public Guid? ReactivatedScheme { get; private set; }
        public Exception? WriteFailure { get; set; }
        public bool FailNextDcWrite { get; set; }
        public List<uint> AcWrites { get; } = [];
        public List<uint> DcWrites { get; } = [];
        public List<(Guid Setting, uint Value)> AcSettingWrites { get; } = [];
        public List<(Guid Setting, uint Value)> DcSettingWrites { get; } = [];

        public Guid GetActiveScheme() => ActiveScheme;

        public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting)
        {
            LastReadScheme = scheme;
            return setting == WindowsProcessorBoostController.MaximumProcessorStateSetting ? MaximumAc : AcValue;
        }

        public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting)
        {
            LastReadScheme = scheme;
            return setting == WindowsProcessorBoostController.MaximumProcessorStateSetting ? MaximumDc : DcValue;
        }

        public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            if (WriteFailure is not null) throw WriteFailure;
            WrittenAcValue = value;
            AcWrites.Add(value);
            AcSettingWrites.Add((setting, value));
            if (setting == WindowsProcessorBoostController.MaximumProcessorStateSetting)
            { if (!IgnoreMaximumWrites) MaximumAc = value; }
            else AcValue = value;
        }

        public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            if (WriteFailure is not null) throw WriteFailure;
            if (FailNextDcWrite)
            {
                FailNextDcWrite = false;
                throw new InvalidOperationException("dc denied");
            }
            WrittenDcValue = value;
            DcWrites.Add(value);
            DcSettingWrites.Add((setting, value));
            if (setting == WindowsProcessorBoostController.MaximumProcessorStateSetting)
            { if (!IgnoreMaximumWrites) MaximumDc = value; }
            else DcValue = value;
        }

        public void SetActiveScheme(Guid scheme) => ReactivatedScheme = scheme;
    }

    private sealed class FakeLeaseStore : IProcessorBoostCompatibilityLeaseStore
    {
        public ProcessorBoostCompatibilityLease? Lease { get; private set; }
        public bool FailSave { get; init; }
        public ValueTask<ProcessorBoostCompatibilityLease?> LoadAsync(CancellationToken token) => ValueTask.FromResult(Lease);
        public ValueTask SaveAsync(ProcessorBoostCompatibilityLease lease, CancellationToken token)
        {
            if (FailSave) throw new IOException("journal write denied");
            Lease = lease;
            return ValueTask.CompletedTask;
        }
        public ValueTask ClearAsync(CancellationToken token)
        { Lease = null; return ValueTask.CompletedTask; }
    }
}
