using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class BindingContextDisplayTests {
    [Fact]
    public void RenderedBindingLineUsesBindingFormatterInsteadOfFeatureValue() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition context = module.GetType(typeof(AkronOverlay).FullName).Methods
            .Single(method => method.Name == "DrawImGuiBindingContext");
        var instructions = context.Body.Instructions.ToList();
        int label = instructions.FindIndex(instruction => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == "Binding: ");
        int render = instructions.FindIndex(label + 1, instruction => instruction.Operand is MethodReference call && call.Name == "TextUnformatted");
        Assert.True(label >= 0 && render > label);
        var line = instructions.Skip(label + 1).Take(render - label - 1).ToList();
        Assert.Contains(line, instruction => instruction.Operand is MethodReference call && call.Name == "DescribeEffectiveMenuBinding");
        Assert.DoesNotContain(context.Body.Instructions, instruction => instruction.Operand is MethodReference call && call.Name == "SafeDescribeEntryValue");
        Assert.Contains(context.Body.Instructions, instruction => instruction.Operand is MethodReference call && call.Name == "DescribeBindingForEntry");
        Assert.Contains(context.Body.Instructions, instruction => instruction.Operand is MethodReference call && call.Name == "StartBindingCaptureForEntry");
        Assert.Contains(context.Body.Instructions, instruction => instruction.Operand is MethodReference call && call.Name == "ClearBindingForEntry");
    }
}
