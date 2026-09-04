using WinPods.Core.Proximity;

namespace WinPods.Core.Abstractions;

/// <summary>受信した 1 件のアドバタイズメント。</summary>
/// <param name="DeviceAddress">アドバタイズ元の Bluetooth アドレス (48bit)。</param>
/// <param name="Rssi">受信信号強度 (dBm)。近接判定に使う。</param>
/// <param name="Timestamp">受信時刻。</param>
/// <param name="Message">デコード済みの Proximity Pairing メッセージ。</param>
public sealed record AirPodsAdvertisement(
    ulong DeviceAddress,
    short Rssi,
    DateTimeOffset Timestamp,
    ProximityPairingMessage Message);
