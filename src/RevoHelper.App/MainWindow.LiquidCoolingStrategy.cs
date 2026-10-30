using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RevoHelper.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim liquidStrategyCommitGate = new(1, 1);
    private long liquidStrategyCommitVersion;

    private async void LiquidCoolingSmartStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle) return;
        try
        {
            if (!IsLoaded || !ViewModel.CanControlLiquidCoolingStrategy) return;
            await ViewModel.SetLiquidCoolingSmartAsync(toggle.IsChecked == true, CancellationToken.None);
        }
        finally { toggle.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget(); }
    }

    private void LiquidCoolingManualStrategy_Commit(object sender, MouseButtonEventArgs e) =>
        QueueLiquidCoolingManualStrategyCommit(sender);

    private void LiquidCoolingManualStrategy_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            QueueLiquidCoolingManualStrategyCommit(sender);
    }

    private void QueueLiquidCoolingManualStrategyCommit(object sender)
    {
        if (!IsLoaded || sender is not Slider slider || !ViewModel.CanChooseLiquidCoolingManualStrategy) return;
        slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
        var version = Interlocked.Increment(ref liquidStrategyCommitVersion);
        _ = CommitLiquidCoolingManualStrategyAsync(version);
    }

    private async Task CommitLiquidCoolingManualStrategyAsync(long version)
    {
        await liquidStrategyCommitGate.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref liquidStrategyCommitVersion) && IsLoaded && ViewModel.CanChooseLiquidCoolingManualStrategy)
                await ViewModel.ApplyLiquidCoolingManualStrategyAsync(CancellationToken.None);
        }
        finally { liquidStrategyCommitGate.Release(); }
    }
}
