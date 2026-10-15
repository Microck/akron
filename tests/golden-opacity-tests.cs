using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class GoldenOpacityTests {
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(55, 110, 55, 27, 88)]
    [InlineData(100, 200, 100, 50, 160)]
    [InlineData(-5, 0, 0, 0, 0)]
    [InlineData(105, 200, 100, 50, 160)]
    public void MultipliesAllPremultipliedChannelsAndClamps(int opacity, int r, int g, int b, int a) {
        var original = new Color(200, 100, 50, 160);
        var actual = AkronModule.ModulateGoldenSpriteColor(original, opacity);
        Assert.Equal(new Color(r, g, b, a), actual);
        Assert.Equal(new Color(200, 100, 50, 160), original);
    }

    [Fact]
    public void RetainsNativeGhostTintAndDoesNotCompoundWhenRenderingAgain() {
        var original = Color.White * .8f;
        var expected = original * .55f;
        for (int i = 0; i < 1000; i++) Assert.Equal(expected, AkronModule.ModulateGoldenSpriteColor(original, 55));
        Assert.Equal(original, AkronModule.ModulateGoldenSpriteColor(original, 100));
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
