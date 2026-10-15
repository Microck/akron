using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection("Native Everest lifecycle")]
public sealed class PausedReloadLifecycleTests {
    [Fact]
    public void ReloadEndsAnExistingPauseAfterReplacingTheRoom() {
        // Wiring guard only; native menu, audio and event behavior require live QA.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Module/akron-module-actions.cs"));
        string action = source.Split("private static void ReloadRoom(")[1]
            .Split("private static void OpenDebugMap(")[0];
        Assert.Contains("bool wasPaused = level.Paused;", action);
        Assert.Contains("level == null || level.Completed || Engine.Scene != level", action);
        Assert.Contains("Engine.Scene != level || level.Completed || !AkronPolicy.CanUse", action);
        Assert.Contains("level.Tracker.GetEntitiesCopy<Player>().ForEach(entity => entity.RemoveSelf());", action);
        Assert.True(action.IndexOf("GetEntitiesCopy<Player>()", StringComparison.Ordinal) <
                    action.IndexOf("level.Reload();", StringComparison.Ordinal));
        Assert.Contains("if (wasPaused && Engine.Scene == level && level.Paused)", action);
        Assert.True(action.IndexOf("level.Reload();", StringComparison.Ordinal) <
                    action.IndexOf("level.Unpause();", StringComparison.Ordinal));
        Assert.True(action.IndexOf("level.Unpause();", StringComparison.Ordinal) <
                    action.IndexOf("NotifyLevelUnpausedAfterReload(level);", StringComparison.Ordinal));
        Assert.True(action.IndexOf("level.Unpause();", StringComparison.Ordinal) <
                    action.IndexOf("level.EndPauseEffects();", StringComparison.Ordinal));
        Assert.True(action.IndexOf("level.EndPauseEffects();", StringComparison.Ordinal) <
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
        Assert.Contains(calls, call => call.DeclaringType.FullName == "Celeste.Level" && call.Name == "EndPauseEffects");
        Assert.Contains(calls, call => call.DeclaringType.FullName == typeof(AkronModule).FullName && call.Name == "NotifyLevelUnpausedAfterReload");
    }
    [NativeEverestFact]
    [Trait("Category", "NativeEverest")]
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

    [NativeEverestFact]
    [Trait("Category", "NativeEverest")]
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

    [Fact]
    public void ProductionWiringBindsEverestAndDirectlyInvokesTheDispatcher() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var root = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var initializer = root.Methods.Single(method => method.Name == ".cctor");
        Assert.Contains(initializer.Body.Instructions, instruction =>
            instruction.Operand is TypeReference type && type.FullName == "Celeste.Mod.Everest/Events/Level");
        Assert.Contains(initializer.Body.Instructions, instruction =>
            instruction.Operand is MethodReference method && method.Name == "ResolveEverestLevelUnpause");
        var notify = root.Methods.Single(method => method.Name == "NotifyLevelUnpausedAfterReload");
        Assert.Empty(notify.Body.ExceptionHandlers);
        Assert.Contains(notify.Body.Instructions, instruction =>
            instruction.Operand is MethodReference method && method.Name == "Invoke" &&
            method.DeclaringType.FullName.StartsWith("System.Action`1", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvedDispatcherIncludesLateSubscribersAndInvokesEachOnce() {
        var dispatch = AkronModule.ResolveEverestLevelUnpause(typeof(EventFixture));
        Assert.NotNull(dispatch);
        int first = 0, second = 0;
        Action<Level> one = _ => first++;
        Action<Level> two = _ => second++;
        EventFixture.OnUnpause += one;
        try {
            dispatch(null!);
            EventFixture.OnUnpause += two;
            dispatch(null!);
            Assert.Equal(2, first);
            Assert.Equal(1, second);
        } finally {
            EventFixture.OnUnpause -= one;
            EventFixture.OnUnpause -= two;
        }
    }

    [Fact]
    public void ResolvedDispatcherPreservesSubscriberExceptionIdentity() {
        var dispatch = AkronModule.ResolveEverestLevelUnpause(typeof(EventFixture));
        Assert.NotNull(dispatch);
        var expected = new InvalidOperationException("fixture subscriber");
        Action<Level> handler = _ => throw expected;
        EventFixture.OnUnpause += handler;
        try {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => dispatch(null!)));
        } finally {
            EventFixture.OnUnpause -= handler;
        }
    }

    [Theory]
    [InlineData(typeof(MissingFixture))]
    [InlineData(typeof(WrongReturnFixture))]
    [InlineData(typeof(WrongParameterFixture))]
    [InlineData(typeof(InstanceFixture))]
    public void IncompatibleDispatcherIsNotBound(Type dispatcherType) {
        Assert.Null(AkronModule.ResolveEverestLevelUnpause(dispatcherType));
    }

    private static class EventFixture {
        internal static event Action<Level>? OnUnpause;
        private static void Unpause(Level level) => OnUnpause?.Invoke(level);
    }
    private sealed class MissingFixture { }
    private sealed class WrongReturnFixture { private static int Unpause(Level level) => 0; }
    private sealed class WrongParameterFixture { private static void Unpause(string level) { } }
    private sealed class InstanceFixture { private void Unpause(Level level) { } }

}


// CI uses reference-only game assemblies whose event bodies are stripped. These
// integration tests must be explicitly enabled with an executable native runtime.
public sealed class NativeEverestFactAttribute : FactAttribute {
    public NativeEverestFactAttribute() {
        if (Environment.GetEnvironmentVariable("AKRON_NATIVE_EVEREST_TESTS") != "1")
            Skip = "Requires native Everest bodies; set AKRON_NATIVE_EVEREST_TESTS=1 with native references.";
    }
}

[CollectionDefinition("Native Everest lifecycle", DisableParallelization = true)]
public sealed class NativeEverestLifecycleCollection { }
