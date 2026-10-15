using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PausedReloadLifecycleTests {
    [Fact]
    public void ReloadEndsAnExistingPauseAfterReplacingTheRoom() {
        // Wiring guard only; native menu, audio and event behavior require live QA.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Module/akron-module-actions.cs"));
        string action = source.Split("private static void ReloadRoom(")[1]
            .Split("private static void OpenDebugMap(")[0];
        Assert.Contains("bool wasPaused = level.Paused;", action);
        Assert.Contains("if (wasPaused && Engine.Scene == level && level.Paused)", action);
        Assert.True(action.IndexOf("level.Reload();", StringComparison.Ordinal) <
                    action.IndexOf("level.Unpause();", StringComparison.Ordinal));
        Assert.True(action.IndexOf("level.Unpause();", StringComparison.Ordinal) <
                    action.IndexOf("NotifyLevelUnpausedAfterReload(level);", StringComparison.Ordinal));
        Assert.Contains("Engine.Scene != level", action);
    }

    [Fact]
    public void CompiledReloadUsesNativeUnpauseAndGlobalNotification() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var root = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var methods = root.Methods.Concat(root.NestedTypes.SelectMany(type => type.Methods));
        var closure = methods.Single(method => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference call && call.DeclaringType.FullName == "Celeste.Level" && call.Name == "Reload") &&
            method.Name.Contains("ReloadRoom", StringComparison.Ordinal));
        var calls = closure.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Contains(calls, call => call.DeclaringType.FullName == "Celeste.Level" && call.Name == "Unpause");
        Assert.Contains(calls, call => call.DeclaringType.FullName == typeof(AkronModule).FullName && call.Name == "NotifyLevelUnpausedAfterReload");
    }
    [Fact]
    public void ReloadNotificationInvokesCurrentGlobalSubscribersOnce() {
        int first = 0;
        int second = 0;
        global::Celeste.Mod.Everest.Events.Level.UnpauseHandler one = level => first++;
        global::Celeste.Mod.Everest.Events.Level.UnpauseHandler two = level => second++;
        global::Celeste.Mod.Everest.Events.Level.OnUnpause += one;
        try {
            AkronModule.NotifyLevelUnpausedAfterReload(null);
            global::Celeste.Mod.Everest.Events.Level.OnUnpause += two;
            try {
                AkronModule.NotifyLevelUnpausedAfterReload(null);
                Assert.Equal(2, first);
                Assert.Equal(1, second);
            } finally {
                global::Celeste.Mod.Everest.Events.Level.OnUnpause -= two;
            }
        } finally {
            global::Celeste.Mod.Everest.Events.Level.OnUnpause -= one;
        }
    }

    [Fact]
    public void ReloadNotificationPreservesNativeSubscriberExceptionSemantics() {
        var expected = new InvalidOperationException("test subscriber");
        global::Celeste.Mod.Everest.Events.Level.UnpauseHandler handler = level => throw expected;
        global::Celeste.Mod.Everest.Events.Level.OnUnpause += handler;
        try {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                AkronModule.NotifyLevelUnpausedAfterReload(null)));
        } finally {
            global::Celeste.Mod.Everest.Events.Level.OnUnpause -= handler;
        }
    }

}
