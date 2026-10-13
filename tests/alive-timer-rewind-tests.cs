using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class AliveTimerRewindTests
{
    [Theory]
    [InlineData(0L, false, -1L)]
    [InlineData(0L, true, 0L)]
    [InlineData(20L, false, -1L)]
    [InlineData(20L, true, 20L)]
    public void RespawnRebasesFutureAliveClockAndItsFreezeAccounting(long now, bool frozen, long frozenStart)
    {
        AkronModuleSession session = new()
        {
            RoomStatAliveStartedAt = 100,
            RoomStatAliveFrozenStartedAt = 110,
            RoomStatAliveFrozenDuration = 12
        };

        Assert.True(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, now, frozen));

        Assert.Equal(now, session.RoomStatAliveStartedAt);
        Assert.Equal(frozenStart, session.RoomStatAliveFrozenStartedAt);
        Assert.Equal(0, session.RoomStatAliveFrozenDuration);
    }

    [Theory]
    [InlineData(100L)]
    [InlineData(130L)]
    public void EqualOrForwardClockPreservesExistingAliveState(long now)
    {
        AkronModuleSession session = new()
        {
            RoomStatAliveStartedAt = 100,
            RoomStatAliveFrozenStartedAt = 110,
            RoomStatAliveFrozenDuration = 12
        };

        Assert.False(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, now, false));

        Assert.Equal(100, session.RoomStatAliveStartedAt);
        Assert.Equal(110, session.RoomStatAliveFrozenStartedAt);
        Assert.Equal(12, session.RoomStatAliveFrozenDuration);
    }

    [Fact]
    public void RepeatedAdditionDoesNotRestartAnAlreadyRebasedTimer()
    {
        AkronModuleSession session = new() { RoomStatAliveStartedAt = 100 };
        Assert.True(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, 0, false));
        session.RoomStatAliveFrozenDuration = 7;

        Assert.False(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, 20, false));

        Assert.Equal(0, session.RoomStatAliveStartedAt);
        Assert.Equal(7, session.RoomStatAliveFrozenDuration);
    }

    [Fact]
    public void RebaseDoesNotResetRoomCountersOrAttemptHistory()
    {
        AkronModuleSession session = new()
        {
            RoomStatAliveStartedAt = 100,
            TrackedRoom = "room",
            RoomEnteredAt = 50,
            RoomStatFrozenStartedAt = 60,
            RoomStatFrozenDuration = 8,
            RoomStatStrawberries = 2,
            DeathsSinceLevelLoad = 3,
            DeathsSinceRoomTransition = 4,
            AttemptElapsedSeconds = 5,
            AttemptStatus = AkronStatus.Cheat,
            AttemptReason = "retained"
        };

        Assert.True(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, 0, false));

        Assert.Equal("room", session.TrackedRoom);
        Assert.Equal(50, session.RoomEnteredAt);
        Assert.Equal(60, session.RoomStatFrozenStartedAt);
        Assert.Equal(8, session.RoomStatFrozenDuration);
        Assert.Equal(2, session.RoomStatStrawberries);
        Assert.Equal(3, session.DeathsSinceLevelLoad);
        Assert.Equal(4, session.DeathsSinceRoomTransition);
        Assert.Equal(5, session.AttemptElapsedSeconds);
        Assert.Equal(AkronStatus.Cheat, session.AttemptStatus);
        Assert.Equal("retained", session.AttemptReason);
    }

    [Fact]
    public void CoherentRestoredAliveStateRetainsItsElapsedAndFrozenTime()
    {
        AkronModuleSession session = new()
        {
            RoomStatAliveStartedAt = 30,
            RoomStatAliveFrozenStartedAt = -1,
            RoomStatAliveFrozenDuration = 4
        };

        Assert.False(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, 50, true));

        Assert.Equal(30, session.RoomStatAliveStartedAt);
        Assert.Equal(-1, session.RoomStatAliveFrozenStartedAt);
        Assert.Equal(4, session.RoomStatAliveFrozenDuration);
    }

    [Fact]
    public void MissingSessionIsANoOp()
    {
        Assert.False(AkronPracticeStats.RebaseAliveTimerAfterRespawn(null!, 0, false));
        AkronPracticeStats.NotifyPlayerRespawn(null!);
    }

    [Fact]
    public void AddedCallbackSafelyHandlesMissingModule()
    {
        FieldInfo instance = typeof(AkronModule).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        Level level = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
        level.Session = (Session)RuntimeHelpers.GetUninitializedObject(typeof(Session));
        try
        {
            instance.SetValue(null, null);
            AkronPracticeStats.NotifyPlayerRespawn(level);
        }
        finally
        {
            instance.SetValue(null, previous);
        }
    }

    [Fact]
    public void RebasedActiveFreezeCountsOnlyTheNewEpochOnRelease()
    {
        AkronModuleSession session = new()
        {
            RoomStatAliveStartedAt = 100,
            RoomStatAliveFrozenStartedAt = 120,
            RoomStatAliveFrozenDuration = 70
        };
        Assert.True(AkronPracticeStats.RebaseAliveTimerAfterRespawn(session, 0, true));

        MethodInfo update = typeof(AkronPracticeStats).GetMethod("UpdateFreezeState", BindingFlags.Static | BindingFlags.NonPublic)!;
        (long startedAt, long duration) = ((long, long))update.Invoke(null,
            new object[] { 30L, false, session.RoomStatAliveFrozenStartedAt, session.RoomStatAliveFrozenDuration })!;
        Assert.Equal(-1, startedAt);
        Assert.Equal(30, duration);

        MethodInfo frozenDuration = typeof(AkronPracticeStats).GetMethod("GetFrozenDuration", BindingFlags.Static | BindingFlags.NonPublic)!;
        long excluded = (long)frozenDuration.Invoke(null, new object[] { 50L, startedAt, duration })!;
        Assert.Equal(20, 50 - session.RoomStatAliveStartedAt - excluded);
    }
}
