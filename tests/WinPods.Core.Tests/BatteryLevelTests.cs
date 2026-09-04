using WinPods.Core.Models;
using Xunit;

namespace WinPods.Core.Tests;

public class BatteryLevelTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(5, 50)]
    [InlineData(10, 100)]
    public void FromNibble_0から10は10パーセント刻みになる(int nibble, int expectedPercent)
    {
        var level = BatteryLevel.FromNibble(nibble);

        Assert.True(level.IsAvailable);
        Assert.Equal(expectedPercent, level.Percent);
        Assert.Equal($"{expectedPercent}%", level.ToString());
    }

    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(-1)]
    public void FromNibble_範囲外は不明として扱う(int nibble)
    {
        var level = BatteryLevel.FromNibble(nibble);

        Assert.False(level.IsAvailable);
        Assert.Equal(0, level.Percent);
        Assert.Equal("--", level.ToString());
    }

    [Fact]
    public void Unknown_は既定値と等しい()
    {
        Assert.Equal(default, BatteryLevel.Unknown);
        Assert.Equal(BatteryLevel.Unknown, BatteryLevel.FromNibble(15));
    }
}
