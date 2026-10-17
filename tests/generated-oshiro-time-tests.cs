using Xunit;
namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
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
    [Theory]
    [InlineData(.75f, .5f, .75f, 1f)]
    [InlineData(.5f, .4f, .5f, 1f)]
    [InlineData(.7f, .5f, .75f, .7f)]
    public void ReleasingUnderlyingTimescaleDoesNotResurrectItsOverride(
        float baseline, float generatedRate, float departingOverride, float expected) {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, baseline, generatedRate);
        lease.RebaseUnderlying(scene, generatedRate, departingOverride, 1f);
        Assert.True(lease.Remove(actor, scene, scene, generatedRate, out float restored));
        Assert.Equal(expected, restored);
    }

    [Fact]
    public void RebaseDoesNotTouchAnotherScenesLease() {
        var lease = new AkronGeneratedTimeRateLease(); var actor = new object(); var scene = new object();
        lease.Observe(actor, scene, true, .75f, .5f);
        lease.RebaseUnderlying(new object(), .5f, .75f, 1f);
        Assert.True(lease.Remove(actor, scene, scene, .5f, out float restored));
        Assert.Equal(.75f, restored);
    }
    [Theory]
    [InlineData(.75f, true, true)]
    [InlineData(.75f, true, false)]
    [InlineData(.5f, true, true)]
    [InlineData(.5f, true, false)]
    [InlineData(.75f, false, true)]
    [InlineData(.75f, false, false)]
    [InlineData(.5f, false, true)]
    [InlineData(.5f, false, false)]
    public void ActualTimescaleMethodsReleaseBothOwnersWithoutResurrectingEither(
        float multiplier, bool timescaleFirst, bool removeGeneratedFirst) {
        const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var instance = typeof(Monocle.Engine).GetField("<Instance>k__BackingField", statics);
        var previousEngine = instance.GetValue(null);
        var engine = (Monocle.Engine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Monocle.Engine));
        System.GC.SuppressFinalize(engine); // No FNA/native constructor ran.
        var scene = (Monocle.Scene)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Monocle.Scene));
        typeof(Monocle.Engine).GetField("scene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(engine, scene);
        var lease = (AkronGeneratedTimeRateLease)typeof(AkronGeneratedOshiroTime).GetField("Lease", statics).GetValue(null);
        float previousRate = Monocle.Engine.TimeRate;
        var session = new AkronModuleSession { TimescaleEnabled = true, TimescaleMultiplier = multiplier };
        var actor = new object();
        try {
            AkronModule.ReleaseTimescale();
            AkronPolicy.UnloadMapRestrictions();
            lease.Clear(); instance.SetValue(null, engine);
            Monocle.Engine.TimeRate = 1f;
            if (timescaleFirst) AkronModule.ApplyTimescale(session);
            lease.Observe(actor, scene, true, Monocle.Engine.TimeRate, .5f);
            Monocle.Engine.TimeRate = .5f;
            if (!timescaleFirst) AkronModule.ApplyTimescale(session);
            void RemoveGenerated() {
                if (lease.Remove(actor, scene, scene, Monocle.Engine.TimeRate, out float restore) && !AkronModule.OwnsCurrentTimescale)
                    Monocle.Engine.TimeRate = restore;
            }
            if (removeGeneratedFirst) { RemoveGenerated(); AkronModule.ReleaseTimescale(); }
            else { AkronModule.ReleaseTimescale(); RemoveGenerated(); }
            Assert.Equal(1f, Monocle.Engine.TimeRate);
            Assert.False(AkronModule.OwnsCurrentTimescale);
        } finally {
            AkronModule.ReleaseTimescale(); lease.Clear();
            instance.SetValue(null, previousEngine);
            Monocle.Engine.TimeRate = previousRate;
            AkronPolicy.UnloadMapRestrictions();
        }
    }
}
