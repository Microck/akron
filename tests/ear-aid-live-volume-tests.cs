using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class EarAidLiveVolumeTests {
    [Theory]
    [InlineData(true, true, 0f, .5f, true)]
    [InlineData(true, true, .5f, 0f, true)]
    [InlineData(true, true, .5f, 2f, true)]
    [InlineData(true, true, .5f, .5f, false)]
    [InlineData(true, true, 2f, 2f, false)]
    [InlineData(true, false, .5f, 1f, true)]
    [InlineData(true, false, 1f, 1f, true)]
    [InlineData(false, true, 1f, .5f, true)]
    [InlineData(false, true, 1f, 1f, true)]
    [InlineData(false, false, .5f, 1f, false)]
    [InlineData(false, false, 1f, 1f, false)]
    public void ReconciliationWritesForToggleOrEnabledNumericChangeOnly(bool wasApplied, bool apply, float previous, float target, bool expected) {
        Assert.Equal(expected, AkronEarAid.NeedsVolumeUpdate(wasApplied, apply, previous, target));
    }

    [Fact]
    public void EveryIntegerPercentEditUpdatesThenStabilizes() {
        float previous = 1f;
        for (int percent = 0; percent <= 200; percent++) {
            float target = percent / 100f;
            Assert.Equal(previous != target, AkronEarAid.NeedsVolumeUpdate(true, true, previous, target));
            Assert.False(AkronEarAid.NeedsVolumeUpdate(true, true, target, target));
            Assert.True(AkronEarAid.NeedsVolumeUpdate(true, false, target, 1f));
            previous = target;
        }
    }
}
