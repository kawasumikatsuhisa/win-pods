using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace WinPods.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Windows の Bluetooth 設定を開く。</summary>
    [RelayCommand]
    private void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:bluetooth",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Bluetooth 設定を開けませんでした: {ex.Message}";
        }
    }
}
