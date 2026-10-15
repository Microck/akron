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
                    action.IndexOf("LevelOnUnpause(level);", StringComparison.Ordinal));
        Assert.Contains("Engine.Scene != level", action);
    }

    [Fact]
    public void CompiledReloadUsesNativeUnpauseAndAkronAccounting() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var root = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var methods = root.Methods.Concat(root.NestedTypes.SelectMany(type => type.Methods));
        var closure = methods.Single(method => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference call && call.DeclaringType.FullName == "Celeste.Level" && call.Name == "Reload") &&
            method.Name.Contains("ReloadRoom", StringComparison.Ordinal));
        var calls = closure.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Contains(calls, call => call.DeclaringType.FullName == "Celeste.Level" && call.Name == "Unpause");
        Assert.Contains(calls, call => call.DeclaringType.FullName == typeof(AkronModule).FullName && call.Name == "LevelOnUnpause");
    }
}
