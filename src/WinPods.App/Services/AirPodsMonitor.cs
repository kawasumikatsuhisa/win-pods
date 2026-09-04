using WinPods.Core.Abstractions;
using WinPods.Core.Proximity;

namespace WinPods.App.Services;

/// <summary>
/// BLE アドバタイズメントを購読し、「いま手元にある AirPods」の状態を保持する。
/// </summary>
/// <remarks>
/// <para>
/// <b>アドレスについての注意</b>: AirPods の BLE アドバタイズは
/// ランダマイズされたアドレスで送信されるため、A2DP/HFP の接続に使う
/// クラシック Bluetooth アドレスとは一致しない。したがって
/// 「どのアドバタイズがどのペアリング済みデバイスのものか」は直接には分からない。
/// v0.1 では「電波が最も強い Proximity Pairing の送信元＝ユーザーの AirPods」と
/// みなす単純な方針を採る (MagicPods 等と同様)。複数セットを使い分ける場合の
/// 厳密な突き合わせは今後の課題。
/// </para>
/// </remarks>
public sealed class AirPodsMonitor : IDisposable
{
    private readonly IAirPodsAdvertisementWatcher _watcher;
    private readonly LidStateTracker _lidTracker = new();
    private readonly object _gate = new();
    private readonly TimeSpan _stickyWindow = TimeSpan.FromSeconds(20);

    private ulong _currentAddress;
    private short _currentRssi = short.MinValue;
    private DateTimeOffset _lastAcceptedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public AirPodsMonitor(IAirPodsAdvertisementWatcher watcher)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _watcher.AdvertisementReceived += OnAdvertisementReceived;
    }

    /// <summary>状態が更新されたときに発生する。</summary>
    public event EventHandler<AirPodsAdvertisement>? StatusUpdated;

    /// <summary>ケースの蓋が開いたときに発生する。</summary>
    public event EventHandler<AirPodsAdvertisement>? LidOpened;

    /// <summary>直近に受信した状態。まだ 1 度も受信していない場合は null。</summary>
    public AirPodsAdvertisement? Latest { get; private set; }

    /// <summary>スキャンを開始する。</summary>
    public void Start() => _watcher.Start();

    /// <summary>スキャンを停止する。</summary>
    public void Stop() => _watcher.Stop();

    private void OnAdvertisementReceived(object? sender, AirPodsAdvertisement advertisement)
    {
        LidEvent lidEvent;

        lock (_gate)
        {
            if (!ShouldAccept(advertisement))
            {
                return;
            }

            _currentAddress = advertisement.DeviceAddress;
            _currentRssi = advertisement.Rssi;
            _lastAcceptedAt = advertisement.Timestamp;
            Latest = advertisement;

            lidEvent = _lidTracker.Update(advertisement.DeviceAddress, advertisement.Message);
        }

        StatusUpdated?.Invoke(this, advertisement);

        if (lidEvent == LidEvent.Opened)
        {
            LidOpened?.Invoke(this, advertisement);
        }
    }

    /// <summary>
    /// 追跡中のデバイスからのものか、あるいはより強い電波の別デバイスに
    /// 乗り換えるべきかを判定する。
    /// </summary>
    private bool ShouldAccept(AirPodsAdvertisement advertisement)
    {
        if (advertisement.DeviceAddress == _currentAddress)
        {
            return true;
        }

        // しばらく受信が途絶えていれば、乗り換え候補を無条件に受け入れる
        // (BLE アドレスのランダマイズで送信元が変わったケース)。
        if (advertisement.Timestamp - _lastAcceptedAt > _stickyWindow)
        {
            return true;
        }

        return advertisement.Rssi > _currentRssi;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.AdvertisementReceived -= OnAdvertisementReceived;
        _watcher.Dispose();
    }
}
