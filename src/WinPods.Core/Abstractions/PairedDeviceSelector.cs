namespace WinPods.Core.Abstractions;

/// <summary>
/// ペアリング済み Bluetooth オーディオデバイスから WinPods の操作対象を選ぶ。
/// </summary>
/// <remarks>
/// OS の列挙順は操作対象の優先順位ではないため、単純な FirstOrDefault は使わない。
/// AirPods / Beats と判断できる名前を優先し、名前で判断できない場合も
/// 「接続中が 1 台だけ」「候補が 1 台だけ」のときだけ安全に選択する。
/// </remarks>
public static class PairedDeviceSelector
{
    private static readonly string[] PreferredNameTokens =
    [
        "AirPods",
        "Beats",
        "Powerbeats",
    ];

    /// <summary>
    /// WinPods の操作対象として十分に特定できるデバイスを返す。
    /// 複数候補から安全に決められない場合は null。
    /// </summary>
    public static PairedDevice? Select(IReadOnlyList<PairedDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (devices.Count == 0)
        {
            return null;
        }

        PairedDevice[] preferred = devices
            .Where(static device => LooksLikeSupportedHeadphones(device.Name))
            .ToArray();

        if (preferred.Length > 0)
        {
            // 接続中の AirPods / Beats を最優先。複数ある場合は OS の列挙順を維持する。
            return preferred.FirstOrDefault(static device => device.IsConnected) ?? preferred[0];
        }

        PairedDevice[] connected = devices.Where(static device => device.IsConnected).ToArray();

        // ユーザーが AirPods の表示名を変更している場合でも、接続中が 1 台だけなら
        // そのデバイスを対象にできる。
        if (connected.Length == 1)
        {
            return connected[0];
        }

        // オーディオ機器自体が 1 台だけなら名前に依存せず採用する。
        return devices.Count == 1 ? devices[0] : null;
    }

    private static bool LooksLikeSupportedHeadphones(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return PreferredNameTokens.Any(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
