namespace WinPods.Core.Abstractions;

/// <summary>ペアリング済みデバイスの情報。</summary>
/// <param name="Address">Bluetooth アドレス (48bit)。</param>
/// <param name="Name">OS に登録されている表示名。</param>
/// <param name="IsConnected">現在接続されているかどうか。</param>
public sealed record PairedDevice(ulong Address, string Name, bool IsConnected);

/// <summary>OS にペアリング済みのオーディオデバイスを列挙する。</summary>
public interface IPairedDeviceProvider
{
    /// <summary>ペアリング済みのオーディオデバイスを列挙する。</summary>
    Task<IReadOnlyList<PairedDevice>> GetPairedAudioDevicesAsync(
        CancellationToken cancellationToken = default);
}
