using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RevoHelper.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim chargeLimitCommitGate = new(1, 1);
    private long chargeLimitCommitVersion;

    private async void ExperimentalChargeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle) return;
        try
        {
            var charge = ViewModel.Experiments.Charge;
            if (!IsLoaded || !charge.CanToggle) return;
            var enabled = toggle.IsChecked == true;
            await charge.SetEnabledAsync(enabled);
        }
        finally { toggle.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget(); }
    }

    private void ExperimentalChargeLimit_Commit(object sender, MouseButtonEventArgs e) =>
        QueueExperimentalChargeLimitCommit(sender);

    private void ExperimentalChargeLimit_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            QueueExperimentalChargeLimitCommit(sender);
    }

    private void QueueExperimentalChargeLimitCommit(object sender)
    {
        // No ValueChanged handler: bindings and initialization never apply a charge limit.
        if (!IsLoaded || sender is not Slider slider || !ViewModel.Experiments.Charge.CanChooseLimit) return;
        slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
        var version = Interlocked.Increment(ref chargeLimitCommitVersion);
        _ = CommitExperimentalChargeLimitAsync(version);
    }

    private async Task CommitExperimentalChargeLimitAsync(long version)
    {
        await chargeLimitCommitGate.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref chargeLimitCommitVersion) && IsLoaded)
                await ViewModel.Experiments.Charge.CommitSelectedLimitAsync();
        }
        finally { chargeLimitCommitGate.Release(); }
    }

    private async void ExperimentalChargeRecover_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.Experiments.Charge.RecoverAsync();
}
