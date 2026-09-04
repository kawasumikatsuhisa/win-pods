using System.Diagnostics.CodeAnalysis;
using WinPods.Core.Models;

namespace WinPods.Core.Proximity;

/// <summary>
/// BLE アドバタイズメントの Manufacturer Data (Company ID 0x004C) に含まれる
/// Apple Proximity Pairing メッセージをデコードする。
/// </summary>
/// <remarks>
/// このクラスは OS 非依存 (純粋な byte 列の解釈) にしてあるため、
/// Windows 以外でも単体テストできる。
/// </remarks>
public static class ProximityPairingParser
{
    /// <summary>Apple の Bluetooth SIG Company Identifier。</summary>
    public const ushort AppleCompanyId = 0x004C;

    /// <summary>Continuity メッセージ種別: Proximity Pairing。</summary>
    public const byte MessageType = 0x07;

    /// <summary>Proximity Pairing メッセージの長さフィールドの値 (25 バイト)。</summary>
    public const byte MessageLength = 0x19;

    /// <summary>type(1) + length(1) + payload(25) = 27 バイト。</summary>
    public const int ExpectedTotalLength = 2 + MessageLength;

    // status バイト (byte 5) のビット。
    // bit5 が立っているときプライマリは右、落ちているとき左。
    private const byte StatusPrimaryIsRightMask = 0x20;

    // 充電フラグ (byte 7 の上位ニブル)。
    // bit0/bit1 はバッテリーニブルの並び順 (下位ニブル側 / 上位ニブル側) に対応する。
    private const int ChargingLowNibblePodMask = 0x01;
    private const int ChargingHighNibblePodMask = 0x02;
    private const int ChargingCaseMask = 0x04;

    /// <summary>
    /// Manufacturer Data (Company ID を<b>含まない</b>本体部分) をデコードする。
    /// </summary>
    /// <param name="manufacturerData">
    /// <c>BluetoothLEManufacturerData.Data</c> の中身。先頭が 0x07 0x19 で始まる 27 バイト。
    /// </param>
    /// <param name="message">デコード結果。失敗時は null。</param>
    /// <returns>Proximity Pairing メッセージとして解釈できた場合 true。</returns>
    public static bool TryParse(
        ReadOnlySpan<byte> manufacturerData,
        [NotNullWhen(true)] out ProximityPairingMessage? message)
    {
        message = null;

        if (manufacturerData.Length < ExpectedTotalLength)
        {
            return false;
        }

        if (manufacturerData[0] != MessageType || manufacturerData[1] != MessageLength)
        {
            return false;
        }

        ushort rawModelId = (ushort)((manufacturerData[3] << 8) | manufacturerData[4]);
        byte status = manufacturerData[5];
        byte podsBattery = manufacturerData[6];
        byte caseByte = manufacturerData[7];
        byte lidIndicator = manufacturerData[8];
        byte color = manufacturerData[9];

        // status の bit5 が立っていれば「上位ニブル = 右」、落ちていれば「上位ニブル = 左」。
        bool primaryIsRight = (status & StatusPrimaryIsRightMask) != 0;

        int highNibble = (podsBattery >> 4) & 0x0F;
        int lowNibble = podsBattery & 0x0F;

        int leftNibble = primaryIsRight ? lowNibble : highNibble;
        int rightNibble = primaryIsRight ? highNibble : lowNibble;

        int chargingFlags = (caseByte >> 4) & 0x0F;
        int caseBatteryNibble = caseByte & 0x0F;

        bool highNibblePodCharging = (chargingFlags & ChargingHighNibblePodMask) != 0;
        bool lowNibblePodCharging = (chargingFlags & ChargingLowNibblePodMask) != 0;

        message = new ProximityPairingMessage
        {
            RawModelId = rawModelId,
            Model = AirPodsModelExtensions.FromRawModelId(rawModelId),
            LeftBattery = BatteryLevel.FromNibble(leftNibble),
            RightBattery = BatteryLevel.FromNibble(rightNibble),
            CaseBattery = BatteryLevel.FromNibble(caseBatteryNibble),
            IsLeftCharging = primaryIsRight ? lowNibblePodCharging : highNibblePodCharging,
            IsRightCharging = primaryIsRight ? highNibblePodCharging : lowNibblePodCharging,
            IsCaseCharging = (chargingFlags & ChargingCaseMask) != 0,
            PrimaryPod = primaryIsRight ? PodSide.Right : PodSide.Left,
            RawStatus = status,
            RawLidIndicator = lidIndicator,
            RawColor = color,
            RawHex = Convert.ToHexString(manufacturerData),
        };

        return true;
    }
}
