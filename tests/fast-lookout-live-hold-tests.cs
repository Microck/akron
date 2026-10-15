using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class FastLookoutLiveHoldTests {
    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 6)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 0)]
    public void HookScalesCachedReadsAndLeavesInitializersUntouched(int malformed, int expectedReads) {
        // Isolated IL fixture; native pan timing is verified separately in-game.
        using var module = ModuleDefinition.CreateModule("LookoutFixture", ModuleKind.Dll);
        var state = new TypeDefinition("Fixture", "LookRoutineState", Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(state);
        var accel = new FieldDefinition("accel", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Single);
        var speed = new FieldDefinition("speed", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Single);
        var decoy = new FieldDefinition("decoy", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Single);
        state.Fields.Add(accel); state.Fields.Add(speed); state.Fields.Add(decoy);
        var method = new MethodDefinition("MoveNext", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
        if (malformed == 3) {
            var wrapper = new TypeDefinition("Fixture", "DMDWrapper", Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(wrapper);
            method.Attributes |= Mono.Cecil.MethodAttributes.Static;
            method.Parameters.Add(new ParameterDefinition("self", Mono.Cecil.ParameterAttributes.None, state));
            wrapper.Methods.Add(method);
        } else {
            state.Methods.Add(method);
        }
        var il = method.Body.GetILProcessor();
        void Store(FieldDefinition field, float value) {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, value); il.Emit(OpCodes.Stfld, field);
        }
        Store(accel, 800f);
        if (malformed != 1) Store(speed, 240f);
        if (malformed == 2) Store(decoy, 800f);
        for (int i = 0; i < 3; i++) {
            if (malformed != 5 && !(malformed == 7 && i == 2)) {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, accel); il.Emit(OpCodes.Pop);
            }
            if (malformed != 6) {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, speed); il.Emit(OpCodes.Pop);
            }
        }
        il.Emit(OpCodes.Ldc_R4, 800f); il.Emit(OpCodes.Pop); // unrelated constant
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        var initializers = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld)
            .Select(i => (Store: i, Previous: i.Previous)).ToArray();
        var originalInstructions = method.Body.Instructions.ToArray();
        using var context = new ILContext(method);
        AkronModule.PatchLookoutMovementReads(context, malformed == 4 ? "Wrong.Owner" : state.FullName);
        Assert.Equal(expectedReads, AkronModule.FastLookoutPatchedMovementReadCount);
        foreach (var initializer in initializers) Assert.Same(initializer.Previous, initializer.Store.Previous);
        Assert.Equal(malformed == 5 || malformed == 6 ? 3 : malformed == 7 ? 5 : 6,
            method.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldfld));
        if (expectedReads == 0) Assert.Equal(originalInstructions, method.Body.Instructions.ToArray());
    }
}
