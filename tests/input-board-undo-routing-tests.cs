using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class InputBoardUndoRoutingTests
{
    [Fact]
    public void BoardUndoReadsTheEditorsInputRatherThanDisabledGameplayInput()
    {
        // This checks compiled routing without a graphics device. Real Ctrl+Z
        // delivery and text-item exclusion require separate live checks in Celeste.
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronOverlay).Assembly.Location);
        MethodDefinition shortcuts = module.GetType(typeof(AkronOverlay).FullName).Methods
            .Single(method => method.Name == "HandleInputBoardEditorShortcuts");
        MethodReference[] calls = shortcuts.Body.Instructions.Select(instruction => instruction.Operand)
            .OfType<MethodReference>().ToArray();
        Assert.Contains(calls, method => method.DeclaringType.FullName == "ImGuiNET.ImGui" && method.Name == "IsKeyPressed");
        Assert.Contains(calls, method => method.DeclaringType.FullName == "ImGuiNET.ImGuiIOPtr" && method.Name == "get_KeyCtrl");
        Assert.Contains(calls, method => method.DeclaringType.FullName == "ImGuiNET.ImGui" && method.Name == "IsAnyItemActive");
        Assert.Contains(calls, method => method.Name == "get_WantTextInput");
        Assert.DoesNotContain(calls, method => method.DeclaringType.FullName.Contains("KeyboardData") && method.Name == "Pressed");
    }
}
