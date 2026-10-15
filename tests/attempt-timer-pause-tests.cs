using System;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class AttemptTimerPauseTests {
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    public void NativeSuspensionIncludesPauseBoundaryAndFreeze(bool paused, bool wasPaused, bool frozen, bool expected) {
        Assert.Equal(expected, AkronPracticeStats.IsNativeAttemptSuspended(paused, wasPaused, frozen));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SuspendedUpdatesDoNotConsumeAttemptOrChangeFiredState(bool before, bool after) {
        var session = new AkronModuleSession { AttemptElapsedSeconds = 7, AutoKillTimerFired = true,
            DeathsSinceLevelLoad = 4, RoomStatAliveStartedAt = 33 };
        for (int i = 0; i < 7200; i++)
            AkronPracticeStats.AdvanceAttemptTimer(session, 1f / 60f, before, after);
        Assert.Equal(7, session.AttemptElapsedSeconds);
        Assert.True(session.AutoKillTimerFired);
        Assert.Equal(4, session.DeathsSinceLevelLoad);
        Assert.Equal(33, session.RoomStatAliveStartedAt);
    }

    [Fact]
    public void ResumeCountsOnlyRemainingActiveDelta() {
        var session = new AkronModuleSession { AttemptElapsedSeconds = 10 };
        for (int i = 0; i < 1000; i++) AkronPracticeStats.AdvanceAttemptTimer(session, .25f, true, true);
        AkronPracticeStats.AdvanceAttemptTimer(session, .25f, true, false);
        Assert.Equal(10, session.AttemptElapsedSeconds);
        for (int i = 0; i < 40; i++) AkronPracticeStats.AdvanceAttemptTimer(session, .25f, false, false);
        Assert.Equal(20, session.AttemptElapsedSeconds);
        Assert.False(session.AutoKillTimerFired);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidOrNonpositiveDeltaDoesNotCorruptClock(float delta) {
        var session = new AkronModuleSession { AttemptElapsedSeconds = 5 };
        AkronPracticeStats.AdvanceAttemptTimer(session, delta, false, false);
        Assert.Equal(5, session.AttemptElapsedSeconds);
    }

    [Fact]
    public void MissingSessionIsSafe() => AkronPracticeStats.AdvanceAttemptTimer(null!, .1f, false, false);

    [Fact]
    public void ProductionRoutingGuardsBothTimerAndAreaBeforePolicyUseAndSamplesNativeUpdate() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        string[] Calls(MethodDefinition method) => method.Body.Instructions
            .Where(i => i.Operand is MethodReference).Select(i => ((MethodReference)i.Operand).Name).ToArray();
        var kill = Calls(module.Methods.Single(m => m.Name == "ApplyAutoKill"));
        int guard = Array.IndexOf(kill, "IsNativeAttemptSuspended");
        Assert.True(guard >= 0 && guard < Array.IndexOf(kill, "CanUse"));
        Assert.True(guard < Array.IndexOf(kill, "TryUse"));
        Assert.True(guard < Array.IndexOf(kill, "TryGetPlayerAutoKillArea"));
        Assert.True(guard < Array.IndexOf(kill, "TriggerAutoKillDeath"));
        var update = Calls(module.Methods.Single(m => m.Name == "LevelOnUpdate"));
        int sample = Array.IndexOf(update, "IsNativeAttemptSuspended");
        Assert.True(sample >= 0 && sample < Array.IndexOf(update, "Run"));
        Assert.True(sample < Array.IndexOf(update, "OnLevelUpdate"));
        var stats = assembly.MainModule.GetType(typeof(AkronPracticeStats).FullName);
        var after = Calls(stats.Methods.Single(m => m.Name == "OnLevelUpdate" && m.Parameters.Count == 2));
        Assert.Contains("UpdateRoomStatFreeze", after);
        Assert.Contains("IsNativeAttemptSuspended", after);
        Assert.Contains("AdvanceAttemptTimer", after);
        Assert.Contains("FinalizeRoom", after);
    }
}
