namespace WinPods.Core.Models;

/// <summary>
/// バッテリー残量。Proximity Pairing メッセージでは 4bit (0-10 が 0%-100%、
/// 15 = 未取得) でエンコードされるため、10% 刻みの値か「不明」のいずれかになる。
/// </summary>
public readonly record struct BatteryLevel
{
    /// <summary>値が取得できていないことを表す。</summary>
    public static BatteryLevel Unknown => default;

    private BatteryLevel(int percent)
    {
        Percent = percent;
        IsAvailable = true;
    }

    /// <summary>残量 (0-100)。<see cref="IsAvailable"/> が false のときは 0。</summary>
    public int Percent { get; }

    /// <summary>残量が取得できているかどうか。</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// アドバタイズ中の 4bit ニブルから生成する。
    /// 0-10 は 0%-100%、それ以外 (15 を含む) は「不明」として扱う。
    /// </summary>
    public static BatteryLevel FromNibble(int nibble) =>
        nibble is >= 0 and <= 10 ? new BatteryLevel(nibble * 10) : Unknown;

    public override string ToString() => IsAvailable ? $"{Percent}%" : "--";
}
