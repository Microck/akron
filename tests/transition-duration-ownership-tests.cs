using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[CollectionDefinition("Transition duration ownership", DisableParallelization = true)]
public sealed class TransitionDurationOwnershipCollection { }

[Collection("Transition duration ownership")]
public sealed class TransitionDurationOwnershipTests : IDisposable
{
    public TransitionDurationOwnershipTests() => AkronModule.RestoreTransitionSpeed();
    public void Dispose() => AkronModule.RestoreTransitionSpeed();
    private static Level LevelWith(float value)
    {
        var level = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
        level.NextTransitionDuration = value;
        return level;
    }

    [Fact]
    public void ReleaseRestoresBaselineAfterRepeatedOwnedWrites()
    {
        var level = LevelWith(0.65f);
        AkronModule.SetTransitionSpeedOverride(level, 1.3f);
        AkronModule.SetTransitionSpeedOverride(level, 0.325f);
        AkronModule.RestoreTransitionSpeed();
        Assert.Equal(0.65f, level.NextTransitionDuration);
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.65f)]
    [InlineData(1.300001f)]
    public void ReleasePreservesLaterExternalWrite(float external)
    {
        var level = LevelWith(0.65f);
        AkronModule.SetTransitionSpeedOverride(level, 1.3f);
        level.NextTransitionDuration = external;
        AkronModule.RestoreTransitionSpeed();
        Assert.Equal(external, level.NextTransitionDuration);
        level.NextTransitionDuration = 0.9f;
        AkronModule.RestoreTransitionSpeed();
        Assert.Equal(0.9f, level.NextTransitionDuration);
    }

    [Fact]
    public void ReapplyingAfterExternalWriteRetainsNewBaseline()
    {
        var level = LevelWith(0.65f);
        AkronModule.SetTransitionSpeedOverride(level, 1.3f);
        level.NextTransitionDuration = 0.05f;
        AkronModule.SetTransitionSpeedOverride(level, 1.3f);
        AkronModule.SetTransitionSpeedOverride(level, 0.325f);
        AkronModule.RestoreTransitionSpeed();
        Assert.Equal(0.05f, level.NextTransitionDuration);
    }

    [Fact]
    public void LevelReplacementReleasesOldOwnerWithoutOverwritingItsSuccessor()
    {
        var old = LevelWith(0.65f);
        var next = LevelWith(0.2f);
        AkronModule.SetTransitionSpeedOverride(old, 1.3f);
        old.NextTransitionDuration = 0.05f;
        AkronModule.SetTransitionSpeedOverride(next, 0.325f);
        Assert.Equal(0.05f, old.NextTransitionDuration);
        AkronModule.RestoreTransitionSpeed();
        Assert.Equal(0.2f, next.NextTransitionDuration);
    }
}
