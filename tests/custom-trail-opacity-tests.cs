using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CustomTrailOpacityTests {
    [Theory]
    [InlineData(-20, 0f)]
    [InlineData(0, 0f)]
    [InlineData(10, 0.1f)]
    [InlineData(45, 0.45f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1f)]
    [InlineData(120, 1f)]
    public void CustomOpacityIsClampedSeparatelyFromLifetime(int percent, float expected) {
        Assert.Equal(expected, AkronModule.ResolveForcedTrailOpacity(true, percent));
        Assert.Equal(0.45f, AkronModule.ForcedTrailDurationSeconds);
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(100)]
    public void NonCustomForcedTrailKeepsFullNativeColor(int percent) {
        Assert.Equal(1f, AkronModule.ResolveForcedTrailOpacity(false, percent));
    }
    [Fact]
    public void EmissionCallKeepsLifetimeAfterColorMultiplyAndHasSkipBranch() {
        // Structural regression checks work with CI's stripped FNA references.
        // Actual native alpha and visible behavior are verified in live QA.
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var method = assembly.MainModule.GetType(typeof(AkronModule).FullName).Methods
            .Single(m => m.Name == "ApplyVisualPlayerOverrides");
        var code = method.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToList();
        int add = code.FindIndex(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "Celeste.TrailManager" && m.Name == "Add");
        Assert.True(add >= 2);
        Assert.Equal(OpCodes.Ldc_R4, code[add - 1].OpCode);
        Assert.Equal(0.45f, (float)code[add - 1].Operand);
        var multiply = Assert.IsAssignableFrom<MethodReference>(code[add - 2].Operand);
        Assert.Equal("Microsoft.Xna.Framework.Color", multiply.DeclaringType.FullName);
        Assert.Equal("op_Multiply", multiply.Name);
        int resolve = code.FindIndex(i => i.Operand is MethodReference m && m.Name == "ResolveForcedTrailOpacity");
        Assert.True(resolve >= 0 && resolve < add - 2);
        Assert.Contains(code.Skip(resolve + 1).Take(add - resolve - 3), i =>
            i.OpCode.FlowControl == FlowControl.Cond_Branch &&
            i.Operand is Instruction target && target.Offset > code[add].Offset);
    }
}
