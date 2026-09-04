using WinPods.Core.Models;

namespace WinPods.Core.Proximity;

/// <summary>
/// Apple Proximity Pairing (Continuity メッセージ type 0x07) をデコードした結果。
/// バイト配置の詳細は <c>docs/proximity-pairing.md</c> を参照。
/// </summary>
public sealed record ProximityPairingMessage
{
    /// <summary>Device Model ID (byte 3-4 を big-endian で読んだ値)。</summary>
    public required ushort RawModelId { get; init; }

    /// <summary>判別した機種。未知の ID の場合は <see cref="AirPodsModel.Unknown"/>。</summary>
    public required AirPodsModel Model { get; init; }

    /// <summary>左イヤホンのバッテリー残量。</summary>
    public required BatteryLevel LeftBattery { get; init; }

    /// <summary>右イヤホンのバッテリー残量。</summary>
    public required BatteryLevel RightBattery { get; init; }

    /// <summary>ケースのバッテリー残量。</summary>
    public required BatteryLevel CaseBattery { get; init; }

    /// <summary>左イヤホンが充電中かどうか。</summary>
    public required bool IsLeftCharging { get; init; }

    /// <summary>右イヤホンが充電中かどうか。</summary>
    public required bool IsRightCharging { get; init; }

    /// <summary>ケースが充電中かどうか。</summary>
    public required bool IsCaseCharging { get; init; }

    /// <summary>
    /// プライマリ (親機) 側のイヤホン。status バイトの bit5 で判別する。
    /// バッテリーニブルと充電フラグの左右並び順がこれで入れ替わる。
    /// </summary>
    public required PodSide PrimaryPod { get; init; }

    /// <summary>status バイト (byte 5) の生値。装着検出などの解析用に保持する。</summary>
    public required byte RawStatus { get; init; }

    /// <summary>Lid Open Counter バイト (byte 8) の生値。</summary>
    public required byte RawLidIndicator { get; init; }

    /// <summary>Device Color バイト (byte 9) の生値。</summary>
    public required byte RawColor { get; init; }

    /// <summary>受信した manufacturer data 全体の16進表現 (デバッグ・解析用)。</summary>
    public required string RawHex { get; init; }

    /// <summary>
    /// ケースの蓋を開けた回数のカウンタ (下位3bit)。
    /// 蓋を開けるたびにインクリメントされるため、値の変化を「蓋が開いた」イベントとして扱える。
    /// </summary>
    public int LidOpenCounter => RawLidIndicator & 0x07;

    /// <summary>
    /// 蓋が開いているかどうかの推定値 (bit3 が 0 のとき開)。
    /// <b>この bit の解釈は実機での検証が必要</b>。確実性が要るところでは
    /// <see cref="LidOpenCounter"/> の変化 (<c>LidStateTracker</c>) を使うこと。
    /// </summary>
    public bool IsLidOpenHint => (RawLidIndicator & 0x08) == 0;
}
