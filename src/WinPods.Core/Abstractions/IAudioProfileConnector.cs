namespace WinPods.Core.Abstractions;

/// <summary>
/// ペアリング済み Bluetooth オーディオデバイスの A2DP / HFP 接続を制御する。
/// </summary>
public interface IAudioProfileConnector
{
    /// <summary>オーディオプロファイルを接続する。</summary>
    /// <returns>接続要求が成功した場合 true。</returns>
    Task<bool> ConnectAsync(ulong deviceAddress, CancellationToken cancellationToken = default);

    /// <summary>オーディオプロファイルを切断する。</summary>
    /// <returns>切断要求が成功した場合 true。</returns>
    Task<bool> DisconnectAsync(ulong deviceAddress, CancellationToken cancellationToken = default);

    /// <summary>現在接続されているかどうかを問い合わせる。</summary>
    Task<bool> IsConnectedAsync(ulong deviceAddress, CancellationToken cancellationToken = default);
}
