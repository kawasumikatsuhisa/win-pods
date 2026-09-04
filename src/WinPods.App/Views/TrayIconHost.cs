using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using WinPods.App.Services;
using WinPods.App.ViewModels;
using WinPods.Core.Abstractions;
using WinPods.Core.Models;

namespace WinPods.App.Views;

/// <summary>
/// タスクトレイのアイコンとポップアップの表示を受け持つ。
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private static readonly TimeSpan LidPopupDuration = TimeSpan.FromSeconds(6);

    private readonly MainViewModel _viewModel;
    private readonly AirPodsMonitor _monitor;
    private readonly TaskbarIcon _trayIcon;
    private readonly PopupWindow _popup;
    private readonly DispatcherTimer _autoHideTimer;
    private bool _disposed;

    public TrayIconHost(MainViewModel viewModel, AirPodsMonitor monitor)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));

        _popup = new PopupWindow { DataContext = _viewModel };

        _autoHideTimer = new DispatcherTimer { Interval = LidPopupDuration };
        _autoHideTimer.Tick += OnAutoHideTick;

        _trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute)),
            ToolTipText = "WinPods",
            ContextMenu = BuildContextMenu(),
            DataContext = _viewModel,
        };

        _trayIcon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _trayIcon.ForceCreate();

        _monitor.LidOpened += OnLidOpened;
        _monitor.StatusUpdated += OnStatusUpdated;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu { DataContext = _viewModel };

        var toggle = new MenuItem();
        toggle.SetBinding(HeaderedItemsControl.HeaderProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ConnectionActionText)));
        toggle.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ToggleConnectionCommand)));

        var refresh = new MenuItem { Header = "状態を更新" };
        refresh.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(nameof(MainViewModel.RefreshCommand)));

        var exit = new MenuItem { Header = "終了" };
        exit.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ExitCommand)));

        menu.Items.Add(toggle);
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        return menu;
    }

    private void OnTrayLeftMouseUp(object sender, RoutedEventArgs e)
    {
        if (_popup.IsVisible)
        {
            _popup.Hide();
            return;
        }

        _autoHideTimer.Stop();
        ShowPopup();
    }

    /// <summary>ケースの蓋が開いたら、しばらくの間ポップアップを表示する。</summary>
    private void OnLidOpened(object? sender, AirPodsAdvertisement advertisement)
    {
        _popup.Dispatcher.BeginInvoke(() =>
        {
            ShowPopup();
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        });
    }

    private void OnStatusUpdated(object? sender, AirPodsAdvertisement advertisement)
    {
        _trayIcon.Dispatcher.BeginInvoke(() =>
        {
            var message = advertisement.Message;
            _trayIcon.ToolTipText = message.Model.HasSeparateEarpieces()
                ? $"{_viewModel.DeviceName}\n左 {_viewModel.LeftBatteryText} / 右 {_viewModel.RightBatteryText} / ケース {_viewModel.CaseBatteryText}"
                : $"{_viewModel.DeviceName}\n{_viewModel.RightBatteryText}";
        });
    }

    /// <summary>ポップアップを開く。開くたびに実際の接続状態を取り直す。</summary>
    private void ShowPopup()
    {
        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }

        _popup.ShowNearTray();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        _popup.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _monitor.LidOpened -= OnLidOpened;
        _monitor.StatusUpdated -= OnStatusUpdated;
        _autoHideTimer.Stop();
        _autoHideTimer.Tick -= OnAutoHideTick;
        _trayIcon.TrayLeftMouseUp -= OnTrayLeftMouseUp;
        _trayIcon.Dispose();
        _popup.Close();
    }
}
