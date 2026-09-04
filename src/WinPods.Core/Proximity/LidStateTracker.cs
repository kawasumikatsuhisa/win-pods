namespace WinPods.Core.Proximity;

/// <summary>ケースの蓋の状態変化。</summary>
public enum LidEvent
{
    /// <summary>変化なし (または初回観測でベースラインを取っただけ)。</summary>
    None,

    /// <summary>蓋が開いた。</summary>
    Opened,

    /// <summary>蓋が閉じた。</summary>
    Closed,
}

/// <summary>
/// Lid Open Counter (byte 8) の変化からケースの開閉を検出する。
/// </summary>
/// <remarks>
/// <para>
/// bit3 の「開/閉」ビットは実機での裏取りが済んでいないため、
/// より確実な「蓋を開けるたびに下位3bitのカウンタが増える」という性質を主に使う。
/// </para>
/// <para>
/// このクラスはスレッドセーフではない。BLE のコールバックから使う場合は
/// 呼び出し側で直列化するか UI スレッドにマーシャリングすること。
/// </para>
/// </remarks>
public sealed class LidStateTracker
{
    private readonly Dictionary<ulong, byte> _lastIndicatorByAddress = new();

    /// <summary>
    /// 新しく受信したメッセージを反映し、検出した開閉イベントを返す。
    /// </summary>
    /// <param name="deviceAddress">アドバタイズ元の Bluetooth アドレス。</param>
    /// <param name="message">受信した Proximity Pairing メッセージ。</param>
    public LidEvent Update(ulong deviceAddress, ProximityPairingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte current = message.RawLidIndicator;

        if (!_lastIndicatorByAddress.TryGetValue(deviceAddress, out byte previous))
        {
            // 初回はベースラインを取るだけ。起動直後に誤ってポップアップを出さない。
            _lastIndicatorByAddress[deviceAddress] = current;
            return LidEvent.None;
        }

        if (previous == current)
        {
            return LidEvent.None;
        }

        _lastIndicatorByAddress[deviceAddress] = current;

        int previousCounter = previous & 0x07;
        int currentCounter = current & 0x07;

        if (previousCounter != currentCounter)
        {
            return LidEvent.Opened;
        }

        bool previousOpen = (previous & 0x08) == 0;
        bool currentOpen = (current & 0x08) == 0;

        if (previousOpen == currentOpen)
        {
            return LidEvent.None;
        }

        return currentOpen ? LidEvent.Opened : LidEvent.Closed;
    }

    /// <summary>指定デバイスの記録を破棄する (ペアリング解除時など)。</summary>
    public void Forget(ulong deviceAddress) => _lastIndicatorByAddress.Remove(deviceAddress);

    /// <summary>すべての記録を破棄する。</summary>
    public void Clear() => _lastIndicatorByAddress.Clear();
}
