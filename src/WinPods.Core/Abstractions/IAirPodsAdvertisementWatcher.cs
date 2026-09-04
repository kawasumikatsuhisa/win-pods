namespace WinPods.Core.Abstractions;

/// <summary>
/// Apple Proximity Pairing のアドバタイズメントを監視する。
/// Windows 実装は <c>WinPods.Windows</c> にある。
/// </summary>
public interface IAirPodsAdvertisementWatcher : IDisposable
{
    /// <summary>Proximity Pairing メッセージを受信したときに発生する。</summary>
    event EventHandler<AirPodsAdvertisement>? AdvertisementReceived;

    /// <summary>スキャン中かどうか。</summary>
    bool IsRunning { get; }

    /// <summary>スキャンを開始する。</summary>
    void Start();

    /// <summary>スキャンを停止する。</summary>
    void Stop();
}
