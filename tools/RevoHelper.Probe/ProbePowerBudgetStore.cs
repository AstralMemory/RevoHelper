using RevoHelper.Hardware.Windows.OemControl;

internal sealed class ProbePowerBudgetStore : IGpuPowerBudgetStore
{
    private readonly Dictionary<int, GpuPowerBudgetRecord> records = [];
    public GpuPowerBudgetRecord? Read(int profile) => records.GetValueOrDefault(profile);
    public void Write(int profile, GpuPowerBudgetRecord record) => records[profile] = record;
}
