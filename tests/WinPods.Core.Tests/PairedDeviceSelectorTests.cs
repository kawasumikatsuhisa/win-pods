using WinPods.Core.Abstractions;
using Xunit;

namespace WinPods.Core.Tests;

public class PairedDeviceSelectorTests
{
    [Fact]
    public void Select_AirPodsを他のオーディオ機器より優先する()
    {
        PairedDevice[] devices =
        [
            new(0x01, "Bluetooth Speaker", true),
            new(0x02, "Katsu's AirPods Pro", false),
        ];

        PairedDevice? selected = PairedDeviceSelector.Select(devices);

        Assert.NotNull(selected);
        Assert.Equal(0x02UL, selected.Address);
    }

    [Fact]
    public void Select_複数のAirPods候補では接続中を優先する()
    {
        PairedDevice[] devices =
        [
            new(0x01, "AirPods Pro", false),
            new(0x02, "AirPods Max", true),
        ];

        PairedDevice? selected = PairedDeviceSelector.Select(devices);

        Assert.NotNull(selected);
        Assert.Equal(0x02UL, selected.Address);
    }

    [Fact]
    public void Select_名前を変更していても接続中が1台なら選べる()
    {
        PairedDevice[] devices =
        [
            new(0x01, "My Earphones", true),
            new(0x02, "Bluetooth Speaker", false),
        ];

        PairedDevice? selected = PairedDeviceSelector.Select(devices);

        Assert.NotNull(selected);
        Assert.Equal(0x01UL, selected.Address);
    }

    [Fact]
    public void Select_候補が1台だけなら名前に依存しない()
    {
        PairedDevice[] devices = [new(0x01, "My Earphones", false)];

        PairedDevice? selected = PairedDeviceSelector.Select(devices);

        Assert.Same(devices[0], selected);
    }

    [Fact]
    public void Select_複数の不明な候補からは勝手に選ばない()
    {
        PairedDevice[] devices =
        [
            new(0x01, "Headphones A", false),
            new(0x02, "Headphones B", false),
        ];

        Assert.Null(PairedDeviceSelector.Select(devices));
    }

    [Fact]
    public void Select_Beatsも優先対象にする()
    {
        PairedDevice[] devices =
        [
            new(0x01, "Speaker", true),
            new(0x02, "Beats Fit Pro", false),
        ];

        PairedDevice? selected = PairedDeviceSelector.Select(devices);

        Assert.NotNull(selected);
        Assert.Equal(0x02UL, selected.Address);
    }
}
