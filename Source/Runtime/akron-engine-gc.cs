using System;
using System.Threading;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.Akron;

// Celeste forces a full blocking garbage collection on the game thread in three
// places. Only one of them lands while the player is playing:
//
//   Celeste.Level.Reload()            GC.Collect(); GC.WaitForPendingFinalizers();
//   Monocle.Engine.OnSceneTransition  GC.Collect(); GC.WaitForPendingFinalizers();
//   Celeste.Level._GCCollect          GC.Collect();
//
// Level.Reload runs on every death - PlayerDeadBody.End passes it to
// DoScreenWipe as the death action - and on every room reload. Measured on the
// Linux test box, each one stops every thread in the process for 222-265 ms, six
// of them per 26 seconds of play. Dying and retrying is the core loop of a
// practice mod, so that is a quarter-second freeze on the most repeated action
// the game has. This is the one Akron takes over.
//
// The other two are left exactly as they are, and that is a decision, not an
// omission:
//
//  * Engine.OnSceneTransition only runs between scenes, behind a loading screen,
//    which is already a moment the player tolerates. Leaving it also means
//    changing chapter still reclaims everything Celeste intended to reclaim
//    there, with no help from Akron.
//  * Level._GCCollect, on room transitions, was rewritten by Everest to queue
//    GC.Collect(1, Forced, blocking: false) on a background task. Its remaining
//    blocking branch runs only when the player has explicitly set Everest's own
//    MultithreadedGC option to false, and overriding a choice somebody made in
//    another mod's menu is worse than the pause it would save.
//
// What replaces the Level.Reload collection is a debt, not a deletion. The two
// calls date from XNA, where forcing a collection after unloading a level was
// how you got finalizer-backed GPU resources released before the next level
// allocated more, and Akron cannot audit what thirty installed mods rely on
// there. So the collection still happens - the same blocking collection, with
// the same wait on finalizers - it just happens the next time the player loads a
// StartPos, which is a pause they asked for. Deaths in between coalesce into one
// debt, so a player who dies forty times pays for one collection, not forty.
internal static class AkronEngineGarbageCollection {
    private static int collectionOwed;
    private static long deferredCollections;
    private static long paidCollections;

    // Exposed so status output can tell a working guard from one that silently
    // failed to install, which otherwise look identical until a spike shows up.
    internal static long DeferredCollections => Interlocked.Read(ref deferredCollections);
    internal static long PaidCollections => Interlocked.Read(ref paidCollections);
    internal static bool CollectionOwed => Volatile.Read(ref collectionOwed) != 0;

    internal static void Load() {
        IL.Celeste.Level.Reload += DeferForcedCollection;
    }

    internal static void Unload() {
        IL.Celeste.Level.Reload -= DeferForcedCollection;
        // Every deferral is a promise that the collection still happens. Unload
        // is the last chance to keep it, and it never runs during play, so pay
        // the debt here rather than throwing it away.
        CollectDeferred();
    }

    // Level.Reload contains exactly one
    //     call void System.GC::Collect()
    //     call void System.GC::WaitForPendingFinalizers()
    // pair. The pair is left in the method and a guard is inserted in front of it
    // rather than replacing it. That matters for coexistence: CelesteTAS patches
    // the same method by matching the same two instructions
    // (TAS.Gameplay.Optimization.FastForwardOptimization.SkipGC) and branching
    // over them while fast-forwarding. Leaving the pair in place means whichever
    // manipulator runs second still finds its pattern, so both guards end up in
    // the method and neither breaks the other.
    //
    // Whichever guard is emitted first also answers first. If CelesteTAS was
    // registered first and is fast-forwarding, its branch skips Akron's guard as
    // well, so no collection runs and Akron records no debt for one it never
    // skipped. That is the right answer for both mods.
    internal static void DeferForcedCollection(ILContext context) {
        ILCursor cursor = new ILCursor(context);
        if (!cursor.TryGotoNext(
                MoveType.AfterLabel,
                instruction => instruction.MatchCall(typeof(GC), nameof(GC.Collect)),
                instruction => instruction.MatchCall(typeof(GC), nameof(GC.WaitForPendingFinalizers)))) {
            // Not a fallback path: this means Celeste or Everest changed the
            // method and the setting silently stopped doing anything, which is
            // exactly what a bug report needs to say.
            AkronLog.Warn(nameof(AkronEngineGarbageCollection),
                "Could not install the deferred-collection guard in " + context.Method.FullName +
                "; Celeste's forced blocking collection there is unchanged.");
            return;
        }

        ILLabel afterCollection = cursor.DefineLabel();
        cursor.EmitDelegate<Func<bool>>(TryDeferForcedCollection);
        cursor.Emit(OpCodes.Brtrue, afterCollection);
        // Step over the two calls the guard skips and land the label on whatever
        // follows them.
        cursor.Index += 2;
        cursor.MarkLabel(afterCollection);
    }

    // Returns true when the vanilla blocking pair should be skipped. Nothing is
    // collected here on purpose: this runs on the game thread during a death, and
    // even a background gen2 costs suspension time in a frame the player sees.
    internal static bool TryDeferForcedCollection() {
        // No module means no settings to read, and the safe answer is Celeste's
        // own behaviour rather than an override nobody asked for.
        return TryDeferForcedCollection(AkronModule.Instance == null ? null : AkronModule.Settings);
    }

    internal static bool TryDeferForcedCollection(AkronModuleSettings settings) {
        if (!ShouldDeferForcedCollection(settings)) {
            return false;
        }

        Interlocked.Exchange(ref collectionOwed, 1);
        Interlocked.Increment(ref deferredCollections);
        return true;
    }

    internal static bool ShouldDeferForcedCollection(AkronModuleSettings settings) {
        return settings?.DeferEngineGarbageCollection == true;
    }

    // Called at the end of a StartPos load, which is the beat the player already
    // waits through and the point where the most garbage exists: the level graph
    // that was live a moment ago has just been replaced wholesale.
    //
    // This runs exactly what Level.Reload would have run. Whatever Celeste or an
    // installed mod depends on that collection for - FNA graphics handles
    // released from a finalizer, audio buffers, anything else with a finalizer
    // holding a native resource - still gets it, at a moment the player is
    // already paused rather than mid-retry.
    //
    // Returns whether a collection was owed, so a caller and a test can tell the
    // difference between "collected" and "nothing to do".
    internal static bool CollectDeferred() {
        if (Interlocked.Exchange(ref collectionOwed, 0) == 0) {
            return false;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        Interlocked.Increment(ref paidCollections);
        return true;
    }
}
