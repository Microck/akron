using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayCloseShortCircuitTests {
    [Fact]
    public void RecoversOnlyOwnedUnpolledFreshInputInSameActiveScene() {
        Assert.True(AkronModule.ShouldRecoverOverlayClose(true, true, true, true, true, 10, 11, 3, 3));
        Assert.True(AkronModule.ShouldRecoverOverlayClose(true, true, true, true, true, ulong.MaxValue, 0, 3, 3));
        for (int missing = 0; missing < 5; missing++) {
            bool[] flags = Enumerable.Repeat(true, 5).ToArray();
            flags[missing] = false;
            Assert.False(AkronModule.ShouldRecoverOverlayClose(flags[0], flags[1], flags[2], flags[3], flags[4], 10, 11, 3, 3));
        }
        Assert.False(AkronModule.ShouldRecoverOverlayClose(true, true, true, true, true, 10, 10, 3, 3));
        Assert.False(AkronModule.ShouldRecoverOverlayClose(true, true, true, true, true, 10, 11, 3, 4));
    }

    [Fact]
    public void RecoveryIncludesExistingTransientOwnershipBeforeAndAfterUpdate() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition engine = module.GetType(typeof(AkronModule).FullName).Methods.Single(m => m.Name == "EngineOnUpdate");
        var instructions = engine.Body.Instructions.ToList();
        int gate = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "ShouldRecoverOverlayClose");
        int native = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "Invoke" && m.DeclaringType.Name == "orig_Update");
        Assert.Contains(instructions.Take(native), i => i.Operand is MethodReference m && m.Name == "get_IsTransientMouseUiActive");
        Assert.Contains(instructions.Skip(native + 1).Take(gate - native - 1), i => i.Operand is MethodReference m && m.Name == "get_IsTransientMouseUiActive");
    }

    [Fact]
    public void NormalHandlerMarksPollBeforeAnyInputOrEarlyReturn() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition handler = module.GetType(typeof(AkronModule).FullName).Methods.Single(m => m.Name == "HandleHotkeys");
        var instructions = handler.Body.Instructions.ToList();
        int mark = instructions.FindIndex(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f && f.Name == "levelHotkeyPollGeneration");
        int input = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "GetState");
        Assert.True(mark >= 0 && input > mark);
        Assert.DoesNotContain(instructions.Take(mark), i => i.OpCode.FlowControl == FlowControl.Return || i.OpCode.FlowControl == FlowControl.Cond_Branch);
    }

    [Fact]
    public void RecoveryFollowsSuccessfulNativeUpdateAndReusesOrdinaryHandler() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition engine = module.GetType(typeof(AkronModule).FullName).Methods.Single(m => m.Name == "EngineOnUpdate");
        var instructions = engine.Body.Instructions.ToList();
        int gate = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "ShouldRecoverOverlayClose");
        int handler = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "HandleHotkeys");
        Assert.True(gate > 0 && handler > gate);
        Assert.Contains(instructions.Take(gate), i => i.Operand is MethodReference m && m.Name == "Invoke" && m.DeclaringType.Name == "orig_Update");
        Assert.Contains(instructions.Skip(gate + 1).Take(handler - gate - 1), i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
        Assert.DoesNotContain(engine.Body.ExceptionHandlers, h => h.HandlerType == ExceptionHandlerType.Finally);
    }
}
