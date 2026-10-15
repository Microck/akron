using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DashRetentionTests {
    [Theory]
    [InlineData(0, 0, 1, 0, 1)]
    [InlineData(1, 1, 1, 0, 2)]
    [InlineData(2, 2, 0, 0, 2)]
    [InlineData(0, 0, 9, 7, 2)]
    [InlineData(2, 2, 7, 7, 2)]
    [InlineData(2, 2, 7, 9, 2)]
    [InlineData(0, 1, 1, 0, 1)]
    [InlineData(3, 5, 9, 7, 5)]
    [InlineData(int.MaxValue, int.MaxValue, 1, 0, int.MaxValue)]
    public void RollbackAccountingIsCumulativeAndIdempotent(int before, int current, int nativeBefore, int nativeAfter, int expected) {
        Assert.Equal(expected, AkronPracticeCounters.RetainReloadedDashes(before, current, nativeBefore, nativeAfter));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 2, 9)]
    [InlineData(1, int.MaxValue, int.MaxValue)]
    [InlineData(-1, 0, 0)]
    public void DisplayCombinesNativeAndRetainedWithoutOverflow(int native, int retained, int expected) {
        Assert.Equal(expected, AkronPracticeCounters.GetDashCount(native, retained));
    }

    [Fact]
    public void ReloadHookIsRegisteredAndRemovedAndDoesNotWriteNativeStatistics() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        foreach (string accessor in new[] { "add_Reload", "remove_Reload" }) {
            Assert.Contains(module.Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Instruction>()),
                i => i.Operand is MethodReference m && m.Name == accessor && m.DeclaringType.FullName == "On.Celeste.Level");
        }
        var counters = assembly.MainModule.GetType(typeof(AkronPracticeCounters).FullName);
        foreach (var method in counters.Methods.Where(m => m.HasBody)) {
            Assert.DoesNotContain(method.Body.Instructions, i => i.OpCode.Code == Code.Stfld &&
                i.Operand is FieldReference f && (f.DeclaringType.FullName == "Celeste.Session" || f.DeclaringType.FullName == "Celeste.SaveData"));
        }
        var added = module.Methods.Single(m => m.Name == "PlayerOnAdded");
        var calls = added.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToList();
        Assert.True(calls.FindIndex(m => m.DeclaringType.Name == nameof(AkronPracticeCounters) && m.Name == "OnRespawn") <
                    calls.FindIndex(m => m.DeclaringType.Name == nameof(AkronAutosave) && m.Name == "NotifyRespawn"));
        var reload = counters.Methods.Single(m => m.Name == "OnLevelReload");
        Assert.Contains(reload.Body.ExceptionHandlers, h => h.HandlerType == ExceptionHandlerType.Finally);
        var respawn = counters.Methods.Single(m => m.Name == "OnRespawn");
        Assert.Contains(respawn.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "ApplyReloadRetention");
    }
}
