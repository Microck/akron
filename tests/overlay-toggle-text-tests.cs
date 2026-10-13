using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayToggleTextTests
{
    [Fact]
    public void KeyboardToggleConsumesStateBeforeCheckingTextOwnership()
    {
        // Reference-only FNA cannot execute KeyboardState methods in this suite.
        // Check compiled wiring here; real key delivery/blur is a native QA gate.
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition toggle = module.GetType(typeof(AkronModule).FullName).Methods
            .Single(method => method.Name == "IsOverlayTogglePressed");
        var instructions = toggle.Body.Instructions.ToList();
        int remember = instructions.FindIndex(instruction => instruction.OpCode == OpCodes.Stsfld &&
            instruction.Operand is FieldReference field && field.Name == "previousOverlayToggleKeyboard");
        int text = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "get_WantTextInput" && method.DeclaringType.FullName == typeof(AkronImGuiRenderer).FullName);
        int keyboard = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "IsKeyboardBindingPressed");
        Assert.True(remember >= 0 && text > remember && keyboard > text);
        // A true text-ownership result must jump over the keyboard toggle check.
        Assert.Contains(instructions.Skip(text + 1).Take(keyboard - text - 1), instruction =>
            (instruction.OpCode == OpCodes.Brtrue || instruction.OpCode == OpCodes.Brtrue_S) &&
            instruction.Operand is Instruction target && instructions.IndexOf(target) > keyboard);
    }
}

public sealed class OverlayToggleDiagnosticsTests
{
    [Theory]
    [InlineData("HandleHotkeys")]
    [InlineData("HandleGlobalOverlayHotkeys")]
    public void DiagnosticsGuardConsumesToggleStateBeforeReturning(string methodName)
    {
        // Reference-only FNA cannot provide live keyboard state. Verify both scene routes
        // consume the snapshot inside the diagnostics guard; native held-key QA is separate.
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition handler = module.GetType(typeof(AkronModule).FullName).Methods
            .Single(method => method.Name == methodName);
        var instructions = handler.Body.Instructions.ToList();
        int guard = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "get_IsOpen" && method.DeclaringType.Name == "AkronDiagnosticsMenu");
        Assert.True(guard >= 0);
        int branch = instructions.FindIndex(guard + 1,
            instruction => instruction.OpCode.FlowControl == FlowControl.Cond_Branch);
        Assert.True(branch > guard);
        Instruction continuation = Assert.IsType<Instruction>(instructions[branch].Operand);
        int continuationIndex = instructions.IndexOf(continuation);
        int refresh = instructions.FindIndex(branch + 1, instruction => instruction.Operand is MethodReference method &&
            method.Name == "RefreshOverlayToggleKeyboardState");
        Assert.InRange(refresh, branch + 1, continuationIndex - 1);
        Assert.DoesNotContain(instructions.Skip(branch + 1).Take(continuationIndex - branch - 1),
            instruction => instruction.Operand is MethodReference method && method.Name == "IsOverlayTogglePressed");
    }
    [Fact]
    public void DiagnosticsCloseConsumesToggleBeforeReleasingItsOwner()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition close = module.GetType(typeof(AkronDiagnosticsMenu).FullName).Methods
            .Single(method => method.Name == "Close");
        var instructions = close.Body.Instructions.ToList();
        int refresh = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "RefreshOverlayToggleKeyboardState" && method.DeclaringType.FullName == typeof(AkronModule).FullName);
        int release = instructions.FindIndex(instruction => instruction.OpCode == OpCodes.Stsfld &&
            instruction.Operand is FieldReference field && field.Name == "ownerScene");
        Assert.True(refresh >= 0 && release > refresh);
    }

}

[Collection(AkronSharedStateCollection.Name)]
public sealed class OverlayInputSessionReleaseTests
{
    [Fact]
    public void EndingTextInputImmediatelyReleasesPublishedOwnership()
    {
        var property = typeof(AkronImGuiRenderer).GetProperty(nameof(AkronImGuiRenderer.WantTextInput))!;
        bool original = AkronImGuiRenderer.WantTextInput;
        try
        {
            property.SetValue(null, true);
            AkronImGuiRenderer.EndTextInputSession();
            Assert.False(AkronImGuiRenderer.WantTextInput);
        }
        finally
        {
            property.SetValue(null, original);
        }
    }

    [Fact]
    public void RendererBindsCaptureToSessionBeforeInvokingLayout()
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition render = module.GetType(typeof(AkronImGuiRenderer).FullName).Methods
            .Single(method => method.Name == "RenderFrame");
        var instructions = render.Body.Instructions.ToList();
        int revision = instructions.FindIndex(instruction => instruction.OpCode == OpCodes.Ldsfld &&
            instruction.Operand is FieldReference field && field.Name == "inputSessionRevision");
        int layout = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "Invoke" && method.DeclaringType.FullName == "System.Action");
        int publish = instructions.FindIndex(instruction => instruction.Operand is MethodReference method &&
            method.Name == "PublishInputCapture");
        Assert.True(revision >= 0 && layout > revision && publish > layout);
    }

    [Fact]
    public void EndedSessionRejectsOldFrameCaptureAndAllowsNextFrame()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var revision = typeof(AkronImGuiRenderer).GetField("inputSessionRevision", flags);
        var publish = typeof(AkronImGuiRenderer).GetMethod("PublishInputCapture", flags);
        Assert.NotNull(revision);
        Assert.NotNull(publish);
        object originalRevision = revision.GetValue(null)!;
        bool keyboard = AkronImGuiRenderer.WantCaptureKeyboard;
        bool text = AkronImGuiRenderer.WantTextInput;
        bool mouse = AkronImGuiRenderer.WantCaptureMouse;
        try
        {
            publish.Invoke(null, new[] { originalRevision, (object) true, true, true });
            Assert.True(AkronImGuiRenderer.WantTextInput);
            AkronImGuiRenderer.EndTextInputSession();
            // Closing from drawLayout must not republish that frame's stale ImGui IO.
            publish.Invoke(null, new[] { originalRevision, (object) true, true, true });
            Assert.False(AkronImGuiRenderer.WantCaptureKeyboard);
            Assert.False(AkronImGuiRenderer.WantTextInput);
            Assert.False(AkronImGuiRenderer.WantCaptureMouse);
            publish.Invoke(null, new[] { revision.GetValue(null)!, (object) true, true, true });
            Assert.True(AkronImGuiRenderer.WantCaptureKeyboard);
            Assert.True(AkronImGuiRenderer.WantTextInput);
            Assert.True(AkronImGuiRenderer.WantCaptureMouse);
        }
        finally
        {
            revision.SetValue(null, originalRevision);
            publish.Invoke(null, new[] { originalRevision, (object) keyboard, text, mouse });
        }
    }
}
