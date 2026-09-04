using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPods.App.Services;
using WinPods.Core.Abstractions;
using WinPods.Core.Models;

namespace WinPods.App.ViewModels;

/// <summary>トレイのポップアップに表示する状態。</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>接続状態を問い合わせる間隔。</summary>
    private static readonly TimeSpan ConnectionPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>接続 / 切断を要求してから実際に状態が変わるのを待つ時間。</summary>
    private static readonly TimeSpan ConnectionSettleTimeout = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan ConnectionSettlePollInterval = TimeSpan.FromMilliseconds(500);

    private readonly AirPodsMonitor _monitor;
    private readonly IAudioProfileConnector _connector;
    private readonly IPairedDeviceProvider _pairedDeviceProvider;
    private readonly DispatcherTimer _connectionPollTimer;

    private PairedDevice? _targetDevice;
    private bool _isPolling;
    private bool _disposed;

    private string _deviceName = "AirPods を検出中…";
    private string _statusText = "スキャン中";
    private string _leftBatteryText = "--";
    private string _rightBatteryText = "--";
    private string _caseBatteryText = "--";
    private bool _isLeftCharging;
    private bool _isRightCharging;
    private bool _isCaseCharging;
    private bool _hasCase = true;
    private bool _hasSeparateEarpieces = true;
    private bool _isConnected;
    private bool _isBusy;
    private DateTimeOffset? _lastUpdated;

    public MainViewModel(
        AirPodsMonitor monitor,
        IAudioProfileConnector connector,
        IPairedDeviceProvider pairedDeviceProvider)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _pairedDeviceProvider = pairedDeviceProvider ?? throw new ArgumentNullException(nameof(pairedDeviceProvider));

        _monitor.StatusUpdated += OnStatusUpdated;

        // 接続状態はこちらの操作以外でも変わる (iPhone に持っていかれる、
        // Windows 側から切断される、など) ので、実際の状態を定期的に問い合わせる。
        _connectionPollTimer = new DispatcherTimer { Interval = ConnectionPollInterval };
        _connectionPollTimer.Tick += OnConnectionPollTick;
        _connectionPollTimer.Start();
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LeftBatteryText
    {
        get => _leftBatteryText;
        private set => SetProperty(ref _leftBatteryText, value);
    }

    public string RightBatteryText
    {
        get => _rightBatteryText;
        private set => SetProperty(ref _rightBatteryText, value);
    }

    public string CaseBatteryText
    {
        get => _caseBatteryText;
        private set => SetProperty(ref _caseBatteryText, value);
    }

    public bool IsLeftCharging
    {
        get => _isLeftCharging;
        private set => SetProperty(ref _isLeftCharging, value);
    }

    public bool IsRightCharging
    {
        get => _isRightCharging;
        private set => SetProperty(ref _isRightCharging, value);
    }

    public bool IsCaseCharging
    {
        get => _isCaseCharging;
        private set => SetProperty(ref _isCaseCharging, value);
    }

    public bool HasCase
    {
        get => _hasCase;
        private set => SetProperty(ref _hasCase, value);
    }

    public bool HasSeparateEarpieces
    {
        get => _hasSeparateEarpieces;
        private set => SetProperty(ref _hasSeparateEarpieces, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionActionText));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    /// <summary>接続トグルボタンのラベル。</summary>
    public string ConnectionActionText => IsConnected ? "切断" : "接続";

    /// <summary>ペアリング済みデバイスの一覧を読み込み、接続状態を反映する。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // 接続 / 切断の処理中は、その結果で状態を更新するので割り込まない。
        if (IsBusy)
        {
            return;
        }

        try
        {
            IReadOnlyList<PairedDevice> devices =
                await _pairedDeviceProvider.GetPairedAudioDevicesAsync().ConfigureAwait(true);

            // v0.1 では最初に見つかったオーディオデバイスを対象にする。
            // TODO: 設定画面で対象デバイスを選べるようにする。
            _targetDevice = devices.FirstOrDefault();

            if (_targetDevice is null)
            {
                IsConnected = false;
                StatusText = "ペアリング済みのオーディオデバイスがありません";
                return;
            }

            ApplyConnectionState(_targetDevice.IsConnected);
        }
        catch (Exception ex)
        {
            StatusText = $"デバイスの取得に失敗しました: {ex.Message}";
        }
    }

    /// <summary>接続 / 切断を切り替える。</summary>
    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (_targetDevice is null)
        {
            await RefreshAsync().ConfigureAwait(true);
        }

        if (_targetDevice is null)
        {
            return;
        }

        bool shouldConnect = !IsConnected;

        IsBusy = true;
        StatusText = shouldConnect ? "接続しています…" : "切断しています…";

        try
        {
            AudioProfileOperationResult result = shouldConnect
                ? await _connector.ConnectAsync(_targetDevice.Address).ConfigureAwait(true)
                : await _connector.DisconnectAsync(_targetDevice.Address).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                // 原因を切り分けられるよう、OS が返したエラーをそのまま見せる。
                StatusText = $"操作に失敗しました: {result.Detail}";
                return;
            }

            // 要求が通っても実際に繋がる / 切れるまでには間があるので、
            // 楽観的にフラグを反転させず、実際の状態を確認してから反映する。
            bool? actual = await WaitForConnectionStateAsync(shouldConnect).ConfigureAwait(true);

            if (actual is not bool state)
            {
                StatusText = "接続状態を確認できませんでした";
                return;
            }

            ApplyConnectionState(state);

            if (state != shouldConnect)
            {
                StatusText = shouldConnect ? "接続できませんでした" : "切断できませんでした";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"操作に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>アプリを終了する。</summary>
    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    /// <summary>
    /// 期待した接続状態になるまで、タイムアウトまで問い合わせ続ける。
    /// 判定できなかった場合は null を返す。
    /// </summary>
    private async Task<bool?> WaitForConnectionStateAsync(bool expected)
    {
        ulong address = _targetDevice!.Address;
        DateTime deadline = DateTime.UtcNow + ConnectionSettleTimeout;
        bool? connected;

        do
        {
            connected = await _connector.IsConnectedAsync(address).ConfigureAwait(true);

            if (connected == expected)
            {
                return connected;
            }

            await Task.Delay(ConnectionSettlePollInterval).ConfigureAwait(true);
        }
        while (DateTime.UtcNow < deadline);

        return connected;
    }

    /// <summary>
    /// 定期的に実際の接続状態を問い合わせ、こちらの操作以外での変化に追随する。
    /// </summary>
    private async void OnConnectionPollTick(object? sender, EventArgs e)
    {
        if (_isPolling || IsBusy || _targetDevice is null)
        {
            return;
        }

        _isPolling = true;

        try
        {
            bool? connected = await _connector.IsConnectedAsync(_targetDevice.Address).ConfigureAwait(true);

            // 判定できなかったときは表示を変えない (未接続と区別する)。
            if (connected is bool state && state != IsConnected)
            {
                ApplyConnectionState(state);
            }
        }
        catch
        {
            // 一時的な問い合わせ失敗は次回の巡回に任せる。
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ApplyConnectionState(bool connected)
    {
        IsConnected = connected;
        StatusText = connected ? "接続済み" : "未接続";
    }

    private void OnStatusUpdated(object? sender, AirPodsAdvertisement advertisement)
    {
        // BLE のコールバックはワーカースレッドから来るので UI スレッドへ移す。
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(advertisement));
    }

    private void Apply(AirPodsAdvertisement advertisement)
    {
        var message = advertisement.Message;

        DeviceName = _targetDevice?.Name is { Length: > 0 } name
            ? name
            : message.Model.ToDisplayName();

        HasCase = message.Model.HasCase();
        HasSeparateEarpieces = message.Model.HasSeparateEarpieces();

        LeftBatteryText = message.LeftBattery.ToString();
        RightBatteryText = message.RightBattery.ToString();
        CaseBatteryText = message.CaseBattery.ToString();

        IsLeftCharging = message.IsLeftCharging;
        IsRightCharging = message.IsRightCharging;
        IsCaseCharging = message.IsCaseCharging;

        LastUpdated = advertisement.Timestamp;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectionPollTimer.Stop();
        _connectionPollTimer.Tick -= OnConnectionPollTick;
        _monitor.StatusUpdated -= OnStatusUpdated;
    }
}
