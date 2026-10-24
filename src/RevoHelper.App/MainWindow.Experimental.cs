using System.Windows;
using System.Windows.Controls;

namespace RevoHelper.App;

public partial class MainWindow
{
    private void MoreProfiles_Click(object sender, RoutedEventArgs e)
    { ViewModel.OpenMoreProfiles(); OpenDrawer(); }

    private async void DynamicPower_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox toggle)
            await ViewModel.Experiments.SetDynamicEnabledAsync(toggle.IsChecked == true);
    }

    private async void DynamicRecover_Click(object sender, RoutedEventArgs e) => await ViewModel.Experiments.RecoverAsync();
    private async void BootWaterCheck_Click(object sender, RoutedEventArgs e) => await ViewModel.Experiments.RefreshBootWaterAsync();
    private async void BootWaterInstall_Click(object sender, RoutedEventArgs e) => await ViewModel.Experiments.InstallBootWaterAsync();
    private async void BootWaterRemove_Click(object sender, RoutedEventArgs e) => await ViewModel.Experiments.RemoveBootWaterAsync();
}
