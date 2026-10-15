using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReloadTeardownScopeTests {
    private static T Bare<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void SameEntities(IReadOnlyList<Entity> expected, IReadOnlyList<Entity> actual) {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++) Assert.Same(expected[i], actual[i]);
    }

    [Fact]
    public void OnlyActiveMatchingUnloadReordersNativeSelectionWithoutMutatingIt() {
        var level = Bare<Level>();
        var other = Bare<Level>();
        var first = Bare<Entity>();
        var second = Bare<Entity>();
        var player = Bare<Player>();
        var player2 = Bare<Player>();
        var native = new List<Entity> { first, player, second, player2 };
        Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, level));
        AkronModule.RunRoomReloadWithTeardown(level, () => {
            Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, other));
            SameEntities(new Entity[] { player, player2, first, second },
                AkronModule.OrderRoomReloadEntities(native, level));
            SameEntities(new Entity[] { first, player, second, player2 }, native);
        });
        Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, level));
    }

    [Fact]
    public void VetoAndDeferredOrigLeaveNoOrderingScopeBehind() {
        var level = Bare<Level>();
        var native = new List<Entity> { Bare<Entity>(), Bare<Player>() };
        var original = native.ToArray();
        Action? deferred = null;
        AkronModule.RunRoomReloadWithTeardown(level, () => {
            // A Reload hook may retain orig without invoking native unload now.
            deferred = () => Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, level));
        });
        SameEntities(original, native);
        deferred!();
        AkronModule.RunRoomReloadWithTeardown(level, () => { }); // complete veto
        Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, level));
    }

    [Fact]
    public void NestedAndThrowingReloadRestorePreviousScope() {
        var outer = Bare<Level>();
        var inner = Bare<Level>();
        var native = new List<Entity> { Bare<Entity>(), Bare<Player>() };
        var expected = new InvalidOperationException("veto throws");
        AkronModule.RunRoomReloadWithTeardown(outer, () => {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
                AkronModule.RunRoomReloadWithTeardown(inner, () => {
                    Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, outer));
                    Assert.NotSame(native, AkronModule.OrderRoomReloadEntities(native, inner));
                    throw expected;
                })));
            Assert.NotSame(native, AkronModule.OrderRoomReloadEntities(native, outer));
            Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, inner));
        });
        Assert.Same(native, AkronModule.OrderRoomReloadEntities(native, outer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IlPatchRequiresOneNativeUnloadCallAndKeepsNativeCall(int calls) {
        using var module = ModuleDefinition.CreateModule("UnloadFixture", ModuleKind.Dll);
        var owner = new TypeDefinition("Fixture", "Wrapper", Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(owner);
        var method = new MethodDefinition("UnloadLevel", Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        owner.Methods.Add(method);
        var levelType = module.ImportReference(typeof(Level));
        var listType = module.ImportReference(typeof(List<Entity>));
        method.Parameters.Add(new ParameterDefinition(levelType));
        method.Parameters.Add(new ParameterDefinition(listType));
        var unload = new MethodReference("UnloadEntities", module.TypeSystem.Void, levelType) { HasThis = true };
        unload.Parameters.Add(new ParameterDefinition(listType));
        var processor = method.Body.GetILProcessor();
        for (int i = 0; i < calls; i++) {
            processor.Emit(OpCodes.Ldarg_0);
            processor.Emit(OpCodes.Ldarg_1);
            processor.Emit(OpCodes.Callvirt, unload);
        }
        processor.Emit(OpCodes.Ret);
        var original = method.Body.Instructions.ToArray();
        using var context = new ILContext(method);
        AkronModule.LevelOnUnloadLevelForRoomReload(context);
        Assert.Equal(calls, method.Body.Instructions.Count(i => ReferenceEquals(i.Operand, unload)));
        if (calls == 1) {
            Assert.True(method.Body.Instructions.Count > original.Length);
            var call = method.Body.Instructions.Single(i => ReferenceEquals(i.Operand, unload));
            Assert.NotSame(original[1], call.Previous);
        } else {
            Assert.Equal(original, method.Body.Instructions.ToArray());
        }
    }
}
