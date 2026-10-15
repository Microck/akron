using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DeathParticleLifetimeTests {
    [Theory]
    [InlineData(0f, 0.1f, 0f)]
    [InlineData(0.05f, 0.1f, 0.5f)]
    [InlineData(0.1f, 0.1f, 1f)]
    [InlineData(0.2f, 0.1f, 1f)]
    [InlineData(0.417f, 0.834f, 0.5f)]
    [InlineData(0.834f, 0.834f, 1f)]
    [InlineData(0.834f, 3f, 0.278f)]
    [InlineData(1.5f, 3f, 0.5f)]
    [InlineData(3f, 3f, 1f)]
    [InlineData(4f, 3f, 1f)]
    [InlineData(-1f, 3f, 0f)]
    public void IndependentAgeUsesSelectedLifetime(float age, float duration, float expected) {
        Assert.Equal(expected, AkronModule.ResolveDeathParticleLifetimeProgress(age, duration), 3);
    }

    [Fact]
    public void BurstOwnerDoesNotWriteNativeDeathTimingOrCallbacks() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var owner = module.NestedTypes.Single(t => t.Name == "CustomDeathParticleBurst");
        var methods = owner.Methods.Concat(module.Methods.Where(m => m.Name == "EnsureCustomDeathParticleBurst"));
        Assert.DoesNotContain(methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions), i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
            (f.DeclaringType.FullName == "Celeste.DeathEffect" || f.DeclaringType.FullName == "Celeste.PlayerDeadBody"));
    }

    [Fact]
    public void LifecycleWiringCoversRetryPauseAndStateRestore() {
        // Structural checks supplement the native gameplay comparison. They do
        // not simulate native entity lifetime, wipe ordering, or render fallback.
        var root = Path.Combine(AppContext.BaseDirectory, "../../../../Source");
        string runtime = File.ReadAllText(Path.Combine(root, "Runtime/akron-death-particles.cs"));
        Assert.Contains("Tags.Global | Tags.TransitionUpdate | Tags.PauseUpdate", runtime);
        Assert.Contains("if (owner.FrozenOrPaused)", runtime);
        Assert.Contains("session.Level == room && !owner.Transitioning", runtime);
        Assert.Contains("AkronSaveLoadService.IgnoreSaveState(this)", runtime);
        Assert.Contains("vanillaDeathParticleDrawDepth--", runtime);
        Assert.Contains("if (RenderFailed)", runtime);
        Assert.Contains("private void RenderVanillaFallback()", runtime);
        Assert.Contains("DeathEffect.Draw(Position, appearance.Primary,", runtime);
        Assert.DoesNotContain("!ShouldUseCustomDeathParticles() || burst.RenderFailed", runtime);
        Assert.Contains("public override void Added(Scene scene)", runtime);
        string transition = File.ReadAllText(Path.Combine(root, "Runtime/akron-render-transition-suppression.cs"));
        Assert.DoesNotContain("ClearCustomDeathParticleBursts();", transition);
        string saveLoad = File.ReadAllText(Path.Combine(root, "SaveLoad/AkronSaveLoad.cs"));
        int saveStart = saveLoad.IndexOf("public static AkronSaveLoadResult SaveRuntimeState(", StringComparison.Ordinal);
        int saveEnd = saveLoad.IndexOf("private static bool IsStartPosSlotName", saveStart, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearCustomDeathParticleBursts", saveLoad[saveStart..saveEnd]);
        Assert.Contains("AkronModule.ClearCustomDeathParticleBursts();", saveLoad);
        Assert.Contains("using var deathBurstCapture = AkronModule.PreserveCustomDeathBurstsForCapture(level);", saveLoad);
        Assert.Contains("customDeathBurstCaptureDepth > 0", runtime);
        Assert.Contains("burst.RebindAfterCapture(level)", runtime);
        Assert.Contains("(!mutationStarted || restored)", runtime);
        Assert.Contains("deathBurstCapture.MarkMutationStarted();", saveLoad);
        Assert.Contains("deathBurstCapture.MarkRestored();", saveLoad);
        int coldStart = saveLoad.IndexOf("private static AkronSaveLoadResult RestorePersistentRuntimeState(", StringComparison.Ordinal);
        int coldEnd = saveLoad.IndexOf("private static AkronSaveLoadResult CacheRestoredRuntimeState", coldStart, StringComparison.Ordinal);
        string coldRestore = saveLoad[coldStart..coldEnd];
        Assert.DoesNotContain("ClearCustomDeathParticleBursts();", coldRestore);
        Assert.Contains("using var deathBurstRollback = AkronModule.PreserveCustomDeathBurstsForCapture(level);", coldRestore);
        Assert.Contains("deathBurstRollback.MarkMutationStarted();", coldRestore);
        Assert.Contains("if (rollbackResult == AkronSaveLoadResult.Success)", coldRestore);
        Assert.Contains("deathBurstRollback.MarkRestored();", coldRestore);
        string actions = File.ReadAllText(Path.Combine(root, "Module/akron-module-actions.cs"));
        int reloadStart = actions.IndexOf("private static void ReloadRoom(", StringComparison.Ordinal);
        int reloadEnd = actions.IndexOf("private static void OpenDebugMap", reloadStart, StringComparison.Ordinal);
        Assert.Contains("ClearCustomDeathParticleBursts();", actions[reloadStart..reloadEnd]);
    }
}
