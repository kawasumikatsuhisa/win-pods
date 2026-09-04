using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using WinPods.Core.Abstractions;
using static WinPods.Windows.Bluetooth.NativeMethods;

namespace WinPods.Windows.Bluetooth;

/// <summary>
/// Windows が対象デバイスについて何を認識しているかを列挙する。
/// </summary>
/// <remarks>
/// <c>BluetoothSetServiceState</c> が ERROR_INVALID_PARAMETER を返す場合、
/// 指定したサービス GUID が「インストール済みサービス」に含まれていないことが多い。
/// このレポートで実際に使える GUID を確認する。読み取りのみで状態は変更しない。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class BluetoothDiagnostics : IBluetoothDiagnostics
{
    /// <summary>よく使われるサービスクラス GUID の短縮名。</summary>
    private static readonly Dictionary<Guid, string> KnownServices = new()
    {
        [new Guid("00001108-0000-1000-8000-00805F9B34FB")] = "Headset (HSP)",
        [new Guid("0000110A-0000-1000-8000-00805F9B34FB")] = "AudioSource (A2DP Source)",
        [new Guid("0000110B-0000-1000-8000-00805F9B34FB")] = "AudioSink (A2DP Sink)",
        [new Guid("0000110C-0000-1000-8000-00805F9B34FB")] = "AVRCP Target",
        [new Guid("0000110D-0000-1000-8000-00805F9B34FB")] = "AdvancedAudio (A2DP)",
        [new Guid("0000110E-0000-1000-8000-00805F9B34FB")] = "AVRCP",
        [new Guid("0000110F-0000-1000-8000-00805F9B34FB")] = "AVRCP Controller",
        [new Guid("00001112-0000-1000-8000-00805F9B34FB")] = "Headset AG",
        [new Guid("0000111E-0000-1000-8000-00805F9B34FB")] = "Handsfree (HFP)",
        [new Guid("0000111F-0000-1000-8000-00805F9B34FB")] = "Handsfree AG",
        [new Guid("00001200-0000-1000-8000-00805F9B34FB")] = "PnP Information",
        [new Guid("00001203-0000-1000-8000-00805F9B34FB")] = "Generic Audio",
    };

    /// <inheritdoc />
    public Task<string> CreateReportAsync(
        ulong deviceAddress,
        string deviceName,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateReport(deviceAddress, deviceName), cancellationToken);

    private static string CreateReport(ulong deviceAddress, string deviceName)
    {
        var report = new StringBuilder();

        report.AppendLine("=== WinPods 診断情報 ===");
        report.AppendLine(CultureInfo.InvariantCulture, $"日時: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion.VersionString}");
        report.AppendLine(CultureInfo.InvariantCulture, $"64bit プロセス: {Environment.Is64BitProcess}");
        report.AppendLine();
        report.AppendLine("[対象デバイス]");
        report.AppendLine(CultureInfo.InvariantCulture, $"名前: {deviceName}");
        report.AppendLine(CultureInfo.InvariantCulture, $"アドレス: {FormatAddress(deviceAddress)}");
        report.AppendLine();

        int radioIndex = 0;

        int radioCount = BluetoothRadios.ForEach(radio =>
        {
            radioIndex++;
            report.AppendLine(CultureInfo.InvariantCulture, $"[ラジオ #{radioIndex}]");

            uint lookupError = BluetoothRadios.GetDeviceInfo(radio, deviceAddress, out BLUETOOTH_DEVICE_INFO info);
            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"BluetoothGetDeviceInfo: {BluetoothRadios.DescribeError(lookupError)}");

            if (lookupError != ERROR_SUCCESS)
            {
                report.AppendLine();
                return false;
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"  名前: {info.szName}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  接続: {info.fConnected}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  登録済み: {info.fRemembered}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  認証済み: {info.fAuthenticated}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  Class of Device: 0x{info.ulClassofDevice:X6}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  構造体サイズ: {info.dwSize}");

            AppendInstalledServices(report, radio, ref info);

            report.AppendLine();
            return true;
        });

        report.AppendLine(CultureInfo.InvariantCulture, $"検出したラジオ数: {radioCount}");

        if (radioCount == 0)
        {
            report.AppendLine("Bluetooth アダプタが列挙できませんでした。");
        }

        return report.ToString();
    }

    private static void AppendInstalledServices(
        StringBuilder report,
        IntPtr radio,
        ref BLUETOOTH_DEVICE_INFO info)
    {
        uint count = 0;
        uint result = BluetoothEnumerateInstalledServices(radio, ref info, ref count, null);

        if (result != ERROR_SUCCESS && result != ERROR_MORE_DATA)
        {
            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"  インストール済みサービス: 取得失敗 {BluetoothRadios.DescribeError(result)}");
            return;
        }

        if (count == 0)
        {
            report.AppendLine("  インストール済みサービス: 0 件");
            return;
        }

        var services = new Guid[count];
        result = BluetoothEnumerateInstalledServices(radio, ref info, ref count, services);

        if (result != ERROR_SUCCESS)
        {
            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"  インストール済みサービス: 取得失敗 {BluetoothRadios.DescribeError(result)}");
            return;
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"  インストール済みサービス: {count} 件");

        for (int i = 0; i < count; i++)
        {
            Guid service = services[i];
            string name = KnownServices.TryGetValue(service, out string? known) ? known : "(不明)";
            report.AppendLine(CultureInfo.InvariantCulture, $"    {service:B} {name}");
        }
    }

    private static string FormatAddress(ulong address)
    {
        byte[] bytes = BitConverter.GetBytes(address);
        return string.Join(':', bytes.Take(6).Reverse().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
