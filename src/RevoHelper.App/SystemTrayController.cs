using System.Drawing;
using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;
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
            var active = action.IsCurrent(current);
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

    private void ExitApplication() => window.Dispatcher.Invoke(() =>
    {
        window.AllowApplicationClose = true;
        window.Close();
        Application.Current.Shutdown();
    });

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
