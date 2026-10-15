using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class GoldenOpacityTests {
    [Fact]
    public void ModulationUsesNativePremultipliedMultiplyAfterClamping() {
        // CI references deliberately strip FNA method bodies. Verify our emitted call path
        // without executing stripped Color operators; native color behavior is covered by live QA.
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var method = module.Methods.Single(m => m.Name == "ModulateGoldenSpriteColor");
        var instructions = method.Body.Instructions;
        int clamp = instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "ClampGoldenTransparencyOpacity");
        int multiply = instructions.ToList().FindIndex(i => i.Operand is MethodReference m &&
            m.Name == "op_Multiply" && m.DeclaringType.FullName == "Microsoft.Xna.Framework.Color" &&
            m.Parameters.Count == 2 && m.Parameters[1].ParameterType.FullName == "System.Single");
        Assert.True(clamp >= 0 && multiply > clamp);
        Assert.Contains(instructions, i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 100f);
        Assert.Contains(instructions, i => i.OpCode == OpCodes.Div);
        Assert.DoesNotContain(instructions, i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "Microsoft.Xna.Framework.Color");
    }

    [Fact]
    public void RenderingRestoresExactColorInFinallyAndUpdateWriterIsRemoved() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        Assert.DoesNotContain(module.Methods, m => m.Name == "UpdateGoldenTransparency");
        var render = module.Methods.Single(m => m.Name == "GoldenSpriteOnRender");
        Assert.Contains(render.Body.Instructions, i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.FullName == "Monocle.Sprite");
        Assert.Contains(render.Body.Instructions, i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.FullName == "Celeste.Strawberry");
        var finallyHandler = Assert.Single(render.Body.ExceptionHandlers, h => h.HandlerType == ExceptionHandlerType.Finally);
        Assert.Contains(render.Body.Instructions.Where(i => i.Offset >= finallyHandler.HandlerStart.Offset && i.Offset < finallyHandler.HandlerEnd.Offset),
            i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "Color");
        foreach (var pair in new[]{("Load", "add_Render"), ("Unload", "remove_Render")}) {
            Assert.Contains(module.Methods.Single(m => m.Name == pair.Item1).Body.Instructions,
                i => i.Operand is MethodReference m && m.Name == pair.Item2 && m.DeclaringType.FullName == "On.Monocle.Image");
        }
    }
}
