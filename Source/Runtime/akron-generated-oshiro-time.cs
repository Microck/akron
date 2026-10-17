using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;
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

    internal void EndScene(object endedScene) { if (scene == endedScene) Clear(); }
    internal void Clear() { contributors.Clear(); scene = null; before = last = 0f; }
}

internal static class AkronGeneratedOshiroTime {
    private const string GeneratedType = "ExtendedVariants.Entities.AutoDestroyingAngryOshiro";
    private static readonly AkronGeneratedTimeRateLease Lease = new();
    private static bool loaded;

    internal static void Load() {
        if (loaded) return;
        IL.Celeste.AngryOshiro.Update += ObserveTimeRateWrites;
        On.Monocle.Entity.Removed += Removed;
        On.Monocle.Entity.SceneEnd += SceneEnd;
        loaded = true;
    }

    internal static void Unload() {
        if (!loaded) return;
        IL.Celeste.AngryOshiro.Update -= ObserveTimeRateWrites;
        On.Monocle.Entity.Removed -= Removed;
        On.Monocle.Entity.SceneEnd -= SceneEnd;
        loaded = false;
        if (Lease.Release(Engine.Scene, Engine.TimeRate, out float restore) && !AkronModule.OwnsCurrentTimescale)
            Engine.TimeRate = restore;
    }

    private static void ObserveTimeRateWrites(ILContext il) {
        var cursor = new ILCursor(il);
        while (cursor.TryGotoNext(MoveType.Before, instruction => instruction.MatchStsfld<Engine>(nameof(Engine.TimeRate)))) {
            // Keep the native store and its value intact; observe even identical-value writes.
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<float, AngryOshiro, float>>(ObserveWrite);
            cursor.Index++; // Pass the original stsfld before searching again.
        }
    }

    private static float ObserveWrite(float value, AngryOshiro owner) {
        Type type = owner.GetType();
        bool generated = type.FullName == GeneratedType && type.Assembly.GetName().Name == "ExtendedVariantMode";
        Lease.Observe(owner, owner.Scene, generated, Engine.TimeRate, value);
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
