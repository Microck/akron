using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PauseTrackerIntervalTests {
    [Fact]
    public void DisabledCollectionDiscardsStartBeforeLaterUntrackedPause() {
        var session = new AkronModuleSession { PauseTrackerCurrentPauseStartedAt = 10, PauseTrackerPauseCount = 1, PauseTrackerPausedSeconds = 2 };
        AkronModule.FinishPauseTrackerInterval(session, 20, false);
        Assert.Equal(-1, session.PauseTrackerCurrentPauseStartedAt);
        Assert.Equal(2, session.PauseTrackerPausedSeconds);
        AkronModule.FinishPauseTrackerInterval(session, 100, true);
        Assert.Equal(2, session.PauseTrackerPausedSeconds);
        Assert.Equal(1, session.PauseTrackerPauseCount);
    }

    [Theory]
    [InlineData(true, 25f, 7f)]
    [InlineData(false, 25f, 2f)]
    [InlineData(true, 15f, 2f)]
    [InlineData(true, null, 2f)]
    public void SettlementPreservesCollectionPolicyAndClearsExactlyOnce(bool allowed, float? now, float expected) {
        var session = new AkronModuleSession { PauseTrackerCurrentPauseStartedAt = 20, PauseTrackerPausedSeconds = 2, PauseTrackerPauseCount = 3, PauseTrackerRapidPauseCount = 1, PauseTrackerLastPauseAt = 20 };
        AkronModule.FinishPauseTrackerInterval(session, now, allowed);
        Assert.Equal(expected, session.PauseTrackerPausedSeconds);
        Assert.Equal(-1, session.PauseTrackerCurrentPauseStartedAt);
        AkronModule.FinishPauseTrackerInterval(session, 1000, true);
        Assert.Equal(expected, session.PauseTrackerPausedSeconds);
        Assert.Equal(3, session.PauseTrackerPauseCount);
        Assert.Equal(1, session.PauseTrackerRapidPauseCount);
        Assert.Equal(20, session.PauseTrackerLastPauseAt);
    }

    [Fact]
    public void MissingSessionIsHarmless() => AkronModule.FinishPauseTrackerInterval(null, 10, true);

    [Fact]
    public void RealUnpauseAlwaysInvokesIntervalFinalization() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Runtime/akron-runtime-guards.cs"));
        string method = source.Split("private static void LevelOnUnpause(Level level)")[1].Split("internal static void FinishPauseTrackerInterval")[0];
        Assert.Contains("FinishPauseTrackerInterval(Session, level?.RawTimeActive,", method);
        Assert.Contains("level != null && Settings.PauseTracker && AkronPolicy.CanUse(AkronFeatureKind.PauseTracker).Allowed", method);
        Assert.DoesNotContain("PauseTrackerCurrentPauseStartedAt =", method);
    }
}
