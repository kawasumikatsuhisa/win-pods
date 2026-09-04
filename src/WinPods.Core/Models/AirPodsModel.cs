namespace WinPods.Core.Models;

/// <summary>
/// Proximity Pairing メッセージの Device Model (byte 3-4) から判別した機種。
/// </summary>
public enum AirPodsModel
{
    Unknown = 0,
    AirPods1,
    AirPods2,
    AirPods3,
    AirPodsPro,
    AirPodsPro2,
    AirPodsMax,
    PowerbeatsPro,
    Powerbeats3,
    BeatsX,
    BeatsSolo3,
    BeatsSoloPro,
    BeatsFitPro,
}

public static class AirPodsModelExtensions
{
    /// <summary>
    /// 既知の Device Model ID (big-endian: byte3 &lt;&lt; 8 | byte4) と機種の対応表。
    /// 未知の ID は <see cref="AirPodsModel.Unknown"/> になるが、
    /// 生の ID は <c>ProximityPairingMessage.RawModelId</c> に保持される。
    /// </summary>
    public static AirPodsModel FromRawModelId(ushort rawModelId) => rawModelId switch
    {
        0x0220 => AirPodsModel.AirPods1,
        0x0F20 => AirPodsModel.AirPods2,
        0x1320 => AirPodsModel.AirPods3,
        0x0E20 => AirPodsModel.AirPodsPro,
        0x1420 => AirPodsModel.AirPodsPro2,
        0x0A20 => AirPodsModel.AirPodsMax,
        0x0B20 => AirPodsModel.PowerbeatsPro,
        0x0320 => AirPodsModel.Powerbeats3,
        0x0520 => AirPodsModel.BeatsX,
        0x0620 => AirPodsModel.BeatsSolo3,
        0x0C20 => AirPodsModel.BeatsSoloPro,
        0x1720 => AirPodsModel.BeatsFitPro,
        _ => AirPodsModel.Unknown,
    };

    /// <summary>UI 表示用の名称。</summary>
    public static string ToDisplayName(this AirPodsModel model) => model switch
    {
        AirPodsModel.AirPods1 => "AirPods",
        AirPodsModel.AirPods2 => "AirPods (第2世代)",
        AirPodsModel.AirPods3 => "AirPods (第3世代)",
        AirPodsModel.AirPodsPro => "AirPods Pro",
        AirPodsModel.AirPodsPro2 => "AirPods Pro (第2世代)",
        AirPodsModel.AirPodsMax => "AirPods Max",
        AirPodsModel.PowerbeatsPro => "Powerbeats Pro",
        AirPodsModel.Powerbeats3 => "Powerbeats3",
        AirPodsModel.BeatsX => "BeatsX",
        AirPodsModel.BeatsSolo3 => "Beats Solo3",
        AirPodsModel.BeatsSoloPro => "Beats Solo Pro",
        AirPodsModel.BeatsFitPro => "Beats Fit Pro",
        _ => "不明なデバイス",
    };

    /// <summary>
    /// 左右独立のイヤホンかどうか。false の場合 (AirPods Max など) は
    /// バッテリー表示を 1 つにまとめる。
    /// </summary>
    public static bool HasSeparateEarpieces(this AirPodsModel model) => model switch
    {
        AirPodsModel.AirPodsMax => false,
        AirPodsModel.Powerbeats3 => false,
        AirPodsModel.BeatsX => false,
        AirPodsModel.BeatsSolo3 => false,
        AirPodsModel.BeatsSoloPro => false,
        _ => true,
    };

    /// <summary>充電ケースを持つかどうか。</summary>
    public static bool HasCase(this AirPodsModel model) => model switch
    {
        AirPodsModel.AirPodsMax => false,
        AirPodsModel.Powerbeats3 => false,
        AirPodsModel.BeatsX => false,
        AirPodsModel.BeatsSolo3 => false,
        AirPodsModel.BeatsSoloPro => false,
        _ => true,
    };
}
