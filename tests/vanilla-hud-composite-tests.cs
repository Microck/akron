using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class VanillaHudCompositeTests
{
    [Theory]
    [InlineData("Load", "add_Render", "On.Celeste.HiresRenderer", "HiresRendererOnRender")]
    [InlineData("Load", "add_Render", "IL.Celeste.Level", "LevelOnRenderForSubHudVisibility")]
    [InlineData("Unload", "remove_Render", "On.Celeste.HiresRenderer", "HiresRendererOnRender")]
    [InlineData("Unload", "remove_Render", "IL.Celeste.Level", "LevelOnRenderForSubHudVisibility")]
    public void FinalCompositeHookIsRegisteredAndRemoved(string methodName, string accessor, string hookType, string handler)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        var method = module.GetType(typeof(AkronModule).FullName).Methods.Single(item => item.Name == methodName);
        Assert.Contains(method.Body.Instructions, instruction => instruction.Operand is MethodReference call &&
            call.DeclaringType.FullName == hookType && call.Name == accessor);
        Assert.Contains(method.Body.Instructions, instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldftn &&
            instruction.Operand is MethodReference call && call.Name == handler);
    }

    [Theory]
    [InlineData("HiresRendererOnRender")]
    [InlineData("ShouldSuppressSubHudRender")]
    public void BothRenderPathsUseCurrentIdentityAndPolicyGate(string methodName)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        var method = module.GetType(typeof(AkronModule).FullName).Methods.Single(item => item.Name == methodName);
        Assert.Contains(method.Body.Instructions, instruction => instruction.Operand is MethodReference call &&
            call.Name == "ShouldSuppressNativeHudRender");
    }

    [Fact]
    public void SubHudGateRetargetsIncomingBranchesAndPreservesOriginalCall()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("SubHudGateFixture", ModuleKind.Dll);
        var type = new TypeDefinition("Fixtures", "LevelRender", TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var render = new MethodDefinition("Render", MethodAttributes.Public, module.TypeSystem.Void);
        type.Methods.Add(render);
        var body = render.Body.Instructions;
        var originalStart = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        var incoming = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Br, originalStart);
        var originalCall = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Callvirt,
            module.ImportReference(typeof(Monocle.Renderer).GetMethod("Render")!));
        var after = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret);
        body.Add(incoming);
        body.Add(originalStart);
        body.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldfld,
            module.ImportReference(typeof(Level).GetField(nameof(Level.SubHudRenderer))!)));
        body.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
        body.Add(originalCall);
        body.Add(after);
        using var context = new MonoMod.Cil.ILContext(render);
        var hook = typeof(AkronModule).GetMethod("LevelOnRenderForSubHudVisibility",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        context.Invoke(il => hook.Invoke(null, new object[] { il }));
        Assert.Contains(originalStart, body);
        Assert.Contains(originalCall, body);
        var target = Assert.IsType<Mono.Cecil.Cil.Instruction>(incoming.Operand);
        Assert.NotSame(originalStart, target);
        Assert.True(body.IndexOf(target) < body.IndexOf(originalStart));
        var skip = Assert.Single(body, instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Brtrue ||
            instruction.OpCode == Mono.Cecil.Cil.OpCodes.Brtrue_S);
        Assert.Same(after, skip.Operand);
    }

    [Fact]
    public void OnlyCurrentNativeHudRenderersMatch()
    {
        object hud = new();
        object subHud = new();
        object unrelated = new();
        Assert.True(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(hud, hud, subHud));
        Assert.True(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(subHud, hud, subHud));
        Assert.False(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(unrelated, hud, subHud));
        Assert.False(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(null!, hud, subHud));
    }

    [Fact]
    public void ReplacedRendererMatchesWithoutAnUpdateTimeSnapshot()
    {
        object oldHud = new();
        object restoredHud = new();
        object subHud = new();
        Assert.True(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(oldHud, oldHud, subHud));
        Assert.False(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(oldHud, restoredHud, subHud));
        Assert.True(AkronRuntimeOptions.IsCurrentVanillaHudRenderer(restoredHud, restoredHud, subHud));
    }

    [Fact]
    public void VanillaHidingDoesNotWriteRendererVisibility()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronRuntimeOptions).Assembly.Location);
        var method = module.GetType(typeof(AkronRuntimeOptions).FullName).Methods.Single(item => item.Name == "ApplyHudVisibility");
        Assert.DoesNotContain(method.Body.Instructions, instruction => instruction.Operand is MethodReference call &&
            (call.Name == "SetHudRendererVisibility" || call.Name == "SetVisible" || call.Name == "set_Visible"));
    }
}
