using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class EarAidLateEnableTests {
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void OriginalVolumeIsCapturedOnlyWhenAcquiringOwnership(bool wasApplied, bool apply, bool expected) {
        Assert.Equal(expected, AkronEarAid.ShouldCaptureOriginalVolume(wasApplied, apply));
    }

    [Fact]
    public void DisabledCreationDoesNotReturnBeforeRegisteringMatchedEvent() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronEarAid).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronEarAid));
        var method = type.Methods.Single(m => m.Name == nameof(AkronEarAid.ApplyVolume));
        var instructions = method.Body.Instructions.ToList();
        int enabled = instructions.FindIndex(i => i.Operand is MethodReference call && call.Name == nameof(AkronEarAid.OverrideEnabled));
        int reconcile = instructions.FindIndex(i => i.Operand is MethodReference call && call.Name == "ReconcileVolumeOverride");
        int add = instructions.FindIndex(i => i.Operand is MethodReference call && call.Name == "Add");
        Assert.True(enabled >= 0 && enabled < reconcile && reconcile < add);
        Assert.DoesNotContain(instructions.Skip(enabled).Take(add - enabled),
            i => i.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch ||
                 i.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Return);
    }

    [Fact]
    public void AcquisitionReadsCurrentVolumeBeforeNativeMutation() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronEarAid).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronEarAid));
        var method = type.Methods.Single(m => m.Name == "ReconcileVolumeOverride");
        string[] calls = method.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToArray();
        int capture = System.Array.IndexOf(calls, "ShouldCaptureOriginalVolume");
        int read = System.Array.IndexOf(calls, "getVolume");
        int policy = System.Array.IndexOf(calls, "TryUseRuntime");
        int write = System.Array.IndexOf(calls, "setVolume");
        Assert.True(capture >= 0 && capture < read && read < policy && policy < write);
    }
}
