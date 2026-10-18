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
        Assert.Equal(OpCodes.Ldc_I4_1, instructions[handler - 1].OpCode);
        Assert.Contains(instructions.Take(gate), i => i.Operand is MethodReference m && m.Name == "Invoke" && m.DeclaringType.Name == "orig_Update");
        Assert.Contains(instructions.Skip(gate + 1).Take(handler - gate - 1), i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
        Assert.DoesNotContain(engine.Body.ExceptionHandlers, h => h.HandlerType == ExceptionHandlerType.Finally);
    }

    [Fact]
    public void OverlayOnlyGuardReturnsBeforeAnyGameplayBinding() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition handler = module.GetType(typeof(AkronModule).FullName).Methods.Single(m => m.Name == "HandleHotkeys");
        var instructions = handler.Body.Instructions.ToList();
        int mode = instructions.FindIndex(i => i.OpCode == OpCodes.Ldarg_1);
        int actions = instructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "CanExecuteLevelActionBindings");
        Assert.True(mode >= 0 && actions > mode);
        int branch = instructions.FindIndex(mode + 1, i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
        Assert.True(branch > mode && branch < actions);
        Assert.True(instructions[branch].OpCode == OpCodes.Brfalse || instructions[branch].OpCode == OpCodes.Brfalse_S);
        // Debug builds route early returns through a shared epilogue; follow the
        // true-mode path rather than assuming Release's direct four-instruction ret.
        Instruction cursor = instructions[branch + 1];
        bool resetRepeat = false;
        for (int steps = 0; steps < instructions.Count; steps++) {
            Assert.NotEqual(instructions[actions], cursor);
            if (cursor.Operand is MethodReference call) {
                Assert.Equal(typeof(AkronModule).FullName, call.DeclaringType.FullName);
                Assert.Contains(call.Name, new[] { "get_Session", "ResetStepHoldRepeat" });
                if (call.Name == "ResetStepHoldRepeat") resetRepeat = true;
            }
            if (cursor.OpCode == OpCodes.Ret) {
                Assert.True(resetRepeat);
                return;
            }
            cursor = cursor.OpCode.FlowControl == FlowControl.Branch
                ? Assert.IsType<Instruction>(cursor.Operand) : cursor.Next;
            Assert.NotNull(cursor);
        }
        Assert.Fail("Overlay-only path did not reach its return.");
    }
}
