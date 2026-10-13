using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class ConfirmedStateLoadTests
{
    private static Level MakeLevel(Session session)
    {
        Level level = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
        level.Session = session;
        return level;
    }

    private static Session MakeSession() =>
        (Session)RuntimeHelpers.GetUninitializedObject(typeof(Session));

    [Fact]
    public void SameLevelAndSessionCanRunTheDeferredRestore()
    {
        Session session = MakeSession();
        Level level = MakeLevel(session);
        Assert.True(AkronModule.IsConfirmedStateLoadContextCurrent(level, session, level));
    }

    [Fact]
    public void DifferentSceneCannotRunTheDeferredRestore()
    {
        Session session = MakeSession();
        Level level = MakeLevel(session);
        Assert.False(AkronModule.IsConfirmedStateLoadContextCurrent(level, session, MakeLevel(session)));
    }

    [Fact]
    public void ReplacedSessionCannotRunTheDeferredRestore()
    {
        Session requested = MakeSession();
        Level level = MakeLevel(MakeSession());
        Assert.False(AkronModule.IsConfirmedStateLoadContextCurrent(level, requested, level));
    }

    [Fact]
    public void MissingLevelOrSessionCannotRunTheDeferredRestore()
    {
        Level level = MakeLevel(null!);
        Assert.False(AkronModule.IsConfirmedStateLoadContextCurrent(null!, MakeSession(), level));
        Assert.False(AkronModule.IsConfirmedStateLoadContextCurrent(level, null!, level));
    }

    [Fact]
    public void EndedSceneCannotRunTheDeferredRestore()
    {
        Session session = MakeSession();
        Level level = MakeLevel(session);
        Assert.False(AkronModule.IsConfirmedStateLoadContextCurrent(level, session, null!));
    }

    [Fact]
    public void StableQueueWaitsForUpdateBoundaryAndRandomScopeThenExecutesOnce()
    {
        FieldInfo queueField = typeof(AkronModule).GetField("afterEngineUpdateActions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo drain = typeof(AkronModule).GetMethod("RunAfterEngineUpdateActions", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo randomStackField = typeof(Calc).GetField("randomStack", BindingFlags.Static | BindingFlags.NonPublic)!;
        Queue<Action> queue = (Queue<Action>)queueField.GetValue(null)!;
        Action[] previousActions = queue.ToArray();
        object? previousStack = randomStackField.GetValue(null);
        Stack<Random> activeScope = new();
        activeScope.Push(new Random(42));
        try
        {
            queue.Clear();
            randomStackField.SetValue(null, activeScope);
            int calls = 0;
            AkronModule.ScheduleAfterStableEngineUpdate(() => calls++);
            Assert.Equal(0, calls);
            Assert.Single(queue);

            drain.Invoke(null, null);
            Assert.Equal(0, calls);
            Assert.Single(queue);

            activeScope.Clear();
            drain.Invoke(null, null);
            Assert.Equal(1, calls);
            Assert.Empty(queue);
            drain.Invoke(null, null);
            Assert.Equal(1, calls);
        }
        finally
        {
            queue.Clear();
            foreach (Action action in previousActions)
                queue.Enqueue(action);
            randomStackField.SetValue(null, previousStack);
        }
    }

    [Fact]
    public void ConfirmedPathQueuesRestoreAndCapturesSelectedSlot()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-actions.cs");
        string source = File.ReadAllText(path);
        int start = source.IndexOf("private static void LoadState(Level level", StringComparison.Ordinal);
        int end = source.IndexOf("internal static bool IsConfirmedStateLoadContextCurrent", start, StringComparison.Ordinal);
        string body = source.Substring(start, end - start);
        Assert.Contains("int slot = Settings.ActiveSavestateSlot;", body);
        Assert.Contains("if (confirmed)", body);
        Assert.Contains("ScheduleAfterStableEngineUpdate(() =>", body);
        Assert.Contains("IsConfirmedStateLoadContextCurrent(level, requestedSession, Engine.Scene)", body);
        Assert.DoesNotContain("AkronSaveLoadService.Load(", body);
    }
}
