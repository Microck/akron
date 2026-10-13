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
