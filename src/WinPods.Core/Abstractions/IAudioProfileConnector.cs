namespace WinPods.Core.Abstractions;

/// <summary>接続 / 切断の要求結果。</summary>
/// <param name="Succeeded">要求が受け付けられたかどうか。</param>
/// <param name="Detail">
/// 失敗した理由 (OS のエラーコードなど)。成功時は空文字列。
/// UI にそのまま出せる程度に短くまとめること。
/// </param>
public readonly record struct AudioProfileOperationResult(bool Succeeded, string Detail)
{
    public static AudioProfileOperationResult Success() => new(true, string.Empty);

    public static AudioProfileOperationResult Failure(string detail) => new(false, detail);
}

/// <summary>
/// ペアリング済み Bluetooth オーディオデバイスの A2DP / HFP 接続を制御する。
/// </summary>
public interface IAudioProfileConnector
{
    /// <summary>オーディオプロファイルを接続する。</summary>
    Task<AudioProfileOperationResult> ConnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default);

    /// <summary>オーディオプロファイルを切断する。</summary>
    Task<AudioProfileOperationResult> DisconnectAsync(
        ulong deviceAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在接続されているかどうかを問い合わせる。
    /// <b>判定できなかった場合は null</b> を返す (未接続と区別すること)。
    /// </summary>
    Task<bool?> IsConnectedAsync(ulong deviceAddress, CancellationToken cancellationToken = default);
}
