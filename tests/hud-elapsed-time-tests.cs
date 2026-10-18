using System;
using System.Globalization;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class HudElapsedTimeTests {
    [Theory]
    [InlineData(0L, "00:00.000")]
    [InlineData(599999999L, "00:59.999")]
    [InlineData(600000000L, "01:00.000")]
    [InlineData(35999999999L, "59:59.999")]
    [InlineData(36000000000L, "60:00.000")]
    [InlineData(36000010000L, "60:00.001")]
    [InlineData(36623450000L, "61:02.345")]
    [InlineData(72000000000L, "120:00.000")]
    [InlineData(864000000000L, "1440:00.000")]
    [InlineData(long.MaxValue, "15372286728:05.477")]
    [InlineData(long.MinValue, "15372286728:05.477")]
    public void KeepsTotalElapsedMinutesAndMillisecondPrecision(long ticks, string expected) {
        Assert.Equal(expected, AkronHudRenderer.FormatHudTicks(ticks));
    }

    [Theory]
    [InlineData(-123456789L)]
    [InlineData(-35999999999L)]
    [InlineData(123456789L)]
    public void ExistingSubHourMagnitudeFormatIsUnchanged(long ticks) {
        Assert.Equal(TimeSpan.FromTicks(ticks).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture),
            AkronHudRenderer.FormatHudTicks(ticks));
    }
}
