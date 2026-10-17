using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Cil;
using Monocle;

namespace Celeste.Mod.Akron;

// One shared interval: per-actor baselines could restore another removed actor's slowdown.
internal sealed class AkronGeneratedTimeRateLease {
    private readonly HashSet<object> contributors = new();
    private object scene;
    private float before;
    private float last;

    internal void Observe(object owner, object ownerScene, bool generated, float current, float next) {
        if (!generated || ownerScene == null) { Clear(); return; }
        if (scene != ownerScene || contributors.Count == 0 || current != last) {
            Clear();
            scene = ownerScene;
            before = current;
        }
        contributors.Add(owner);
        last = next;
    }

    internal bool Remove(object owner, object ownerScene, object currentScene, float current, out float restore) {
        restore = current;
        if (!contributors.Remove(owner)) return false;
        if (scene != ownerScene || scene != currentScene || current != last) { Clear(); return false; }
        if (contributors.Count != 0) return false;
        restore = before;
        Clear();
        return true;
    }

    internal bool Release(object currentScene, float current, out float restore) {
        restore = before;
        bool owned = contributors.Count > 0 && scene == currentScene && current == last;
        Clear();
        return owned;
    }

    internal bool RebaseUnderlying(object currentScene, float current, float oldBaseline, float newBaseline) {
        if (contributors.Count == 0 || scene != currentScene || current != last || before != oldBaseline)
            return false;
        before = newBaseline;
        return true;
    }

    internal void EndScene(object endedScene) { if (scene == endedScene) Clear(); }
    internal void Clear() { contributors.Clear(); scene = null; before = last = 0f; }
}

internal static class AkronGeneratedOshiroTime {
    private const string GeneratedType = "ExtendedVariants.Entities.AutoDestroyingAngryOshiro";
    private static readonly AkronGeneratedTimeRateLease Lease = new();
    private static bool loaded;
    private static readonly List<ILHook> OtherWriters = new();
    private static bool ownershipProtected;

    internal static void Load() {
        if (loaded) return;
        // Mark the installation active before subscribing so any partial setup can roll back.
        loaded = true;
        try {
            ownershipProtected = ObserveOtherNativeWriters();
            IL.Celeste.AngryOshiro.Update += ObserveTimeRateWrites;
            On.Monocle.Entity.Removed += Removed;
            On.Monocle.Entity.SceneEnd += SceneEnd;
        } catch {
            Unload();
            throw;
        }
    }

    internal static void Unload() {
        if (!loaded) return;
        IL.Celeste.AngryOshiro.Update -= ObserveTimeRateWrites;
        On.Monocle.Entity.Removed -= Removed;
        On.Monocle.Entity.SceneEnd -= SceneEnd;
        loaded = false;
        ownershipProtected = false;
        foreach (ILHook hook in OtherWriters) hook.Dispose();
        OtherWriters.Clear();
        if (Lease.Release(Engine.Scene, Engine.TimeRate, out float restore) && !AkronModule.OwnsCurrentTimescale)
            Engine.TimeRate = restore;
    }

    private static void ObserveTimeRateWrites(ILContext il) {
        var cursor = new ILCursor(il);
        while (cursor.TryGotoNext(MoveType.Before, instruction => instruction.MatchStsfld<Engine>(nameof(Engine.TimeRate)))) {
            cursor.MoveAfterLabels();
            // Keep the native store and its value intact; observe even identical-value writes.
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<float, AngryOshiro, float>>(ObserveWrite);
            cursor.Index++; // Pass the original stsfld before searching again.
        }
    }

    private static float ObserveWrite(float value, AngryOshiro owner) {
        if (!ownershipProtected) { Lease.Clear(); return value; }
        Type type = owner.GetType();
        bool generated = type.FullName == GeneratedType && type.Assembly.GetName().Name == "ExtendedVariantMode";
        Lease.Observe(owner, owner.Scene, generated, Engine.TimeRate, value);
        return value;
    }

