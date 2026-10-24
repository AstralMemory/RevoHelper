using RevoHelper.Core.Control.DynamicPower;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

public sealed class WindowsDynamicFrequencyBackend : IDynamicFrequencyBackend
{
    private readonly IPowerProfileApi api;
    private readonly Func<bool?> isAcPower;
    private static readonly Guid[] Settings =
    [
        WindowsProcessorFrequencyLimitController.MaximumFrequencySetting,
        WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting,
        WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting
    ];

    public WindowsDynamicFrequencyBackend(IPowerProfileApi api, Func<bool?>? isAcPower = null)
    { this.api = api; this.isAcPower = isAcPower ?? ReadAcPower; }

    public Task<DynamicPowerContext> GetContextAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ac = isAcPower() ?? throw new InvalidOperationException("Windows 未能确定 AC/DC 供电状态。");
        var context = new DynamicPowerContext(api.GetActiveScheme(), ac);
        DynamicPowerLeaseValidator.ValidateContext(context);
        return Task.FromResult(context);
    }

    public Task<DynamicFrequencyValues> ReadAsync(DynamicPowerContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DynamicPowerLeaseValidator.ValidateContext(context);
        var values = new DynamicFrequencyValues(Read(context, 0), Read(context, 1), Read(context, 2));
        DynamicPowerLeaseValidator.ValidateValues(values);
        return Task.FromResult(values);
    }

    public Task WriteAsync(DynamicPowerContext context, int processorClass, uint mhz, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DynamicPowerLeaseValidator.ValidateContext(context);
        if (processorClass is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(processorClass));
        if (mhz is not (0 or >= 100 and <= 64000)) throw new ArgumentOutOfRangeException(nameof(mhz));
        if (context.IsAc) api.WriteAcValue(context.Scheme, WindowsProcessorBoostController.ProcessorSubgroup, Settings[processorClass], mhz);
        else api.WriteDcValue(context.Scheme, WindowsProcessorBoostController.ProcessorSubgroup, Settings[processorClass], mhz);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(DynamicPowerContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DynamicPowerLeaseValidator.ValidateContext(context);
        // Refresh only if this exact scheme is still active; never reactivate a historical scheme.
        if (api.GetActiveScheme() == context.Scheme) api.SetActiveScheme(context.Scheme);
        return Task.CompletedTask;
    }

    private uint Read(DynamicPowerContext context, int processorClass) => context.IsAc
        ? api.ReadAcValue(context.Scheme, WindowsProcessorBoostController.ProcessorSubgroup, Settings[processorClass])
        : api.ReadDcValue(context.Scheme, WindowsProcessorBoostController.ProcessorSubgroup, Settings[processorClass]);

    private static bool? ReadAcPower()
    {
        if (!GetSystemPowerStatus(out var power)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return power.AcLineStatus switch { 0 => false, 1 => true, _ => null };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus powerStatus);
}
