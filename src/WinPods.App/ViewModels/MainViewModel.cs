using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPods.App.Services;
using WinPods.Core.Abstractions;
using WinPods.Core.Models;

namespace WinPods.App.ViewModels;

/// <summary>トレイのポップアップに表示する状態。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AirPodsMonitor _monitor;
    private readonly IAudioProfileConnector _connector;
    private readonly IPairedDeviceProvider _pairedDeviceProvider;

    private PairedDevice? _targetDevice;
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
        try
        {
            IReadOnlyList<PairedDevice> devices =
                await _pairedDeviceProvider.GetPairedAudioDevicesAsync().ConfigureAwait(true);

            // v0.1 では最初に見つかったオーディオデバイスを対象にする。
            // TODO: 設定画面で対象デバイスを選べるようにする。
            _targetDevice = devices.FirstOrDefault();

            if (_targetDevice is null)
            {
                StatusText = "ペアリング済みのオーディオデバイスがありません";
                IsConnected = false;
                return;
            }

            IsConnected = _targetDevice.IsConnected;
            StatusText = IsConnected ? "接続済み" : "未接続";
        }
        catch (Exception ex)
        {
            StatusText = $"デバイスの取得に失敗しました: {ex.Message}";
        }
    }

    /// <summary>接続/ 切断を切り替える。</summary>
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

        IsBusy = true;
        StatusText = IsConnected ? "切断しています…" : "接続しています…";

        try
        {
            bool succeeded = IsConnected
                ? await _connector.DisconnectAsync(_targetDevice.Address).ConfigureAwait(true)
                : await _connector.ConnectAsync(_targetDevice.Address).ConfigureAwait(true);

            if (succeeded)
            {
                IsConnected = !IsConnected;
                StatusText = IsConnected ? "接続済み" : "未接続";
            }
            else
            {
                StatusText = "操作に失敗しました";
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
}
