using WinPods.Core.Models;
using WinPods.Core.Proximity;
using Xunit;

namespace WinPods.Core.Tests;

public class ProximityPairingParserTests
{
    /// <summary>
    /// docs/proximity-pairing.md のバイト配置に従ってメッセージを組み立てる。
    /// 末尾 16 バイトの暗号化ペイロードはデコード対象外なので固定値で埋める。
    /// </summary>
    private static byte[] BuildMessage(
        ushort modelId,
        byte status,
        byte podsBattery,
        byte caseByte,
        byte lidIndicator = 0x00,
        byte color = 0x00)
    {
        var data = new byte[ProximityPairingParser.ExpectedTotalLength];
        data[0] = ProximityPairingParser.MessageType;
        data[1] = ProximityPairingParser.MessageLength;
        data[2] = 0x01;
        data[3] = (byte)(modelId >> 8);
        data[4] = (byte)(modelId & 0xFF);
        data[5] = status;
        data[6] = podsBattery;
        data[7] = caseByte;
        data[8] = lidIndicator;
        data[9] = color;
        data[10] = 0x00;
        return data;
    }

    [Fact]
    public void TryParse_実際の形のバイト列をデコードできる()
    {
        // AirPods Pro / status bit5 = 0 (上位ニブルが左) / 左 80% 右 100% / ケース 40%
        byte[] data = Convert.FromHexString("0719010E20558A240200000102030405060708090A0B0C0D0E0F10");

        Assert.Equal(ProximityPairingParser.ExpectedTotalLength, data.Length);
        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal((ushort)0x0E20, message.RawModelId);
        Assert.Equal(AirPodsModel.AirPodsPro, message.Model);
        Assert.Equal(PodSide.Left, message.PrimaryPod);
        Assert.Equal(80, message.LeftBattery.Percent);
        Assert.Equal(100, message.RightBattery.Percent);
        Assert.Equal(40, message.CaseBattery.Percent);
        Assert.True(message.IsLeftCharging);
        Assert.False(message.IsRightCharging);
        Assert.False(message.IsCaseCharging);
    }

    [Fact]
    public void TryParse_status_bit5_が立っていると上位ニブルが右になる()
    {
        // 0x75 は bit5 が立っている → プライマリは右
        byte[] data = BuildMessage(0x0E20, status: 0x75, podsBattery: 0x8A, caseByte: 0x14);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal(PodSide.Right, message.PrimaryPod);
        Assert.Equal(100, message.LeftBattery.Percent);  // 下位ニブル 0xA
        Assert.Equal(80, message.RightBattery.Percent);  // 上位ニブル 0x8
        Assert.True(message.IsLeftCharging);             // bit0 = 下位ニブル側
        Assert.False(message.IsRightCharging);
    }

    [Fact]
    public void TryParse_status_bit5_が落ちていると上位ニブルが左になる()
    {
        // 0x55 は bit5 が落ちている → プライマリは左
        byte[] data = BuildMessage(0x0E20, status: 0x55, podsBattery: 0x8A, caseByte: 0x14);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal(PodSide.Left, message.PrimaryPod);
        Assert.Equal(80, message.LeftBattery.Percent);   // 上位ニブル 0x8
        Assert.Equal(100, message.RightBattery.Percent); // 下位ニブル 0xA
        Assert.False(message.IsLeftCharging);            // bit0 = 下位ニブル側 = 右
        Assert.True(message.IsRightCharging);
    }

    [Fact]
    public void TryParse_ケース充電中フラグを読める()
    {
        byte[] data = BuildMessage(0x0E20, status: 0x55, podsBattery: 0x55, caseByte: 0x45);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.True(message.IsCaseCharging);
        Assert.False(message.IsLeftCharging);
        Assert.False(message.IsRightCharging);
        Assert.Equal(50, message.CaseBattery.Percent);
    }

    [Fact]
    public void TryParse_ニブルが0xFのバッテリーは不明として扱う()
    {
        byte[] data = BuildMessage(0x0E20, status: 0x55, podsBattery: 0xF5, caseByte: 0x0F);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.False(message.LeftBattery.IsAvailable);
        Assert.Equal("--", message.LeftBattery.ToString());
        Assert.True(message.RightBattery.IsAvailable);
        Assert.Equal(50, message.RightBattery.Percent);
        Assert.False(message.CaseBattery.IsAvailable);
    }

    [Theory]
    [InlineData((ushort)0x0220, AirPodsModel.AirPods1)]
    [InlineData((ushort)0x0F20, AirPodsModel.AirPods2)]
    [InlineData((ushort)0x1320, AirPodsModel.AirPods3)]
    [InlineData((ushort)0x0E20, AirPodsModel.AirPodsPro)]
    [InlineData((ushort)0x1420, AirPodsModel.AirPodsPro2)]
    [InlineData((ushort)0x0A20, AirPodsModel.AirPodsMax)]
    [InlineData((ushort)0x0B20, AirPodsModel.PowerbeatsPro)]
    [InlineData((ushort)0xABCD, AirPodsModel.Unknown)]
    public void TryParse_機種を判別できる(ushort modelId, AirPodsModel expected)
    {
        byte[] data = BuildMessage(modelId, status: 0x55, podsBattery: 0x55, caseByte: 0x05);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal(expected, message.Model);
        Assert.Equal(modelId, message.RawModelId);
    }

    [Fact]
    public void TryParse_未知の機種でも生のIDは保持される()
    {
        byte[] data = BuildMessage(0xABCD, status: 0x55, podsBattery: 0x55, caseByte: 0x05);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal(AirPodsModel.Unknown, message.Model);
        Assert.Equal((ushort)0xABCD, message.RawModelId);
        Assert.StartsWith("0719", message.RawHex, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_短すぎるバイト列を拒否する()
    {
        byte[] data = [0x07, 0x19, 0x01, 0x0E, 0x20];

        Assert.False(ProximityPairingParser.TryParse(data, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void TryParse_メッセージ種別が異なるものを拒否する()
    {
        byte[] data = BuildMessage(0x0E20, status: 0x55, podsBattery: 0x55, caseByte: 0x05);
        data[0] = 0x10; // Nearby Info など別の Continuity メッセージ

        Assert.False(ProximityPairingParser.TryParse(data, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void TryParse_長さフィールドが異なるものを拒否する()
    {
        byte[] data = BuildMessage(0x0E20, status: 0x55, podsBattery: 0x55, caseByte: 0x05);
        data[1] = 0x0A;

        Assert.False(ProximityPairingParser.TryParse(data, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void TryParse_lid関連の生値を保持する()
    {
        byte[] data = BuildMessage(
            0x0E20, status: 0x55, podsBattery: 0x55, caseByte: 0x05, lidIndicator: 0x3B, color: 0x02);

        Assert.True(ProximityPairingParser.TryParse(data, out var message));

        Assert.Equal(0x3B, message.RawLidIndicator);
        Assert.Equal(3, message.LidOpenCounter);   // 下位 3bit
        Assert.False(message.IsLidOpenHint);       // bit3 が立っている
        Assert.Equal(0x02, message.RawColor);
        Assert.Equal(0x55, message.RawStatus);
    }
}