    // Discover actual bodies, including iterator MoveNext and compiler-generated callbacks.
    // This is bounded to the loaded native assembly; arbitrary other-mod IL is not covered.
    private static bool ObserveOtherNativeWriters() {
        try {
            System.Reflection.Module runtime = typeof(Engine).Module;
            using ModuleDefinition disk = ModuleDefinition.ReadModule(runtime.Assembly.Location,
                new ReaderParameters { ReadSymbols = false });
            if (disk.Mvid != runtime.ModuleVersionId)
                throw new InvalidOperationException("Loaded native module differs from its disk image.");
            MethodInfo oshiroUpdate = typeof(AngryOshiro).GetMethod(nameof(AngryOshiro.Update));
            var seen = new HashSet<MethodBase>();
            int stores = 0;
            foreach (MethodDefinition method in NativeMethods(disk.Types)) {
                if (!method.HasBody) continue;
                int count = 0;
                foreach (Instruction instruction in method.Body.Instructions)
                    if (IsTimeRateStore(instruction)) count++;
                if (count == 0) continue;
                MethodBase target = runtime.ResolveMethod(method.MetadataToken.ToInt32());
                if (target == oshiroUpdate ||
                    (target.DeclaringType == typeof(Engine) && target.Name == "orig_ctor_Engine") ||
                    !seen.Add(target)) continue;
                OtherWriters.Add(new ILHook(target, ObserveCompetingWrites));
                stores += count;
            }
            if (OtherWriters.Count == 0) throw new InvalidOperationException("No native clock writers found.");
            Logger.Log(LogLevel.Info, nameof(AkronModule),
                $"Generated Oshiro clock protection: {OtherWriters.Count} native writer bodies, {stores} stores.");
            return true;
        } catch (Exception error) {
            foreach (ILHook hook in OtherWriters) hook.Dispose();
            OtherWriters.Clear();
            Lease.Clear();
            Logger.Log(LogLevel.Warn, nameof(AkronModule),
                "Generated Oshiro clock restoration disabled: " + error);
            return false;
        }
    }

    private static IEnumerable<MethodDefinition> NativeMethods(IEnumerable<TypeDefinition> types) {
        foreach (TypeDefinition type in types) {
            foreach (MethodDefinition method in type.Methods) yield return method;
            foreach (MethodDefinition method in NativeMethods(type.NestedTypes)) yield return method;
        }
    }

    internal static bool IsTimeRateStore(Instruction instruction) =>
        instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference field &&
        field.DeclaringType.FullName == typeof(Engine).FullName && field.Name == nameof(Engine.TimeRate);

    internal static void ObserveCompetingWrites(ILContext il) {
        var cursor = new ILCursor(il);
        int count = 0;
        while (cursor.TryGotoNext(MoveType.Before, IsTimeRateStore)) {
            cursor.MoveAfterLabels();
            cursor.EmitDelegate<Func<float, float>>(ObserveCompetingWrite);
            cursor.Index++;
            count++;
        }
        if (count == 0) throw new InvalidOperationException("Native clock writer lost its store site.");
    }

    // Akron's explicit timescale takes over the whole generated interval. Preserve the
    // underlying clock, not the transient Oshiro rate, for its eventual release.
    internal static float TakeTimescaleBaseline(float current) =>
        Lease.Release(Engine.Instance == null ? null : Engine.Scene, current, out float prior) ? prior : current;

    internal static bool ReleaseTimescaleUnderGenerated(float current, float oldBaseline, float newBaseline) =>
        Lease.RebaseUnderlying(Engine.Instance == null ? null : Engine.Scene, current, oldBaseline, newBaseline);

    internal static void InvalidateOwnership() => Lease.Clear();

    internal static float ObserveCompetingWrite(float value) {
        Lease.Clear();
        return value;
    }

    private static void Removed(On.Monocle.Entity.orig_Removed orig, Entity self, Scene scene) {
        orig(self, scene); // EntityList has removed self; the tracker is updated after this callback.
        if (Lease.Remove(self, scene, Engine.Scene, Engine.TimeRate, out float restore) && !AkronModule.OwnsCurrentTimescale)
            Engine.TimeRate = restore;
    }

    private static void SceneEnd(On.Monocle.Entity.orig_SceneEnd orig, Entity self, Scene scene) {
        // Never restore a departing scene's value over the incoming scene's clock.
        Lease.EndScene(scene);
        orig(self, scene);
    }
}
