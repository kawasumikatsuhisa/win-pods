using WinPods.Core.Models;
using WinPods.Core.Proximity;
using Xunit;

namespace WinPods.Core.Tests;

public class LidStateTrackerTests
{
    private const ulong DeviceAddress = 0xAABBCCDDEEFF;

    private static ProximityPairingMessage MessageWithLid(byte lidIndicator) =>
        new()
        {
            RawModelId = 0x0E20,
            Model = AirPodsModel.AirPodsPro,
            LeftBattery = BatteryLevel.FromNibble(8),
            RightBattery = BatteryLevel.FromNibble(8),
            CaseBattery = BatteryLevel.FromNibble(5),
            IsLeftCharging = false,
            IsRightCharging = false,
            IsCaseCharging = false,
            PrimaryPod = PodSide.Left,
            RawStatus = 0x55,
            RawLidIndicator = lidIndicator,
            RawColor = 0x00,
            RawHex = string.Empty,
        };

    [Fact]
    public void Update_初回はベースラインを取るだけでイベントを出さない()
    {
        var tracker = new LidStateTracker();

        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x01)));
    }

    [Fact]
    public void Update_同じ値が続いてもイベントを出さない()
    {
        var tracker = new LidStateTracker();
        tracker.Update(DeviceAddress, MessageWithLid(0x01));

        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x01)));
        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x01)));
    }

    [Fact]
    public void Update_カウンタが進んだら蓋が開いたとみなす()
    {
        var tracker = new LidStateTracker();
        tracker.Update(DeviceAddress, MessageWithLid(0x01));

        Assert.Equal(LidEvent.Opened, tracker.Update(DeviceAddress, MessageWithLid(0x02)));
        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x02)));
        Assert.Equal(LidEvent.Opened, tracker.Update(DeviceAddress, MessageWithLid(0x03)));
    }

    [Fact]
    public void Update_カウンタが一周して戻っても開いたとみなす()
    {
        var tracker = new LidStateTracker();
        tracker.Update(DeviceAddress, MessageWithLid(0x07));

        Assert.Equal(LidEvent.Opened, tracker.Update(DeviceAddress, MessageWithLid(0x00)));
    }

    [Fact]
    public void Update_カウンタ据え置きでbit3だけ変わったら開閉として扱う()
    {
        var tracker = new LidStateTracker();
        tracker.Update(DeviceAddress, MessageWithLid(0x03)); // bit3 = 0 → 開

        Assert.Equal(LidEvent.Closed, tracker.Update(DeviceAddress, MessageWithLid(0x0B)));
        Assert.Equal(LidEvent.Opened, tracker.Update(DeviceAddress, MessageWithLid(0x03)));
    }

    [Fact]
    public void Update_デバイスごとに独立して追跡する()
    {
        var tracker = new LidStateTracker();
        const ulong other = 0x112233445566;

        tracker.Update(DeviceAddress, MessageWithLid(0x01));

        // 別デバイスの初回観測はベースライン扱い
        Assert.Equal(LidEvent.None, tracker.Update(other, MessageWithLid(0x05)));
        Assert.Equal(LidEvent.Opened, tracker.Update(other, MessageWithLid(0x06)));
        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x01)));
    }

    [Fact]
    public void Forget_記録を破棄すると次回はベースラインからやり直す()
    {
        var tracker = new LidStateTracker();
        tracker.Update(DeviceAddress, MessageWithLid(0x01));
        tracker.Forget(DeviceAddress);

        Assert.Equal(LidEvent.None, tracker.Update(DeviceAddress, MessageWithLid(0x02)));
    }
}
