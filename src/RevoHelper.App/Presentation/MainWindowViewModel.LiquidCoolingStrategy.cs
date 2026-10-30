using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

public sealed partial class MainWindowViewModel
{
    private int selectedLiquidCoolingManualIndex = (int)LiquidCoolingFanMode.Balanced;

    public int SelectedLiquidCoolingManualIndex
    {
        get => selectedLiquidCoolingManualIndex;
        set
        {
            if (value is < 0 or > 3 || IsLiquidCoolingSmartStrategy || IsLiquidCoolingBusy
                || selectedLiquidCoolingManualIndex == value) return;
            selectedLiquidCoolingManualIndex = value;
            OnPropertyChanged();
        }
    }

    public bool CanChooseLiquidCoolingManualStrategy => CanControlLiquidCoolingStrategy && !IsLiquidCoolingSmartStrategy;

    private void SynchronizeLiquidCoolingManualReadback(LiquidCoolingFanMode mode)
    {
        if (mode is >= LiquidCoolingFanMode.Quieter and <= LiquidCoolingFanMode.Maximum)
        {
            selectedLiquidCoolingManualIndex = (int)mode;
            OnPropertyChanged(nameof(SelectedLiquidCoolingManualIndex));
        }
        OnPropertyChanged(nameof(CanChooseLiquidCoolingManualStrategy));
    }

    public async Task SetLiquidCoolingSmartAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!CanControlLiquidCoolingStrategy || isShuttingDown) return;
        try
        {
            await SetLiquidCoolingFanAsync(enabled ? LiquidCoolingFanMode.Auto
                : (LiquidCoolingFanMode)selectedLiquidCoolingManualIndex, cancellationToken);
        }
        finally
        {
            OnPropertyChanged(nameof(IsLiquidCoolingSmartStrategy));
            OnPropertyChanged(nameof(CanChooseLiquidCoolingManualStrategy));
        }
    }

    public async Task ApplyLiquidCoolingManualStrategyAsync(CancellationToken cancellationToken)
    {
        if (!CanChooseLiquidCoolingManualStrategy || isShuttingDown) return;
        var target = (LiquidCoolingFanMode)selectedLiquidCoolingManualIndex;
        await SetLiquidCoolingFanAsync(target, cancellationToken);
        // A rejected command must not leave the slider claiming an unconfirmed mode.
        SynchronizeLiquidCoolingManualReadback(SelectedLiquidCoolingFanMode);
    }
}
