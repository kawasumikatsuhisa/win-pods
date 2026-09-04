using System.Threading;
using System.Windows;
using WinPods.App.Services;
using WinPods.App.ViewModels;
using WinPods.App.Views;
using WinPods.Windows.Bluetooth;

namespace WinPods.App;

/// <summary>
/// アプリのエントリポイント。ウィンドウを持たず、タスクトレイに常駐する。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\WinPods.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private AirPodsMonitor? _monitor;
    private MainViewModel? _viewModel;
    private TrayIconHost? _trayIconHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // 既に起動しているのでこのプロセスは終了する。
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        try
        {
            Compose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"初期化に失敗しました。\n\n{ex}",
                "WinPods",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void Compose()
    {
        var watcher = new BluetoothLeAirPodsWatcher();
        _monitor = new AirPodsMonitor(watcher);

        _viewModel = new MainViewModel(
            _monitor,
            new BluetoothServiceStateConnector(),
            new WindowsPairedDeviceProvider(),
            new BluetoothDiagnostics());

        _trayIconHost = new TrayIconHost(_viewModel, _monitor);

        _monitor.Start();

        // 起動直後にペアリング済みデバイスと接続状態を読み込む。
        _viewModel.RefreshCommand.Execute(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconHost?.Dispose();
        _viewModel?.Dispose();
        _monitor?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
