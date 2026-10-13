using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class RestoredSaveSessionTests
{
    private static T Empty<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public void WarmRestoreRebindsToLiveSessionWithoutChangingSaveStatisticsOrLegacySession()
    {
        SaveData save = Empty<SaveData>();
        Session stale = Empty<Session>();
        Session live = Empty<Session>();
        save.CurrentSession_Safe = stale;
        save.CurrentSession = stale;
        save.TotalDeaths = 7;
        save.Time = 900;
        save.LastArea_Safe = new AreaKey { ID = 17, Mode = AreaMode.BSide };
        stale.Time = 345;
        stale.Deaths = 2;
        live.Time = 1234;
        live.Deaths = 4;

        AkronSaveLoadService.RelinkRestoredSaveSession(save, live, false);

        Assert.Same(live, save.CurrentSession_Safe);
        Assert.Same(stale, save.CurrentSession);
        Assert.Equal(7, save.TotalDeaths);
        Assert.Equal(900, save.Time);
        Assert.Equal(17, save.LastArea_Safe.ID);
        Assert.Equal(AreaMode.BSide, save.LastArea_Safe.Mode);
        Assert.Equal(345, stale.Time);
        Assert.Equal(2, stale.Deaths);
        Assert.Equal(1234, live.Time);
        Assert.Equal(4, live.Deaths);
    }

    [Fact]
    public void LaterLiveSessionChangesAreVisibleThroughTheSavedReference()
    {
        SaveData save = Empty<SaveData>();
        Session live = Empty<Session>();
        AkronSaveLoadService.RelinkRestoredSaveSession(save, live, false);
        live.Time = 5000;
        live.Deaths++;
        Assert.Equal(5000, save.CurrentSession_Safe.Time);
        Assert.Equal(1, save.CurrentSession_Safe.Deaths);
    }

    [Fact]
    public void RepeatedWarmLoadsFollowTheNewestLiveSession()
    {
        SaveData save = Empty<SaveData>();
        Session first = Empty<Session>();
        Session second = Empty<Session>();
        AkronSaveLoadService.RelinkRestoredSaveSession(save, first, false);
        AkronSaveLoadService.RelinkRestoredSaveSession(save, second, false);
        Assert.Same(second, save.CurrentSession_Safe);
        AkronSaveLoadService.RelinkRestoredSaveSession(save, second, false);
        Assert.Same(second, save.CurrentSession_Safe);
    }

    [Fact]
    public void FullRollbackPreservesItsCapturedReferenceRatherThanNormalizingIt()
    {
        SaveData save = Empty<SaveData>();
        Session captured = Empty<Session>();
        save.CurrentSession_Safe = captured;
        AkronSaveLoadService.RelinkRestoredSaveSession(save, Empty<Session>(), true);
        Assert.Same(captured, save.CurrentSession_Safe);
    }

    [Fact]
    public void MissingSaveDataIsAcceptedWithoutThrowing()
    {
        AkronSaveLoadService.RelinkRestoredSaveSession(null!, Empty<Session>(), false);
    }

    [Fact]
    public void MissingLiveSessionDoesNotClearTheExistingReference()
    {
        SaveData save = Empty<SaveData>();
        Session existing = Empty<Session>();
        save.CurrentSession_Safe = existing;
        AkronSaveLoadService.RelinkRestoredSaveSession(save, null!, false);
        Assert.Same(existing, save.CurrentSession_Safe);
    }

    [Fact]
    public void WarmRestoreInitializesAnAbsentSavedSessionReference()
    {
        SaveData save = Empty<SaveData>();
        Session live = Empty<Session>();
        AkronSaveLoadService.RelinkRestoredSaveSession(save, live, false);
        Assert.Same(live, save.CurrentSession_Safe);
    }

    [Fact]
    public void ClonedWarmSessionCanBeReboundWithoutMutatingTheCapturedGraph()
    {
        AkronDeepClone.Initialize();
        AkronDeepClone.ClearSharedState();
        try
        {
            Session initial = Empty<Session>();
            initial.Time = 100;
            initial.Deaths = 3;
            SaveData retained = Empty<SaveData>();
            retained.CurrentSession_Safe = initial;
            SessionGraph captured = (SessionGraph)AkronDeepClone.Clone(new SessionGraph(initial, retained));
            Assert.Same(captured.Session, captured.Save.CurrentSession_Safe);
            Assert.NotSame(initial, captured.Session);

            AkronDeepClone.ClearSharedState();
            Session restored = (Session)AkronDeepClone.Clone(captured.Session);
            Assert.NotSame(captured.Session, restored);
            Assert.Same(initial, retained.CurrentSession_Safe);
            AkronSaveLoadService.RelinkRestoredSaveSession(retained, restored, false);
            restored.Time = 200;
            restored.Deaths++;

            Assert.Same(restored, retained.CurrentSession_Safe);
            Assert.Equal(200, retained.CurrentSession_Safe.Time);
            Assert.Equal(4, retained.CurrentSession_Safe.Deaths);
            Assert.Same(captured.Session, captured.Save.CurrentSession_Safe);
            Assert.Equal(100, captured.Session.Time);
            Assert.Equal(3, captured.Session.Deaths);
            Assert.Equal(100, initial.Time);
            Assert.Equal(3, initial.Deaths);
        }
        finally
        {
            AkronDeepClone.Reset();
        }
    }

    [Fact]
    public void FullGraphCloneRetainsItsSessionAliasAcrossRollbackGuard()
    {
        AkronDeepClone.Initialize();
        AkronDeepClone.ClearSharedState();
        try
        {
            Session session = Empty<Session>();
            SaveData save = Empty<SaveData>();
            save.CurrentSession_Safe = session;
            SessionGraph restored = (SessionGraph)AkronDeepClone.Clone(new SessionGraph(session, save));
            AkronSaveLoadService.RelinkRestoredSaveSession(restored.Save, restored.Session, true);
            Assert.Same(restored.Session, restored.Save.CurrentSession_Safe);
            Assert.NotSame(session, restored.Session);
            Assert.NotSame(save, restored.Save);
            Assert.Same(session, save.CurrentSession_Safe);
        }
        finally
        {
            AkronDeepClone.Reset();
        }
    }

    [Fact]
    public void ProductionRestoreRelinksFinalLiveStateAfterGlobalReplacementAndStatistics()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRoot(), "Source", "SaveLoad", "AkronSaveLoad.cs"));
        int start = source.IndexOf("private static bool RestoreNativeSlot(", StringComparison.Ordinal);
        int end = source.IndexOf("private static bool MatchesCurrentNativeSession(", start, StringComparison.Ordinal);
        string restore = source.Substring(start, end - start);
        int replacement = restore.IndexOf("SaveData.Instance = savedSaveData;", StringComparison.Ordinal);
        int counters = restore.IndexOf("level.Session.DeathsInCurrentLevel = Math.Max", StringComparison.Ordinal);
        int rebind = restore.IndexOf("RelinkRestoredSaveSession(SaveData.Instance, level.Session, restoreGlobalSaveData);", StringComparison.Ordinal);
        Assert.True(replacement >= 0 && counters > replacement && rebind > counters);
        Assert.DoesNotContain(".StartSession(", restore);
        string helper = File.ReadAllText(Path.Combine(
            FindRoot(), "Source", "SaveLoad", "akron-restored-save-session.cs"));
        Assert.DoesNotContain(".StartSession(", helper);
        Assert.DoesNotContain("saveData.CurrentSession =", helper);
    }

    private sealed class SessionGraph
    {
        public Session Session;
        public SaveData Save;

        public SessionGraph(Session session, SaveData save)
        {
            Session = session;
            Save = save;
        }
    }

    private static string FindRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
}
