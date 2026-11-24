using System.Drawing;
using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;
using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;

namespace RevoHelper.App;

public sealed class SystemTrayController : IDisposable
{
    private readonly MainWindow window;
    private readonly MainWindowViewModel viewModel;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Font boldMenuFont;
    private readonly Forms.ToolStripMenuItem showItem;
    private readonly Forms.ToolStripMenuItem exitItem;
    private readonly Dictionary<TrayProfileAction, Forms.ToolStripMenuItem> profileItems = [];
    private readonly List<Image> profileImages = [];
    private bool disposed;
    private bool exitRequested;
    private bool exitWaitingLong;
    private bool forcedExitAsked;
    private bool exitClosing;

    public SystemTrayController(MainWindow window, MainWindowViewModel viewModel)
    {
        this.window = window;
        this.viewModel = viewModel;
        menu = new Forms.ContextMenuStrip();
        boldMenuFont = new Font(menu.Font, System.Drawing.FontStyle.Bold);
        foreach (var action in TrayProfileAction.All)
        {
            var image = CreateProfileDot(action.DotColor);
            profileImages.Add(image);
            var item = new Forms.ToolStripMenuItem(action.Label) { Image = image };
            item.Click += async (_, _) => await ApplyAsync(action).ConfigureAwait(true);
            menu.Items.Add(item);
            profileItems[action] = item;
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        showItem = new Forms.ToolStripMenuItem();
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(showItem);
        exitItem = new Forms.ToolStripMenuItem();
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            Text = "RevoHelper",
            Icon = ExtractIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowWindow();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshProfileMarkers();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfileId) or
            nameof(MainWindowViewModel.SelectedTurboStrategy) or
            nameof(MainWindowViewModel.SelectedCustomProfileIndex) or
            nameof(MainWindowViewModel.IsBasicTurboActive) or
            nameof(MainWindowViewModel.SelectedUiLanguageCode))
        {
            window.Dispatcher.BeginInvoke(RefreshProfileMarkers);
        }
    }

    private void RefreshProfileMarkers()
    {
        var current = viewModel.ActivePlatformSelection;
        var english = viewModel.SelectedUiLanguageCode == "en-US";
        showItem.Text = english ? "Show RevoHelper" : "显示 RevoHelper";
        exitItem.Text = english ? "Exit" : "退出";
        foreach (var (action, item) in profileItems)
        {
            item.Text = !english ? action.Label : action.Selection.Mode switch
            {
                RevoHelper.Core.Control.PlatformMode.Office => "Office",
                RevoHelper.Core.Control.PlatformMode.Balanced => "Balanced",
                RevoHelper.Core.Control.PlatformMode.TurboQuiet => "Turbo · Quiet",
                RevoHelper.Core.Control.PlatformMode.TurboHigh => "Turbo · High",
                _ => $"Custom {action.Selection.ProfileIndex}"
            };
            // Basic Turbo matches the quiet entry for switching, but quiet is not actually on.
            var active = action.IsCurrent(current) &&
                         !(viewModel.IsBasicTurboActive && action.Selection.Mode is RevoHelper.Core.Control.PlatformMode.TurboQuiet);
            item.Checked = active;
            item.Font = active ? boldMenuFont : menu.Font;
        }
    }

    private static Bitmap CreateProfileDot(string hexColor)
    {
        var bitmap = new Bitmap(14, 14);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(ColorTranslator.FromHtml(hexColor));
        graphics.FillEllipse(brush, 2, 2, 10, 10);
        return bitmap;
    }

    private async Task ApplyAsync(TrayProfileAction action)
    {
        try
        {
            var applyTask = await window.Dispatcher.InvokeAsync(() =>
                viewModel.ApplyPlatformSelectionFromTrayAsync(action.Selection, CancellationToken.None));
            await applyTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            notifyIcon.ShowBalloonTip(3000, viewModel.SelectedUiLanguageCode == "en-US" ? "RevoHelper profile change failed" : "RevoHelper 调度失败", exception.Message, Forms.ToolTipIcon.Error);
        }
    }

    private void ShowWindow() => window.Dispatcher.Invoke(() =>
    {
        window.Show();
        if (window.WindowState is WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    });

    // Stop new performance changes and let the one under way (switch, apply or rollback) end
    // first, however long it takes, so App.OnExit can restore frequency, PL and Boost without
    // racing it; the preset PL baseline lives only in memory, so skipping its restore would
    // lose it. This waits on the UI thread without blocking it: the change's holder needs that
    // thread to finish. A note appears if the wait runs past a few seconds; from then on,
    // choosing Exit again offers a forced exit, for a change that never ends because a
    // hardware call hangs.
    private void ExitApplication() => window.Dispatcher.BeginInvoke(new Action(async () =>
    {
        if (exitClosing) return;
        if (exitRequested)
        {
            if (exitWaitingLong && !forcedExitAsked) OfferForcedExit();
            return;
        }
        exitRequested = true;
        try
        {
            var settled = viewModel.PrepareForExitAsync(Timeout.InfiniteTimeSpan);
            if (await Task.WhenAny(settled, Task.Delay(TimeSpan.FromSeconds(3))) != settled)
            {
                exitWaitingLong = true;
                var english = viewModel.SelectedUiLanguageCode == "en-US";
                notifyIcon.ShowBalloonTip(5000,
                    english ? "RevoHelper is finishing a change" : "RevoHelper 正在完成调整",
                    english
                        ? "It exits once the current performance change has ended. If it stays stuck, choose Exit again to force it."
                        : "正在等待当前的性能调整完成，完成后自动退出。如果一直没有反应，可以再次点击“退出”强制退出。",
                    Forms.ToolTipIcon.Info);
            }
            await settled;
        }
        catch (Exception exception)
        {
            RuntimeDiagnosticLog.Write("exit-recovery", $"{exception.GetType().Name}: {exception.Message}");
        }
        CloseApplication();
    }));

    // The change still holds the performance gate, so App.OnExit cannot claim it and skips the
    // restores that would race the change: the frequency records (the dynamic layer too) and the
    // temporary processor-state record stay for the next start to try, and the preset PL
    // baseline, kept only in memory, is lost.
    private void OfferForcedExit()
    {
        forcedExitAsked = true;
        var english = viewModel.SelectedUiLanguageCode == "en-US";
        var choice = MessageBox.Show(
            english
                ? "The current performance change has not finished. The hardware may not be responding.\n\n" +
                  "If RevoHelper exits now:\n" +
                  "• The change may be left half done.\n" +
                  "• The next time RevoHelper starts, it tries to restore the frequency cap and temporary processor limits.\n" +
                  "• The PL power limits set in this session may stay until Windows restarts.\n\n" +
                  "Force RevoHelper to exit? Choose No to keep waiting."
                : "当前的性能调整还没有完成，可能是硬件没有响应。\n\n" +
                  "如果现在强制退出：\n" +
                  "• 这次调整可能只完成了一部分；\n" +
                  "• 下次打开 RevoHelper 时，会尝试恢复频率上限和临时的处理器限制；\n" +
                  "• 本会话的 PL 功耗设置可能会保留到重启电脑。\n\n" +
                  "要强制退出吗？选择“否”会继续等待。",
            english ? "Force RevoHelper to exit?" : "强制退出 RevoHelper？",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        forcedExitAsked = false;
        // The change may have ended while the question was open, and the normal exit run.
        if (choice != MessageBoxResult.Yes || exitClosing) return;
        RuntimeDiagnosticLog.Write("exit-recovery", "Forced exit chosen while a performance change was still running.");
        CloseApplication();
    }

    private void CloseApplication()
    {
        if (exitClosing) return;
        exitClosing = true;
        window.AllowApplicationClose = true;
        window.Close();
        Application.Current.Shutdown();
    }

    private static Icon ExtractIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
        foreach (var image in profileImages) image.Dispose();
        boldMenuFont.Dispose();
    }
}
