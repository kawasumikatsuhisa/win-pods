namespace WinPods.Core.Abstractions;

/// <summary>
/// 接続まわりの不具合を切り分けるための診断情報を作る。
/// </summary>
public interface IBluetoothDiagnostics
{
    /// <summary>
    /// 対象デバイスについて、OS が認識している情報を人が読める形にまとめる。
    /// 副作用は持たない (接続状態を変更しない)。
    /// </summary>
    Task<string> CreateReportAsync(
        ulong deviceAddress,
        string deviceName,
        CancellationToken cancellationToken = default);
}
