using Xunit;
namespace Celeste.Mod.Akron.Tests;

public sealed class GeneratedOshiroTimeTests {
    [Theory]
    [InlineData(1f)]
    [InlineData(0.75f)]
    public void FinalRemovalRestoresPriorRateInsteadOfUnconditionalOne(float prior) {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, prior, .6f);
        lease.Observe(actor, scene, true, .6f, .5f);
        Assert.True(lease.Remove(actor, scene, scene, .5f, out float restored));
        Assert.Equal(prior, restored);
        Assert.False(lease.Remove(actor, scene, scene, restored, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedContributorsCannotRestoreEachOthersStaleSlowdown(bool removeFirstWriterFirst) {
        var lease = new AkronGeneratedTimeRateLease(); var a = new object(); var b = new object(); var scene = new object();
        lease.Observe(a, scene, true, 1f, .5f);
        lease.Observe(b, scene, true, .5f, .5f); // identical-value write still contributes
        Assert.False(lease.Remove(removeFirstWriterFirst ? a : b, scene, scene, .5f, out _));
        Assert.True(lease.Remove(removeFirstWriterFirst ? b : a, scene, scene, .5f, out float restored));
        Assert.Equal(1f, restored);
    }

    [Fact]
    public void NativeOshiroSameValueWriteSupersedesGeneratedOwnership() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.Observe(new object(), scene, false, .5f, .5f);
        Assert.False(lease.Remove(actor, scene, scene, .5f, out _));
    }

    [Fact]
    public void DistinctExternalWriteIsNeverOverwrittenOnRemoval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(actor, scene, scene, .7f, out _));
    }

    [Fact]
    public void NewGeneratedWriteAfterExternalChangeCapturesNewBaseline() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.Observe(actor, scene, true, .7f, .5f);
        Assert.True(lease.Remove(actor, scene, scene, .5f, out float restored));
        Assert.Equal(.7f, restored);
    }

    [Fact]
    public void SceneChangeCannotRestoreOldRate() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(actor, scene, new object(), .5f, out _));
    }

    [Fact]
    public void SceneEndClearsOnlyItsOwnInterval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.EndScene(new object());
        Assert.True(lease.Release(scene, .5f, out _));
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.EndScene(scene);
        Assert.False(lease.Release(scene, .5f, out _));
    }

    [Fact]
    public void UnrelatedRemovalDoesNotReleaseInterval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.False(lease.Remove(new object(), scene, scene, .5f, out _));
        Assert.True(lease.Release(scene, .5f, out float restored));
        Assert.Equal(1f, restored);
        Assert.False(lease.Release(scene, .5f, out _));
    }
    [Fact]
    public void CompetingSameValueWriteInvalidatesAndRebasesNextGeneratedInterval() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        lease.Clear(); // Every observed competing store invalidates, regardless of its value.
        Assert.False(lease.Remove(actor, scene, scene, .5f, out _));
        lease.Observe(actor, scene, true, .5f, .4f);
        Assert.True(lease.Remove(actor, scene, scene, .4f, out float restored));
        Assert.Equal(.5f, restored);
    }

    [Fact]
    public void CompetingObserverPreservesStoreAndRetargetsIncomingBranch() {
        using var module = Mono.Cecil.ModuleDefinition.CreateModule("ClockFixture", Mono.Cecil.ModuleKind.Dll);
        var type = new Mono.Cecil.TypeDefinition("Fixture", "Writer", Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new Mono.Cecil.MethodDefinition("Write", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
        type.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        var store = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Stsfld,
            module.ImportReference(typeof(Monocle.Engine).GetField(nameof(Monocle.Engine.TimeRate))));
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, .5f);
        var branch = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Br, store);
        il.Append(branch); il.Append(store); il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        using var context = new MonoMod.Cil.ILContext(method);
        context.Invoke(AkronGeneratedOshiroTime.ObserveCompetingWrites);
        Assert.True(AkronGeneratedOshiroTime.IsTimeRateStore(store));
        Assert.NotSame(store, branch.Operand);
        Assert.NotSame(branch, store.Previous);
        Assert.Equal(.5f, AkronGeneratedOshiroTime.ObserveCompetingWrite(.5f));
    }
    [Theory]
    [InlineData(.5f)]
    [InlineData(.75f)]
    public void ExplicitTimescaleTakeoverKeepsUnderlyingBaselineAndConsumesGeneratedLease(float overrideRate) {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, 1f, .5f);
        Assert.True(lease.Release(scene, .5f, out float baseline));
        Assert.Equal(1f, baseline);
        Assert.False(lease.Remove(actor, scene, scene, overrideRate, out _));
        // A later generated write under the override is invalidated by the next explicit write.
        lease.Observe(actor, scene, true, overrideRate, .5f);
        lease.Clear();
        Assert.False(lease.Remove(actor, scene, scene, overrideRate, out _));
        Assert.Equal(1f, baseline);
    }
}
