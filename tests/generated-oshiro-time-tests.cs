using Xunit;
namespace Celeste.Mod.Akron.Tests;

public sealed class GeneratedOshiroTimeTests {
    [Theory]
    [InlineData(1f)]
    [InlineData(0.75f)]
    public void FinalRemovalRestoresPriorRateInsteadOfUnconditionalOne(float prior) {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, prior, .6f);
        lease.Observe(actor, scene, true, .6f, .5f);
        Assert.True(lease.Remove(actor, scene, scene, .5f, out float restored));
        Assert.Equal(prior, restored);
        Assert.False(lease.Remove(actor, scene, scene, restored, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedContributorsCannotRestoreEachOthersStaleSlowdown(bool removeFirstWriterFirst) {
        var lease = new AkronGeneratedTimeRateLease(); var a = new object(); var b = new object(); var scene = new object();
        lease.Observe(a, scene, true, 1f, .5f);
        lease.Observe(b, scene, true, .5f, .5f); // identical-value write still contributes
        Assert.False(lease.Remove(removeFirstWriterFirst ? a : b, scene, scene, .5f, out _));
        Assert.True(lease.Remove(removeFirstWriterFirst ? b : a, scene, scene, .5f, out float restored));
        Assert.Equal(1f, restored);
    }

    [Fact]
    public void NativeOshiroSameValueWriteSupersedesGeneratedOwnership() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.Observe(new object(), scene, false, .5f, .5f);
        Assert.False(lease.Remove(actor, scene, scene, .5f, out _));
    }

    [Fact]
    public void DistinctExternalWriteIsNeverOverwrittenOnRemoval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(actor, scene, scene, .7f, out _));
    }

    [Fact]
    public void NewGeneratedWriteAfterExternalChangeCapturesNewBaseline() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.Observe(actor, scene, true, .7f, .5f);
        Assert.True(lease.Remove(actor, scene, scene, .5f, out float restored));
        Assert.Equal(.7f, restored);
    }

    [Fact]
    public void SceneChangeCannotRestoreOldRate() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(actor, scene, new object(), .5f, out _));
    }

    [Fact]
    public void SceneEndClearsOnlyItsOwnInterval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.EndScene(new object());
        Assert.True(lease.Release(scene, .5f, out _));
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.EndScene(scene);
        Assert.False(lease.Release(scene, .5f, out _));
    }

    [Fact]
    public void UnrelatedRemovalDoesNotReleaseInterval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(new object(), scene, scene, .5f, out _));
        Assert.True(lease.Release(scene, .5f, out float restored));
        Assert.Equal(1f, restored);
        Assert.False(lease.Release(scene, .5f, out _));
    }
}
