using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Celeste;
using FMOD.Studio;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

// Snapshot work is by far the largest allocator Akron has, on both of its
// background threads. The restart-copy worker walks a whole room graph and
// writes tens to hundreds of megabytes of JSON; the prewarm worker parses the
// same amount back in and keeps every byte of it. Every byte either one produces
// is retained while it works, so the gen0 collections they cause have a large
// surviving set, are expensive, and stop the game thread whichever thread
// allocated.
//
// Neither worker runs at all while the player is in control. They are not slowed
// down; they are stopped, and they resume the moment the player pauses, leaves
// the level, waits for input after a StartPos load, or exits the game. Measured
// on the Linux test box, an earlier version of this that allowed a 16 MiB/s
// trickle during play still left 12.8 frames per 1000 over 33 ms and stretched
// the degraded window from 28 s to 94 s, so the total number of dropped frames
// went up rather than down. A rate that keeps the player in a degraded state for
// longer is a worse trade than a stop, because the thing being protected is
// gameplay, not throughput.
//
// The cost is that a slot stays non-restart-safe, and the map's other slots stay
// unwarmed, until the player next stops playing. Four things bound it: the pause
// menu opens the gate, a StartPos load holds it open for as long as the load
// freezes the game thread, a Load that needs a restart copy finishes it on the
// spot, and Shutdown forces the gate open and drains what it can before the
// process exits.
//
// Stopping the worker cannot make a restart copy fail. A job only reaches the
// worker once it holds its own retained leases on the saved clone and the fresh
// baseline, both immutable; the room-change and room-reload failure paths act
// only on jobs still waiting for a baseline, which this never delays. The only
// thing a longer run can change is that a second Set on the same slot
// supersedes the first, which is the intended outcome of setting twice.
internal static class AkronSnapshotPacing {
    // Sleep in slices rather than on a handle so that the gate opening, a
    // cancellation, and shutdown are all picked up promptly without another
    // signalling object to keep in sync with the flags below.
    private const int SleepSliceMilliseconds = 25;

    internal const string CancelledMessage = "Celeste closed before its restart copy finished";
    internal const string AbandonedMessage = "the work was abandoned while it was parked";

    private static volatile bool gameplayActive;
    private static volatile bool forcedOpen;
    private static volatile bool cancelled;

    [ThreadStatic]
    private static bool inPacedWork;
    [ThreadStatic]
    private static long parkedTicks;
    [ThreadStatic]
    private static Func<bool> abandoned;

    // Set from the game thread once per engine update. Volatile rather than
    // locked: the worker only needs to see the change soon, not exactly.
    internal static bool GameplayActive {
        get => gameplayActive;
        set => gameplayActive = value;
    }

    // Forces the gate open regardless of what the scene is doing. Shutdown sets
    // it so a worker mid-sleep stops waiting, and a Load that needs an
    // outstanding copy sets it for as long as it waits: both are moments where
    // finishing the snapshot matters more than the frame it lands in.
    internal static bool ForcedOpen {
        get => forcedOpen;
        set => forcedOpen = value;
    }

    // Aborts the job in flight at its next pace point. Set only when the process
    // is going away and the queue could not be drained in time, so that quitting
    // stays bounded instead of holding a closed window open for the whole queue.
    internal static bool Cancelled {
        get => cancelled;
        set => cancelled = value;
    }

    // Time this job spent parked rather than working. Reported alongside the
    // wall-clock figure so "the copy took 52 seconds" cannot be read as 52
    // seconds of work when it was 50 seconds of waiting for the player to stop.
    internal static TimeSpan ParkedTime => TimeSpan.FromTicks(
        (long) (parkedTicks * (10_000_000d / Stopwatch.Frequency)));

    // Ambient for the duration of one job rather than a parameter threaded
    // through the graph walk, the snapshot writer and the snapshot reader, none
    // of which has any other reason to know about scheduling. This mirrors the
    // captured render-target scope the reconstruction graph already uses.
    // Thread-static, so the game thread's own captures and reads are never paced.
    //
    // isAbandoned is optional and exists for speculative work. A parked prewarm
    // read is holding a snapshot file open and a half-built document in memory
    // for as long as the player keeps playing, so it has to let go of both the
    // moment its queue is replaced rather than waiting for the next gate opening
    // to notice. A restart copy passes null: nothing supersedes a job that is
    // already running.
    internal static void BeginPacedWork(Func<bool> isAbandoned = null) {
        inPacedWork = true;
        parkedTicks = 0;
        abandoned = isAbandoned;
    }

    internal static void EndPacedWork() {
        inPacedWork = false;
        abandoned = null;
    }

    // Called at every point in the snapshot pipeline where it is safe to stop:
    // once per object in the fresh-room index pass, once per document node in the
    // capture walk, once per buffer flush in the writer, and once per buffer fill
    // in the reader. All four are a few kilobytes to a few hundred kilobytes
    // apart, which is what bounds how much a worker can still allocate after the
    // player takes control again. None of them holds a lock.
    internal static void Pace() {
        if (!inPacedWork) {
            return;
        }
        if (cancelled) {
            throw new OperationCanceledException(CancelledMessage);
        }
        if (!ShouldSuspend()) {
            return;
        }

        // Only the suspending path reads the clock. Pace runs once per document
        // node, which is millions of calls on a large map, and a timestamp on
        // every one of them would be a measurable tax for nothing.
        long suspendedFrom = Stopwatch.GetTimestamp();
        try {
            do {
                Thread.Sleep(SleepSliceMilliseconds);
                if (abandoned != null && abandoned()) {
                    throw new OperationCanceledException(AbandonedMessage);
                }
            } while (!cancelled && ShouldSuspend());
        } finally {
            parkedTicks += Stopwatch.GetTimestamp() - suspendedFrom;
        }
        if (cancelled) {
            throw new OperationCanceledException(CancelledMessage);
        }
    }

    // Exposed so a test can drive the decision without a worker or a clock.
    internal static bool ShouldSuspend() {
        return gameplayActive && !forcedOpen;
    }
}

public static partial class AkronSaveLoadService {
    private const int MaxFreshRoomEntityListDrainPasses = 64;

    private readonly struct DetachedScreenWipes {
        public ScreenWipe LevelWipe { get; }
        public List<Renderer> Renderers { get; }
        public Stack<(ScreenWipe Wipe, int Index)> RendererWipes { get; }

        public DetachedScreenWipes(
            ScreenWipe levelWipe,
            List<Renderer> renderers,
            Stack<(ScreenWipe Wipe, int Index)> rendererWipes
        ) {
            LevelWipe = levelWipe;
            Renderers = renderers;
            RendererWipes = rendererWipes;
        }
    }

    private static readonly PropertyInfo ComponentListLockModeProperty = typeof(ComponentList).GetProperty(
        "LockMode",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(ComponentList).FullName, "LockMode");
    private static readonly FieldInfo EntityListEntitiesField = typeof(EntityList).GetField(
        "entities",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(EntityList).FullName, "entities");
    private static readonly FieldInfo EntityListToAddField = typeof(EntityList).GetField(
        "toAdd",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(EntityList).FullName, "toAdd");
    private static readonly FieldInfo ComponentListComponentsField = typeof(ComponentList).GetField(
        "components",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(ComponentList).FullName, "components");
    private static readonly FieldInfo EntityComponentsField = typeof(Entity).GetField(
        "<Components>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(Entity).FullName, "<Components>k__BackingField");
    private static readonly FieldInfo LevelWipeField = typeof(Level).GetField(
        nameof(Level.Wipe),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    ) ?? throw new MissingMemberException(typeof(Level).FullName, nameof(Level.Wipe));
    private static readonly Dictionary<string, AkronSaveLoadSlotOwner> RuntimeSlots = new Dictionary<string, AkronSaveLoadSlotOwner>(StringComparer.Ordinal);
    private static readonly List<AkronRegisteredSaveLoadAction> RegisteredActions = new List<AkronRegisteredSaveLoadAction>();
    private static readonly List<Func<Type, bool>> ReturnSameObjectPredicates = new List<Func<Type, bool>>();
    private static readonly List<Func<object, object>> CustomCloneProcessors = new List<Func<object, object>>();

    // How much memory the warm StartPos clones are allowed to hold between them.
    //
    // The slot count is the wrong unit for this and always was. Measured on the test
    // machine, one warm slot costs 13.7 MB on vanilla Forsaken City, 15.5 MB on Spring
    // Collab 2020's Ancient Engine and 77 MB on the same pack's Heart of the Storm - a
    // five-fold spread inside one map pack, tracking each map's saved-state size rather
    // than anything about the slot. At 77 MB a slot the machine ran out of memory at 36
    // slots and 3.85 GiB of process, so the same fifty slots that cost 874 MB on one
    // map of a pack takes the whole game down on another. A count cannot express that;
    // bytes can.
    //
    // 1 GiB is the floor, so the count ceiling still binds first everywhere it already fit:
    // fifty warm slots measure 685 MB on vanilla and 874 MB on Ancient Engine, so
    // neither map reaches this and nothing about them changes. Heart of the Storm stops
    // adding warm clones at about thirteen instead of running the process out of memory
    // at thirty-six. The worst case this leaves is roughly 1 GB of Celeste and mods,
    // plus this 1 GiB, plus the read-ahead cache's own separate budget - measured at
    // 1.2 GiB when completely full - which is a little over 3 GiB of process.
    //
    // Warm-all raises that floor only as far as the managed-memory ceiling for the
    // current machine allows. The ceiling reserves 40% of memory for Celeste's native
    // allocations, other mods and the operating system. Unlike a larger fixed constant,
    // that lets an 8 GB machine retain a measured 50-slot map without making the same
    // promise on a 4 GB machine that cannot pay for it.
    //
    // This bounds the warm clones only. The prewarmed documents are a different
    // population under MaxPrewarmedSnapshotBytes, and prewarm already skips any slot
    // that will restore from memory, so a slot is in one population or the other.
    internal const long MaxWarmStartPosBytes = 1024L * 1024L * 1024L;
    // A capture needs room before its allocation can be measured. Reserve at least
    // 128 MiB, which clears the largest measured 77 MiB clone with room for variance,
    // then raise the projection to the largest warm clone this map still holds.
    internal const long MinWarmStartPosCaptureReserveBytes = 128L * 1024L * 1024L;

    private readonly struct WarmStartPosCost {
        internal WarmStartPosCost(long bytes, long useStamp) {
            Bytes = bytes;
            UseStamp = useStamp;
        }

        internal long Bytes { get; }

        // Lower is colder. Set when the slot is captured and again whenever it serves a
        // load from memory, so the slot evicted first is the one the player has gone
        // longest without using.
        internal long UseStamp { get; }
    }

    private static readonly Dictionary<string, WarmStartPosCost> WarmStartPosCosts =
        new Dictionary<string, WarmStartPosCost>(StringComparer.Ordinal);
    private static long retainedFreshBaselineBytes;
    private static long nextWarmStartPosUseStamp;
    private static HashSet<string> protectedWarmStartPosSlots;
    private static long? activeWarmStartPosBudgetBytes;
    private static long retainedWarmStartPosBudgetBytes = MaxWarmStartPosBytes;

    // The batch protects every slot it promises to warm. If the measured population
    // cannot fit, the next capture is refused instead of evicting an earlier slot and
    // claiming success with a partly warm map.
    internal sealed class WarmStartPosBatch : IDisposable {
        private readonly HashSet<string> previousProtection;
        private readonly long? previousBudgetBytes;
        private bool disposed;
        private bool committed;

        internal WarmStartPosBatch(
            HashSet<string> previousProtection,
            long? previousBudgetBytes
        ) {
            this.previousProtection = previousProtection;
            this.previousBudgetBytes = previousBudgetBytes;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            if (committed && activeWarmStartPosBudgetBytes.HasValue) {
                retainedWarmStartPosBudgetBytes = activeWarmStartPosBudgetBytes.Value;
            }
            protectedWarmStartPosSlots = previousProtection;
            activeWarmStartPosBudgetBytes = previousBudgetBytes;
            if (!committed) {
                // A failed batch can leave successful earlier reconstructions above the
                // ordinary budget. Reconcile after removing this batch's protection.
                TrimWarmStartPosSlots(out _);
            }
        }

        internal void Commit() {
            committed = true;
        }
    }

    internal static WarmStartPosBatch BeginWarmStartPosBatch(IEnumerable<string> slotNames) {
        HashSet<string> previousProtection = protectedWarmStartPosSlots;
        long? previousBudgetBytes = activeWarmStartPosBudgetBytes;
        protectedWarmStartPosSlots = new HashSet<string>(
            (slotNames ?? Enumerable.Empty<string>())
                .Where(slotName => !string.IsNullOrWhiteSpace(slotName))
                .Select(NormalizeRuntimeSlotName),
            StringComparer.Ordinal);
        activeWarmStartPosBudgetBytes = CalculateAvailableWarmStartPosBudgetBytes();
        return new WarmStartPosBatch(previousProtection, previousBudgetBytes);
    }

    internal static long WarmStartPosBudgetBytes {
        get {
            if (activeWarmStartPosBudgetBytes.HasValue) {
                return activeWarmStartPosBudgetBytes.Value;
            }

            // The first warm-all batch can raise the retained ceiling. Recheck the
            // process before later captures and trims so growth elsewhere in Celeste
            // cannot turn that old allowance into an out-of-memory risk.
            return Math.Min(
                retainedWarmStartPosBudgetBytes,
                CalculateAvailableWarmStartPosBudgetBytes());
        }
    }

    private static long CalculateAvailableWarmStartPosBudgetBytes() {
        long retainedOutsideWarmClones = Math.Max(
            GC.GetTotalMemory(forceFullCollection: false) - WarmStartPosBytes,
            0L);
        return CalculateWarmStartPosBudgetBytes(
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            retainedOutsideWarmClones);
    }

    internal static long CalculateWarmStartPosBudgetBytes(
        long totalAvailableMemoryBytes,
        long retainedOutsideWarmClones
    ) {
        if (totalAvailableMemoryBytes <= 0L || totalAvailableMemoryBytes == long.MaxValue) {
            return MaxWarmStartPosBytes;
        }

        long managedMemoryCeiling = totalAvailableMemoryBytes / 5L * 3L;
        return Math.Max(
            MaxWarmStartPosBytes,
            managedMemoryCeiling - Math.Max(retainedOutsideWarmClones, 0L));
    }

    public static string LastPersistentSnapshotError { get; private set; } = string.Empty;

    // Set only when the reconstruction graph refused a saved object. The assembly-qualified
    // name is what turns an opaque refusal into a message that can name the mod the object
    // came from.
    //
    // Its scope is one load: LoadRuntimeState clears both values on the way in, and the
    // only reader is the failure report that runs the moment LoadRuntimeState returns. A
    // capture that fails in between can leave this value behind, and that is harmless
    // because nothing reads it until the next load has already cleared it. Making the
    // error's setter clear this instead was tried and reverted: the rollback that runs
    // after a refused rebuild rewrites the error to append its own outcome, which threw
    // the refused type away and left the player back on the raw graph text - reproduced
    // in game on Midnight Aquarium before it shipped.
    public static string LastPersistentSnapshotRefusedTypeName { get; private set; } = string.Empty;

    // What the refusal above is about, which decides whether the type name explains it.
    // A refusal that the room's map changed carries a type like any other and must not be
    // attributed to the mod that ships it, so the kind travels with the name rather than
    // being inferred from it.
    //
    // internal rather than public like the two above it because the kind is Akron's own
    // vocabulary: the only reader is the load-failure report in this assembly.
    internal static AkronReconstructionRefusalKind LastPersistentSnapshotRefusedKind { get; private set; } =
        AkronReconstructionRefusalKind.SavedObject;

    // The three values always describe the same failure, so they are always written
    // together. The kind has no default here on purpose: every caller holds one, and a
    // caller that silently took SavedObject would be guessing on the player's behalf.
    private static void SetPersistentSnapshotFailure(
        string error,
        string refusedTypeName,
        AkronReconstructionRefusalKind refusedKind
    ) {
        LastPersistentSnapshotError = error;
        LastPersistentSnapshotRefusedTypeName = refusedTypeName;
        LastPersistentSnapshotRefusedKind = refusedKind;
    }

    public static string CurrentSlotName { get; private set; } = GetSlotName(1);

    // HasRuntimeState answers change whenever a warm runtime slot appears or is dropped.
    // Callers that cache a value derived from it (the StartPos list on the HUD path)
    // compare this counter instead of re-deriving the answer every frame. Every mutation
    // of RuntimeSlots must route through MarkRuntimeSlotsChanged.
    private static long runtimeStateRevision;

    internal static long RuntimeStateRevision => Interlocked.Read(ref runtimeStateRevision);

    private static void MarkRuntimeSlotsChanged() {
        Interlocked.Increment(ref runtimeStateRevision);
    }

    public static void OnLevelBegin(Level level) {
        if (level != null) {
            CurrentSlotName = GetSlotName(AkronModule.Settings.ActiveSavestateSlot);
        }
    }

    public static void ClearRuntimeState() {
        AkronStartPosReconstruction.ReleaseOwnedResources();
        AkronStartPosPersistence.ClearRuntimeFreshBaselines();
        RunClearStateActions();
        foreach (AkronSaveLoadSlotOwner runtimeSlot in RuntimeSlots.Values.Distinct()) {
            runtimeSlot.ReleaseOwnership();
        }
        // Per-slot release removes owned registrations. This final reset also
        // clears any stale generation left after a live asset reload.
        AkronVirtualAssetReloadTracker.Clear();
        RegisteredActions.Clear();
        ReturnSameObjectPredicates.Clear();
        CustomCloneProcessors.Clear();
        RuntimeSlots.Clear();
        MarkRuntimeSlotsChanged();
        WarmStartPosCosts.Clear();
        protectedWarmStartPosSlots = null;
        activeWarmStartPosBudgetBytes = null;
        retainedWarmStartPosBudgetBytes = MaxWarmStartPosBytes;
        CurrentSlotName = GetSlotName(1);
    }

    public static object RegisterSaveLoadAction(
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        return RegisterSaveLoadAction(
            null,
            saveState,
            loadState,
            clearState,
            beforeSaveState,
            beforeLoadState,
            preCloneEntities);
    }

    public static object RegisterNamedSaveLoadAction(
        string registrationName,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        if (string.IsNullOrWhiteSpace(registrationName)) {
            throw new ArgumentException("A stable save/load registration name is required.", nameof(registrationName));
        }
        return RegisterSaveLoadAction(
            registrationName,
            saveState,
            loadState,
            clearState,
            beforeSaveState,
            beforeLoadState,
            preCloneEntities);
    }

    private static object RegisterSaveLoadAction(
        string registrationName,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        AkronRegisteredSaveLoadAction action = new AkronRegisteredSaveLoadAction(
            GetRegisteredActionId(registrationName, saveState, loadState, clearState, beforeSaveState, beforeLoadState, preCloneEntities),
            saveState,
            loadState,
            clearState,
            beforeSaveState,
            beforeLoadState,
            preCloneEntities);
        AddRegisteredAction(action);
        return action;
    }

    public static object RegisterStaticTypes(Type type, params string[] memberNames) {
        AkronRegisteredSaveLoadAction action = new AkronRegisteredSaveLoadAction(
            GetStaticRegistrationId(type, memberNames),
            (savedValues, _) => SaveStaticMemberValues(savedValues, type, memberNames),
            (savedValues, _) => LoadStaticMemberValues(savedValues, type, memberNames),
            null,
            null,
            null,
            null
        );
        AddRegisteredAction(action);
        return action;
    }

    private static string GetRegisteredActionId(string registrationName, params Delegate[] callbacks) {
        string nameIdentity = registrationName == null
            ? "unnamed"
            : "named=" + registrationName.Length.ToString(CultureInfo.InvariantCulture) + ":" + registrationName;
        string baseId = "callbacks|" + nameIdentity + "|" + string.Join("|", callbacks.Select((callback, index) =>
            index.ToString(CultureInfo.InvariantCulture) + ":" + GetDelegateIdentity(callback)));
        return baseId + "|registration=0";
    }

    private static string GetAvailableRegistrationId(string baseId) {
        int registrationIndex = 0;
        string id;
        do {
            id = baseId + "|registration=" + registrationIndex.ToString(CultureInfo.InvariantCulture);
            registrationIndex++;
        } while (RegisteredActions.Any(existing => string.Equals(existing.Id, id, StringComparison.Ordinal)));
        return id;
    }

    private static string GetStaticRegistrationId(Type type, IEnumerable<string> memberNames) {
        string baseId = "static|" + GetTypeIdentity(type) + "|" +
                        string.Join(",", (memberNames ?? Array.Empty<string>()).OrderBy(name => name, StringComparer.Ordinal));
        return GetAvailableRegistrationId(baseId);
    }

    private static string GetDelegateIdentity(Delegate callback) {
        if (callback == null) {
            return "null";
        }

        MethodInfo method = callback.Method;
        return GetTypeIdentity(method.DeclaringType) + "::" + method.Name +
               "|generic=" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture) +
               "|returns=" + GetTypeIdentity(method.ReturnType) +
               "|parameters=" + string.Join(",", method.GetParameters().Select(parameter => GetTypeIdentity(parameter.ParameterType))) +
               "|target=" + (callback.Target == null ? "static" : GetTypeIdentity(callback.Target.GetType()));
    }

    private static string GetTypeIdentity(Type type) {
        if (type == null) {
            return "unknown";
        }
        string assemblyName = type.Assembly.GetName().Name ?? "unknown";
        return assemblyName + ":" + (type.FullName ?? type.Name);
    }

    private static void AddRegisteredAction(AkronRegisteredSaveLoadAction action) {
        if (RegisteredActions.Any(existing => string.Equals(existing.Id, action.Id, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                "Duplicate save/load action identity. Use RegisterNamedSaveLoadAction with a stable owner name: " + action.Id);
        }
        RegisteredActions.Add(action);
    }

    public static void Unregister(object obj) {
        if (obj is AkronRegisteredSaveLoadAction action) {
            RegisteredActions.Remove(action);
        }
    }

    public static void IgnoreSaveState(Entity entity, bool based = false) {
        if (entity.Get<AkronIgnoreSaveStateComponent>() == null) {
            entity.Add(new AkronIgnoreSaveStateComponent(based));
        }
    }

    public static void AddReturnSameObjectProcessor(Func<Type, bool> predicate) {
        ReturnSameObjectPredicates.Add(predicate);
    }

    public static void RemoveReturnSameObjectProcessor(Func<Type, bool> predicate) {
        ReturnSameObjectPredicates.Remove(predicate);
    }

    public static void AddCustomDeepCloneProcessor(Func<object, object> processor) {
        CustomCloneProcessors.Add(processor);
    }

    public static void RemoveCustomDeepCloneProcessor(Func<object, object> processor) {
        CustomCloneProcessors.Remove(processor);
    }

    public static object DeepClone(object from) {
        return AkronDeepClone.Clone(from);
    }

    private static void CaptureRegisteredActionState(
        AkronSaveLoadSlot saveSlot,
        AkronRegisteredSaveLoadAction action,
        Level level
    ) {
        Dictionary<Type, Dictionary<string, object>> savedValues =
            new Dictionary<Type, Dictionary<string, object>>();
        action.SaveState?.Invoke(savedValues, level);
        saveSlot.ActionState[action.Id] =
            (Dictionary<Type, Dictionary<string, object>>) AkronDeepClone.CloneDormant(
                savedValues,
                out List<EventInstance> capturedEventInstances);
        saveSlot.SavedLevelEventInstances ??= new List<EventInstance>();
        saveSlot.SavedLevelEventInstances.AddRange(capturedEventInstances);
    }

    public static bool ShouldReturnSameObject(Type type) {
        foreach (Func<Type, bool> predicate in ReturnSameObjectPredicates) {
            if (predicate(type)) {
                return true;
            }
        }

        return false;
    }

    public static object TryCustomClone(object sourceObject) {
        foreach (Func<object, object> processor in CustomCloneProcessors) {
            object clonedObject = processor(sourceObject);
            if (clonedObject != null) {
                return clonedObject;
            }
        }

        return null;
    }

    public static AkronSaveLoadResult Save(Level level, int slot) {
        if (level == null) {
            return AkronSaveLoadResult.Failed;
        }

        CurrentSlotName = GetSlotName(slot);

        AkronPolicyDecision policy = AkronPolicy.CanUse(AkronFeatureKind.Savestates);
        if (!policy.Allowed) {
            return AkronSaveLoadResult.Blocked;
        }

        // Numbered savestates belong to Speedrun Tool; Akron only forwards them. Akron's own
        // clone machinery below serves StartPos slots (see SaveRuntimeState), not these.
        return TryBrokerSave(slot);
    }

    public static AkronSaveLoadResult Load(Level level, int slot) {
        if (level == null) {
            return AkronSaveLoadResult.Failed;
        }

        CurrentSlotName = GetSlotName(slot);

        // A savestate rewinds gameplay. It must not rewind the list of StartPos slots
        // the player has set, which lives in Akron's module save data and session and
        // is replaced wholesale by both restore paths. Held here rather than inside
        // either path because the brokered path is the one every shipped build takes:
        // SpeedrunTool restores _Session and _SaveData itself and Akron never sees the
        // assignment. See AkronActions.RestoreStartPosCatalogAfterStateLoad.
        //
        // Unconditional rather than only on Success. A load can be refused before it
        // touches anything, but it can also fail or throw after the module state has
        // already been replaced, and telling those apart from out here means trusting
        // a result code to describe how far a third-party mod got. The cost of being
        // wrong the safe way is one catalog rebuild - about one stat per placed slot -
        // on an action that is already a whole-level restore.
        Dictionary<string, AkronPersistedStartPosMap> startPosCatalog =
            AkronModule.Instance == null ? null : AkronModule.SaveData?.StartPositionsByMap;
        try {
            return LoadCore(level, slot);
        } finally {
            AkronActions.RestoreStartPosCatalogAfterStateLoad(level, startPosCatalog);
        }
    }

    private static AkronSaveLoadResult LoadCore(Level level, int slot) {
        return TryBrokerLoad(level, slot);
    }

    public static AkronSaveLoadSlot CaptureRuntimeState(
        Level level,
        string slotName,
        bool saveTimeAndDeaths,
        bool capturePersistentResources = true,
        bool prepareForRestore = true
    ) {
        if (level == null || !CanAccessNativeState(level, out _)) {
            return null;
        }

        CurrentSlotName = string.IsNullOrWhiteSpace(slotName) ? "StartPos" : slotName;
        bool isStartPosCapture = CurrentSlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
        AkronLevelRenderState renderState = AkronLevelRenderState.Capture(level);
        List<AkronGameplayBufferSnapshot> gameplayBuffers = new List<AkronGameplayBufferSnapshot>();
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> persistentRenderTargets =
            new Dictionary<object, AkronReconstructionResourcePayload>();
        if (isStartPosCapture) {
            try {
                gameplayBuffers = AkronGameplayBufferState.Capture();
            } catch (Exception exception) {
                LastPersistentSnapshotError = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }
        int virtualAssetMarker = AkronVirtualAssetReloadTracker.Mark();
        bool retainsTrackedVirtualAssets = false;
        AkronSaveLoadSlot saveSlot = null;
        DetachedScreenWipes? entryWipes = isStartPosCapture
            ? DetachTransientScreenWipes(level)
            : null;
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }

            // Keep the exact Set-frame hook-owner identities with every clone
            // that can reach them. Persistence runs later on a worker, after a
            // mod may have changed its live hook registrations.
            IReadOnlyDictionary<object, string> hookOwnerRegistrations = isStartPosCapture
                ? AkronStartPosReconstruction.CaptureHookOwnerRegistrations()
                : null;
            using IDisposable hookOwnerScope = hookOwnerRegistrations != null
                ? AkronStartPosReconstruction.UseHookOwnerRegistrations(hookOwnerRegistrations)
                : null;

            // StartPos needs full-state semantics: the room is cloned as a whole,
            // then restored as a whole. A player-only snapshot cannot preserve
            // collected objects, entity cycles, triggers, or room-local runtime
            // state accurately enough for practice starts.
            saveSlot = BuildNativeSlot(level, CurrentSlotName, saveTimeAndDeaths, includeLevelSnapshot: true);
            saveSlot.GameplayBuffers = gameplayBuffers;
            saveSlot.PersistentRenderTargets = persistentRenderTargets;
            if (hookOwnerRegistrations != null) {
                saveSlot.HookOwnerRegistrations = hookOwnerRegistrations;
            }
            AkronIgnoreSaveStateComponent.RemoveAllFromSnapshot(saveSlot.SavedLevel);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                CaptureRegisteredActionState(saveSlot, action, level);
            }
            if (prepareForRestore) {
                PrepareSlotPreClone(saveSlot);
            }
            if (capturePersistentResources && isStartPosCapture) {
                try {
                    persistentRenderTargets = AkronVirtualRenderTargetResourceAdapter.CaptureSetFramePayloads(
                        AkronVirtualAssetReloadTracker.GetRenderTargetsSince(virtualAssetMarker));
                    saveSlot.PersistentRenderTargets = persistentRenderTargets;
                } catch (Exception exception) {
                    LastPersistentSnapshotError = exception.GetType().Name + ": " + exception.Message;
                    ReleaseDormantEventInstances(saveSlot);
                    saveSlot = null;
                    return null;
                }
            }

            saveSlot.TrackedVirtualAssetRegistrations =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(virtualAssetMarker);
            retainsTrackedVirtualAssets = true;
            return saveSlot;
        } catch {
            ReleaseDormantEventInstances(saveSlot);
            throw;
        } finally {
            if (!retainsTrackedVirtualAssets) {
                AkronVirtualAssetReloadTracker.DiscardSince(virtualAssetMarker);
            }
            AkronDeepClone.ClearSharedState();
            renderState.Restore(level);
            RestoreTransientScreenWipes(level, entryWipes);
        }
    }

    public static AkronSaveLoadResult SaveRuntimeState(Level level, string slotName, bool saveTimeAndDeaths) {
        if (level == null) {
            return AkronSaveLoadResult.Failed;
        }

        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        CurrentSlotName = normalizedSlotName;
        if (ShouldBrokerRuntimeState(normalizedSlotName)) {
            AkronSaveLoadResult brokerResult = AkronSpeedrunToolBroker.Save(normalizedSlotName);
            if (brokerResult == AkronSaveLoadResult.Success) {
                AkronStartPosPersistence.RemoveRuntimeFreshBaseline(normalizedSlotName);
                if (RuntimeSlots.Remove(normalizedSlotName, out AkronSaveLoadSlotOwner previousSlot)) {
                    previousSlot.ReleaseOwnership();
                    MarkRuntimeSlotsChanged();
                }
                AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
                return AkronSaveLoadResult.Success;
            }
            if (brokerResult != AkronSaveLoadResult.BrokerUnavailable) {
                return brokerResult;
            }
        }

        // What the clone costs is measured as the capture's own allocation on this
        // thread. The deep clone is nearly all of it, the counter is thread-local so
        // nothing else in the process can move it, and it cannot be perturbed by a
        // collection landing mid-capture the way a heap-size reading would be. It reads
        // a little high, because the transient work around the clone is counted too,
        // and high is the safe direction for a memory guard.
        long allocatedBeforeCapture = GC.GetAllocatedBytesForCurrentThread();
        AkronSaveLoadSlot saveSlot = CaptureRuntimeState(level, normalizedSlotName, saveTimeAndDeaths);
        if (saveSlot == null) {
            return AkronSaveLoadResult.Blocked;
        }
        StoreRuntimeSlot(normalizedSlotName, saveSlot);
        RecordWarmStartPosCost(normalizedSlotName, allocatedBeforeCapture);
        AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
        return AkronSaveLoadResult.Success;
    }

    private static bool IsStartPosSlotName(string slotName) {
        return slotName != null &&
               slotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
    }

    // Every path that puts a clone into RuntimeSlots has to declare what it cost, or the
    // budget stops seeing memory that is really resident. There are two of them: a Set,
    // and the re-cache that follows a load rebuilt from disk. The second one matters most
    // here, because a load is exactly how a slot the trim evicted earlier comes back
    // warm; leaving it uncounted would let the population climb past the budget one
    // evicted-then-reloaded slot at a time.
    private static void RecordWarmStartPosCost(string slotName, long allocatedBeforeCapture) {
        if (!IsStartPosSlotName(slotName)) {
            return;
        }

        long capturedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeCapture;
        WarmStartPosCosts[slotName] =
            new WarmStartPosCost(Math.Max(capturedBytes, 0L), ++nextWarmStartPosUseStamp);
    }

    // The warm StartPos clones' total cost, recomputed from RuntimeSlots rather than
    // kept in step by every remover. RuntimeSlots is mutated from eight places - a Set,
    // a clear, a park, a rollback, a chapter re-entry, a broker save, a shutdown - and a
    // running total that has to be told about each of them is a total that will drift
    // silently, which for a memory guard means either refusing slots that would have fit
    // or letting the game run out of memory anyway. Fifty entries is nothing to walk and
    // this only runs when a slot is set or when a status line is built.
    internal static long WarmStartPosBytes {
        get {
            // A fresh-room baseline can be shared by every slot in its room, so its
            // owner records the allocation once while leases only extend its lifetime.
            long total = Interlocked.Read(ref retainedFreshBaselineBytes);
            List<string> gone = null;
            foreach (KeyValuePair<string, WarmStartPosCost> pair in WarmStartPosCosts) {
                if (RuntimeSlots.ContainsKey(pair.Key)) {
                    total += pair.Value.Bytes;
                } else {
                    (gone ??= new List<string>()).Add(pair.Key);
                }
            }
            foreach (string slotName in gone ?? Enumerable.Empty<string>()) {
                WarmStartPosCosts.Remove(slotName);
            }
            return total;
        }
    }

    // Marks a slot as just used, so the eviction below reaches for it last. Called when
    // a load is served from the warm clone; a load that rebuilds from disk does not
    // count, because that slot is not holding any of this budget.
    internal static void MarkWarmStartPosUsed(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        if (WarmStartPosCosts.TryGetValue(normalizedSlotName, out WarmStartPosCost cost)) {
            WarmStartPosCosts[normalizedSlotName] =
                new WarmStartPosCost(cost.Bytes, ++nextWarmStartPosUseStamp);
        }
    }

    // Drops the coldest warm clones until the population is back inside its budget, and
    // reports what it dropped so the player can be told.
    //
    // Dropping a warm clone is not losing the slot. The clone's whole purpose is to skip
    // the snapshot read, so a slot that loses it still loads from its restart copy on
    // disk and still restores the same state - the slow path every slot takes after the
    // game is restarted. That is the degradation this trades for: a few seconds on a
    // load, instead of the process running out of memory.
    //
    // A slot is only droppable once its restart copy exists and is not still being
    // written. Until then the clone is the only copy of that state and dropping it would
    // destroy the slot. That is also what keeps the slot just set: its own copy is
    // pending at this point, so it is never the one evicted.
    internal static int TrimWarmStartPosSlots(out long droppedBytes) {
        return TrimWarmStartPosSlotsTo(WarmStartPosBudgetBytes, out droppedBytes);
    }

    // A native StartPos clone contains one map's Level graph and cannot serve any
    // other map. Once the active catalog changes, release restart-safe clones that
    // are absent from it. Their disk snapshots remain canonical and can rebuild the
    // whole map-scoped warm set when the player returns.
    internal static int DiscardRestartSafeWarmStartPosSlotsExcept(IEnumerable<string> retainedSlotNames) {
        HashSet<string> retained = new HashSet<string>(
            (retainedSlotNames ?? Enumerable.Empty<string>())
                .Where(slotName => !string.IsNullOrWhiteSpace(slotName))
                .Select(NormalizeRuntimeSlotName),
            StringComparer.Ordinal);
        string[] discarded = WarmStartPosCosts.Keys
            .Where(slotName => !retained.Contains(slotName) && CanDropWarmStartPosSlot(slotName))
            .ToArray();
        foreach (string slotName in discarded) {
            ReleaseRuntimeStateMemory(slotName);
            WarmStartPosCosts.Remove(slotName);
        }
        return discarded.Length;
    }

    private static int TrimWarmStartPosSlotsTo(long targetBytes, out long droppedBytes) {
        droppedBytes = 0;
        int dropped = 0;
        long total = WarmStartPosBytes;
        while (total > targetBytes) {
            string coldest = null;
            long coldestStamp = long.MaxValue;
            foreach (KeyValuePair<string, WarmStartPosCost> pair in WarmStartPosCosts) {
                if (pair.Value.UseStamp >= coldestStamp ||
                    !CanDropWarmStartPosSlot(pair.Key)) {
                    continue;
                }
                coldest = pair.Key;
                coldestStamp = pair.Value.UseStamp;
            }
            if (coldest == null) {
                // Everything left is still being copied to disk. Refusing to drop it is
                // the only correct answer; AkronActions declines the next Set instead.
                break;
            }

            long coldestBytes = WarmStartPosCosts[coldest].Bytes;
            AkronLog.Info(nameof(AkronSaveLoadService),
                "StartPos warm clone for " + coldest + " dropped to stay inside the " +
                (WarmStartPosBudgetBytes / (1024d * 1024d)).ToString("F0", CultureInfo.InvariantCulture) +
                " MB warm budget; it holds " +
                (coldestBytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) +
                " MB and still loads from its restart copy.");
            // Eviction releases only resources owned by this clone. Registered
            // ClearState callbacks describe live global helper-mod state, so a
            // cold StartPos clone must not fire them.
            ReleaseRuntimeStateMemory(coldest);
            WarmStartPosCosts.Remove(coldest);
            total -= coldestBytes;
            droppedBytes += coldestBytes;
            dropped++;
        }
        return dropped;
    }

    private static bool CanDropWarmStartPosSlot(string slotName) {
        return (protectedWarmStartPosSlots == null || !protectedWarmStartPosSlots.Contains(slotName)) &&
               !AkronActions.HasPendingStartPosState(slotName) &&
               AkronStartPosReconstruction.HasSnapshot(slotName);
    }

    // Test seam, called from nowhere in the mod. The budget only becomes interesting at
    // hundreds of megabytes, and the only way to get there honestly is to deep-clone
    // tens of real Celeste rooms, which needs a running game. This installs a warm slot
    // carrying a stated cost through the same dictionary a capture writes and the same
    // RuntimeSlots entry a capture stores, so the reconcile, the eviction order and the
    // blocked case all run against the real code rather than a stand-in for it. Each
    // call takes the next use stamp, so the call order is the least-recently-used order.
    internal static void AddWarmStartPosSlotForTests(string slotName, string mapSid, long bytes) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        StoreRuntimeSlot(
            normalizedSlotName,
            new AkronSaveLoadSlot(normalizedSlotName, "test-room", mapSid, saveTimeAndDeaths: false));
        WarmStartPosCosts[normalizedSlotName] = new WarmStartPosCost(bytes, ++nextWarmStartPosUseStamp);
    }

    // Makes room for the next clone before CaptureRuntimeState allocates it. The exact
    // cost is only known afterward, so the largest resident clone is the best local
    // projection, with a conservative floor for the first capture. A pending slot has
    // no disk copy and cannot be spent; if those slots alone occupy the target, refuse
    // without dropping anything.
    internal static bool PrepareWarmStartPosCapture(
        string mapSid,
        out int droppedSlots,
        out long droppedBytes
    ) {
        long total = WarmStartPosBytes;
        long projectedCaptureBytes = MinWarmStartPosCaptureReserveBytes;
        foreach (KeyValuePair<string, WarmStartPosCost> pair in WarmStartPosCosts) {
            if (RuntimeSlots.TryGetValue(pair.Key, out AkronSaveLoadSlotOwner owner) &&
                string.Equals(owner.Slot.MapSid, mapSid, StringComparison.Ordinal)) {
                projectedCaptureBytes = Math.Max(projectedCaptureBytes, pair.Value.Bytes);
            }
        }
        long warmBudgetBytes = WarmStartPosBudgetBytes;
        if (projectedCaptureBytes > warmBudgetBytes) {
            droppedSlots = 0;
            droppedBytes = 0;
            return false;
        }

        long targetBytes = warmBudgetBytes - projectedCaptureBytes;
        if (total <= targetBytes) {
            droppedSlots = 0;
            droppedBytes = 0;
            return true;
        }

        long bytesThatCannotBeDropped = total;
        foreach (KeyValuePair<string, WarmStartPosCost> pair in WarmStartPosCosts) {
            if (CanDropWarmStartPosSlot(pair.Key)) {
                bytesThatCannotBeDropped -= pair.Value.Bytes;
            }
        }
        if (bytesThatCannotBeDropped > targetBytes) {
            droppedSlots = 0;
            droppedBytes = 0;
            return false;
        }

        droppedSlots = TrimWarmStartPosSlotsTo(targetBytes, out droppedBytes);
        return true;
    }

    internal static bool PrepareFreshRuntimeBaselineCapture(
        string mapSid,
        bool retainedBaselineAlreadyExists,
        out int droppedSlots,
        out long droppedBytes
    ) {
        if (!retainedBaselineAlreadyExists) {
            return PrepareWarmStartPosCapture(mapSid, out droppedSlots, out droppedBytes);
        }

        // The current room still has to be captured: arbitrary mod session and save
        // data can change its fresh graph. The new owner is released immediately after
        // that capture proves it shares the retained baseline key, so this allocation
        // is temporary and must not evict one of warm-all's protected native slots.
        droppedSlots = 0;
        droppedBytes = 0;
        return true;
    }

    private static void StoreRuntimeSlot(string slotName, AkronSaveLoadSlot saveSlot) {
        AkronStartPosPersistence.RemoveRuntimeFreshBaseline(slotName);
        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(saveSlot, ReleaseRuntimeSlotResources);
        if (RuntimeSlots.Remove(slotName, out AkronSaveLoadSlotOwner previousSlot)) {
            previousSlot.ReleaseOwnership();
        }
        RuntimeSlots[slotName] = owner;
        MarkRuntimeSlotsChanged();
    }

    internal static AkronSaveLoadSlotLease CaptureFreshRuntimeState(
        Level level,
        string slotName,
        string runtimeStateSlotName = null
    ) {
        if (level == null) {
            return null;
        }
        bool retainedBaselineAlreadyExists =
            AkronStartPosPersistence.HasSharedRuntimeFreshBaseline(runtimeStateSlotName, level);
        if (!PrepareFreshRuntimeBaselineCapture(
                level.Session.Area.GetSID(),
                retainedBaselineAlreadyExists,
                out _,
                out _)) {
            LastPersistentSnapshotError =
                "fresh-room baseline could not be captured inside the warm memory limit";
            return null;
        }

        long allocatedBeforeCapture = GC.GetAllocatedBytesForCurrentThread();
        CurrentSlotName = string.IsNullOrWhiteSpace(slotName) ? "fresh baseline" : slotName;
        AkronLevelRenderState renderState = AkronLevelRenderState.Capture(level);
        int virtualAssetMarker = AkronVirtualAssetReloadTracker.Mark();
        DetachedScreenWipes entryWipes = DetachTransientScreenWipes(level);
        AkronSaveLoadSlot saveSlot = null;
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }
            saveSlot = BuildPersistentBaselineSlot(level, CurrentSlotName);
            AkronIgnoreSaveStateComponent.RemoveAllFromSnapshot(saveSlot.SavedLevel);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                CaptureRegisteredActionState(saveSlot, action, level);
            }
            // Warm-all restores this retained graph before every additional cold slot.
            // Prepare it like a normal native runtime slot so the first restore is
            // valid and later restores can refresh the consumed pre-clone state.
            PrepareSlotPreClone(saveSlot);
        } catch {
            ReleaseDormantEventInstances(saveSlot);
            throw;
        } finally {
            AkronVirtualAssetReloadTracker.DiscardSince(virtualAssetMarker);
            AkronDeepClone.ClearSharedState();
            renderState.Restore(level);
            RestoreTransientScreenWipes(level, entryWipes);
        }

        long capturedBytes = Math.Max(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeCapture,
            0L);
        Interlocked.Add(ref retainedFreshBaselineBytes, capturedBytes);
        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(
            saveSlot,
            slot => ReleaseFreshRuntimeBaseline(slot, capturedBytes));
        AkronSaveLoadSlotLease lease = owner.Retain();
        owner.ReleaseOwnership();
        lease = AkronStartPosPersistence.DeduplicateRuntimeFreshBaseline(
            runtimeStateSlotName,
            lease);
        TrimWarmStartPosSlots(out _);
        if (WarmStartPosBytes > WarmStartPosBudgetBytes) {
            lease.Dispose();
            LastPersistentSnapshotError =
                "fresh-room baseline exceeded the warm memory limit after capture";
            return null;
        }
        return lease;
    }

    private static DetachedScreenWipes DetachTransientScreenWipes(Level level) {
        ScreenWipe levelWipe = (ScreenWipe) LevelWipeField.GetValue(level);
        RendererList rendererList = AkronLevelRenderState.RendererListField?.GetValue(level) as RendererList;
        List<Renderer> renderers = rendererList?.Renderers;
        Stack<(ScreenWipe Wipe, int Index)> rendererWipes = new Stack<(ScreenWipe, int)>();
        if (renderers != null) {
            for (int index = renderers.Count - 1; index >= 0; index--) {
                if (renderers[index] is ScreenWipe wipe) {
                    rendererWipes.Push((wipe, index));
                    renderers.RemoveAt(index);
                }
            }
        }
        // Wipes are process-owned transitions, not stable room state. Level.Wipe
        // can clear before its renderer leaves the list, so exclude both forms.
        LevelWipeField.SetValue(level, null);
        return new DetachedScreenWipes(levelWipe, renderers, rendererWipes);
    }

    private static void RestoreTransientScreenWipes(Level level, DetachedScreenWipes? boundary) {
        if (!boundary.HasValue) {
            return;
        }
        DetachedScreenWipes entryWipes = boundary.Value;
        LevelWipeField.SetValue(level, entryWipes.LevelWipe);
        if (entryWipes.Renderers == null) {
            return;
        }
        foreach ((ScreenWipe wipe, int index) in entryWipes.RendererWipes) {
            if (!entryWipes.Renderers.Contains(wipe)) {
                entryWipes.Renderers.Insert(
                    Math.Min(index, entryWipes.Renderers.Count),
                    wipe);
            }
        }
    }

    internal static IReadOnlyList<string> GetRegisteredActionIdsForPersistence() {
        return RegisteredActions
            .Select(action => action.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    internal static AkronSaveLoadResult PersistRuntimeStateSnapshot(
        AkronSaveLoadSlot saveSlot,
        AkronSaveLoadSlot freshSlot,
        IReadOnlyList<string> registeredActionIds,
        string snapshotDirectory,
        out string error
    ) {
        error = string.Empty;
        if (saveSlot?.SavedLevel == null || freshSlot?.SavedLevel == null) {
            error = "saved or fresh StartPos graph is unavailable";
            return AkronSaveLoadResult.NoState;
        }

        AkronPersistentRuntimeState savedRuntimeState = AkronPersistentRuntimeState.CaptureSaved(saveSlot);
        AkronPersistentRuntimeState freshRuntimeState = AkronPersistentRuntimeState.CaptureSaved(freshSlot);
        using IDisposable renderTargetScope = AkronStartPosReconstruction.UseCapturedRenderTargets(
            saveSlot.PersistentRenderTargets);
        using IDisposable hookOwnerScope = AkronStartPosReconstruction.UseHookOwnerRegistrations(
            saveSlot.HookOwnerRegistrations);
        AkronReconstructionCapture capture = AkronStartPosReconstruction.Capture(savedRuntimeState, freshRuntimeState);
        if (!capture.Success) {
            error = capture.Error;
            return AkronSaveLoadResult.Failed;
        }

        AkronReconstructionCapture actionCapture = AkronStartPosReconstruction.CaptureActionState(
            saveSlot.ActionState,
            freshSlot.ActionState);
        if (!actionCapture.Success) {
            error = "registered action state " + actionCapture.Error;
            return AkronSaveLoadResult.Failed;
        }

        capture.Document.ActionStateDocument = actionCapture.Document;
        capture.Document.RegisteredActionIds = new List<string>(registeredActionIds ?? Array.Empty<string>());
        capture.Document.GameplayBuffers = saveSlot.GameplayBuffers;
        capture.Document.BerryProgress = saveSlot.BerryProgress;
        if (!AkronStartPosReconstruction.SaveSnapshot(
                saveSlot.SlotName,
                saveSlot.MapSid,
                saveSlot.LevelName,
                saveSlot.FileSlot,
                capture.Document,
                out error,
                snapshotDirectory)) {
            return AkronSaveLoadResult.Failed;
        }
        return AkronSaveLoadResult.Success;
    }

    public static AkronSaveLoadResult RestoreRuntimeState(
        Level level,
        AkronSaveLoadSlot saveSlot,
        bool allowDeadPlayer = false
    ) {
        return RestoreRuntimeState(
            level,
            saveSlot,
            allowDeadPlayer,
            saveSlot?.SlotName);
    }

    private static AkronSaveLoadResult RestoreRuntimeState(
        Level level,
        AkronSaveLoadSlot saveSlot,
        bool allowDeadPlayer,
        string freshBaselineStateSlotName
    ) {
        if (level == null || saveSlot == null) {
            return AkronSaveLoadResult.NoState;
        }

        CurrentSlotName = saveSlot.SlotName;
        if (!CanAccessNativeState(level, out _, allowDeadPlayer)) {
            return AkronSaveLoadResult.Blocked;
        }
        if (!MatchesCurrentNativeSession(level, saveSlot)) {
            return AkronSaveLoadResult.SessionMismatch;
        }

        IReadOnlyDictionary<object, string> hookOwnerRegistrations = saveSlot.HookOwnerRegistrations;
        if (hookOwnerRegistrations?.Count > 0) {
            IReadOnlyDictionary<object, string> currentHookOwnerRegistrations =
                AkronStartPosReconstruction.CaptureHookOwnerRegistrations();
            if (!AkronStartPosReconstruction.AreHookOwnerRegistrationsCurrent(
                    hookOwnerRegistrations,
                    currentHookOwnerRegistrations)) {
                // A helper hot reload replaced or changed a process singleton.
                // Reject this warm cache before it mutates the level so the caller
                // can discard it and rebuild from the restart-safe snapshot.
                return AkronSaveLoadResult.SessionMismatch;
            }
            hookOwnerRegistrations = currentHookOwnerRegistrations;
        }

        bool suppressLagPauserForStartPos = saveSlot.SlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
        if (suppressLagPauserForStartPos) {
            AkronModule.SuppressLagPauserForNativeStartPosRestore();
        }
        AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
        AkronIgnoreSaveStateComponent.RemoveAll(level);
        // The snapshot this restore writes back was captured without the room's
        // playback ghosts, so the live ones have to step aside for it and step
        // back afterwards. The room is not reloaded on this path, so the ghosts
        // that come back are the same ones that were here a moment ago.
        List<Entity> detachedGhosts = AkronSnapshotExclusion.DetachFromLevel(level);
        try {
            using IDisposable hookOwnerScope = AkronStartPosReconstruction.UseHookOwnerRegistrations(
                hookOwnerRegistrations);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeLoadState?.Invoke(level);
            }

            if (!RestoreNativeSlot(
                    level,
                    saveSlot,
                    restoreAkronModuleState: false,
                    restoreGlobalSaveData: false)) {
                return AkronSaveLoadResult.SessionMismatch;
            }

            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                if (saveSlot.ActionState.TryGetValue(action.Id, out Dictionary<Type, Dictionary<string, object>> savedValues)) {
                    action.LoadState?.Invoke((Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues), level);
                }
            }
            if (saveSlot.GameplayBuffers.Count > 0) {
                AkronGameplayBufferState.RestoreBestEffort(saveSlot.GameplayBuffers);
            }
            PrepareRuntimeSlotPreClone(saveSlot);
            AkronStartPosPersistence.UseRuntimeFreshBaseline(freshBaselineStateSlotName);
            // Berry progress is persistent save data. Apply it only after the
            // remaining restore work can no longer report a normal failure.
            if (saveSlot.BerryProgress != null &&
                !saveSlot.BerryProgress.TryRestore(level, out string berryRestoreError)) {
                LastPersistentSnapshotError = berryRestoreError;
                return AkronSaveLoadResult.Failed;
            }
            AkronGameplayBufferState.ArmLevelPresentation(level, saveSlot.GameplayBuffers);
            // This load was served by the warm clone, so the slot has earned its place
            // in the warm budget over whichever slot has gone longest without one.
            MarkWarmStartPosUsed(saveSlot.SlotName);
        } finally {
            AkronDeepClone.ClearSharedState();
            AkronSnapshotExclusion.ReattachToLevel(level, detachedGhosts);
            AkronIgnoreSaveStateComponent.ReAddAll(level);
            if (suppressLagPauserForStartPos) {
                AkronModule.SuppressLagPauserForNativeStartPosRestore();
            }
        }

        return AkronSaveLoadResult.Success;
    }

    internal static AkronSaveLoadResult RestoreRuntimeFreshBaseline(
        Level level,
        string stateSlotName
    ) {
        if (level == null) {
            LastPersistentSnapshotError = "the requested StartPos fresh-room baseline is unavailable";
            return AkronSaveLoadResult.NoState;
        }
        string normalizedStateSlotName = NormalizeRuntimeSlotName(stateSlotName);
        using AkronSaveLoadSlotLease baseline =
            AkronStartPosPersistence.RetainRuntimeFreshBaseline(normalizedStateSlotName);
        if (baseline?.Slot == null) {
            LastPersistentSnapshotError = "the requested StartPos fresh-room baseline is unavailable";
            return AkronSaveLoadResult.NoState;
        }

        AkronSaveLoadResult restore = RestoreRuntimeState(
            level,
            baseline.Slot,
            allowDeadPlayer: true,
            freshBaselineStateSlotName: normalizedStateSlotName);
        if (restore != AkronSaveLoadResult.Success) {
            LastPersistentSnapshotError =
                "the requested StartPos fresh-room baseline could not be restored: " + restore;
        }
        return restore;
    }

    // usedSnapshot reports which path actually ran. The caller cannot work this out
    // from HasRuntimeStateInMemory before the call: a chapter re-entry leaves a stale
    // runtime slot in memory that fails with SessionMismatch, and the load then falls
    // through to the snapshot rebuild below. Reporting the pre-call memory state
    // instead mislabelled a 4.6 s rebuild as a warm restore in the log, and suppressed
    // the prewarm of the map's other slots in the one case prewarm exists for.
    public static AkronSaveLoadResult LoadRuntimeState(
        Level level,
        string slotName,
        bool allowDeadPlayer,
        out bool usedSnapshot
    ) {
        usedSnapshot = false;
        SetPersistentSnapshotFailure(
            string.Empty,
            string.Empty,
            AkronReconstructionRefusalKind.SavedObject);
        if (level == null) {
            return AkronSaveLoadResult.Failed;
        }

        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        CurrentSlotName = normalizedSlotName;
        if (ShouldBrokerRuntimeState(normalizedSlotName)) {
            AkronSaveLoadResult brokerResult = AkronSpeedrunToolBroker.Load(normalizedSlotName);
            bool canFallBackToNativeSlot = brokerResult == AkronSaveLoadResult.BrokerUnavailable ||
                                           brokerResult == AkronSaveLoadResult.NoState && RuntimeSlots.ContainsKey(normalizedSlotName);
            if (!canFallBackToNativeSlot) {
                return brokerResult;
            }
        }

        if (RuntimeSlots.TryGetValue(normalizedSlotName, out AkronSaveLoadSlotOwner saveSlot)) {
            AkronSaveLoadResult warmResult = RestoreRuntimeState(level, saveSlot.Slot, allowDeadPlayer);
            if (warmResult != AkronSaveLoadResult.SessionMismatch) {
                return warmResult;
            }

            // A chapter re-entry creates a new session nonce while the process can
            // still hold the old native slot. Drop only that stale memory copy, then
            // rebuild from the restart-safe snapshot below.
            DiscardRuntimeStateMemory(normalizedSlotName);
        }
        // A newer Set can be usable in memory while its replacement snapshot is
        // still saving or has failed. Never pair that newer position metadata
        // with the previous successful snapshot from the same disk slot.
        if (AkronActions.HasPendingStartPosState(normalizedSlotName)) {
            // A Load that reaches here already tried to finish the copy and could
            // not, so the message has to say what the player can do about it rather
            // than only that it is unfinished.
            LastPersistentSnapshotError =
                "its restart copy is still finishing; pause the game for a moment and load it again";
            return AkronSaveLoadResult.NoState;
        }
        if (!AkronStartPosReconstruction.HasSnapshot(normalizedSlotName)) {
            // This says nothing about why, deliberately. A slot emptied by a snapshot
            // format move never gets here: BuildRuntimeStartPositions keeps only slots
            // HasRuntimeState answers for, which is memory or a snapshot under the
            // current name, so such a slot is gone from the list before a load can be
            // pressed and the player is told about the move by DescribeMissingStartPos,
            // out of the catalog entry that records which format wrote it. Reaching
            // here means a slot that had a readable copy when the room loaded and does
            // not now, and nothing here knows what happened to it.
            LastPersistentSnapshotError = "no restart copy of this StartPos exists on disk";
            return AkronSaveLoadResult.NoState;
        }
        usedSnapshot = true;
        return RestorePersistentRuntimeState(level, normalizedSlotName, allowDeadPlayer);
    }

    internal static bool HasRuntimeStateInMemory(string slotName) {
        return RuntimeSlots.ContainsKey(NormalizeRuntimeSlotName(slotName));
    }

    // "Is this slot in memory" and "will this slot load from memory" are different
    // questions after a chapter re-entry: the runtime slot survives the re-entry but
    // its session nonce does not, so the warm attempt above returns SessionMismatch and
    // the load rebuilds from the snapshot. Anything deciding whether a slot is about to
    // pay a snapshot read has to ask the second question. Measured on the test box,
    // asking the first one made prewarm skip every slot on a map the player had
    // re-entered, so the next Load paid the full 4.1 s read.
    internal static bool WillRestoreFromRuntimeMemory(Level level, string slotName) {
        return level != null &&
               RuntimeSlots.TryGetValue(NormalizeRuntimeSlotName(slotName), out AkronSaveLoadSlotOwner owner) &&
               owner?.Slot != null &&
               MatchesCurrentNativeSession(level, owner.Slot);
    }

    private static AkronSaveLoadResult RestorePersistentRuntimeState(
        Level level,
        string slotName,
        bool allowDeadPlayer
    ) {
        if (!CanAccessNativeState(level, out _, allowDeadPlayer)) {
            return AkronSaveLoadResult.Blocked;
        }
        if (!AkronStartPosReconstruction.TryLoadSnapshot(slotName, out AkronReconstructionDocument document, out string loadError, out string loadRefusedTypeName)) {
            // The reader refuses one thing: a saved type this process cannot load. That is
            // a missing object every time, and the map rule does not run until the rebuild.
            SetPersistentSnapshotFailure(
                loadError,
                loadRefusedTypeName,
                AkronReconstructionRefusalKind.SavedObject);
            return AkronSaveLoadResult.Failed;
        }
        if (!string.Equals(level.Session.Area.GetSID(), document.MapSid, StringComparison.Ordinal)) {
            LastPersistentSnapshotError = "snapshot map differs";
            return AkronSaveLoadResult.SessionMismatch;
        }
        if ((SaveData.Instance?.FileSlot ?? -1) != document.FileSlot) {
            LastPersistentSnapshotError = "snapshot save file differs";
            return AkronSaveLoadResult.SessionMismatch;
        }
        if (document.ActionStateDocument == null) {
            LastPersistentSnapshotError = "snapshot registered action state is missing";
            return AkronSaveLoadResult.Failed;
        }
        List<string> currentActionIds = RegisteredActions.Select(action => action.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (document.RegisteredActionIds == null || !document.RegisteredActionIds.SequenceEqual(currentActionIds, StringComparer.Ordinal)) {
            LastPersistentSnapshotError = "registered action set differs";
            return AkronSaveLoadResult.Failed;
        }

        // A persistent restore unloads the current room before reconstructing
        // the saved one. Keep a native clone so every failure can return the
        // player to the exact state that existed before Load was attempted.
        string rollbackSlotName = AkronActions.StartPosStateSlotPrefix + "Restore rollback " + Guid.NewGuid().ToString("N");
        AkronSaveLoadSlot rollbackSlot;
        try {
            // The rollback slot never goes to disk. Capturing every render target here would
            // add GPU readback work to the already-expensive first post-restart Load.
            rollbackSlot = CaptureRuntimeState(
                level,
                rollbackSlotName,
                saveTimeAndDeaths: true,
                capturePersistentResources: false);
        } catch (Exception exception) {
            CurrentSlotName = slotName;
            LastPersistentSnapshotError = "could not capture pre-load state: " + exception.GetType().Name + ": " + exception.Message;
            return AkronSaveLoadResult.Failed;
        }
        CurrentSlotName = slotName;
        if (rollbackSlot == null) {
            LastPersistentSnapshotError = "could not capture pre-load state";
            return AkronSaveLoadResult.Failed;
        }

        AkronSaveLoadResult restoreResult;
        AkronSaveLoadSlotLease freshBaseline = null;
        try {
            AkronIgnoreSaveStateComponent.RemoveAll(level);
            try {
                restoreResult = RestorePersistentRuntimeStateCore(level, document, out freshBaseline);
            } catch (Exception exception) {
                // A saved type that will not load is refused while the document is walked,
                // which is outside the reconstruction graph's own handlers, so an
                // uninstalled mod arrives here rather than as a returned failure. The
                // refusal still names the type and what it is about, and that is what
                // names the missing mod or the map that stopped placing the entity.
                AkronReconstructionException refusal = exception as AkronReconstructionException;
                SetPersistentSnapshotFailure(
                    exception.GetType().Name + ": " + exception.Message,
                    refusal?.RefusedTypeName ?? string.Empty,
                    refusal?.RefusedKind ?? AkronReconstructionRefusalKind.SavedObject);
                restoreResult = AkronSaveLoadResult.Failed;
            } finally {
                AkronDeepClone.ClearSharedState();
                AkronIgnoreSaveStateComponent.ReAddAll(level);
            }

            if (restoreResult == AkronSaveLoadResult.Success) {
                AkronSaveLoadResult cacheResult = CacheRestoredRuntimeState(level, slotName, freshBaseline);
                if (cacheResult != AkronSaveLoadResult.Success) {
                    AkronLog.Warn(nameof(AkronSaveLoadService),
                        "Restored StartPos could not be cached; later Loads will use the cold path. " +
                        LastPersistentSnapshotError);
                }
                return AkronSaveLoadResult.Success;
            }

            string persistentFailure = LastPersistentSnapshotError;
            AkronSaveLoadResult rollbackResult = RestoreRuntimeState(level, rollbackSlot, allowDeadPlayer: true);
            // A successful rollback is indistinguishable from "the button did nothing"
            // unless the message says so. Name the outcome, not just the failure.
            LastPersistentSnapshotError = rollbackResult == AkronSaveLoadResult.Success
                ? persistentFailure + "; nothing was changed and you are still in " + level.Session.Level
                : persistentFailure + "; pre-load state rollback failed: " + rollbackResult;
            return restoreResult;
        } finally {
            CurrentSlotName = slotName;
            freshBaseline?.Dispose();
            ReleaseRuntimeSlotResources(rollbackSlot);
        }
    }

    private static AkronSaveLoadResult CacheRestoredRuntimeState(
        Level level,
        string slotName,
        AkronSaveLoadSlotLease freshBaseline
    ) {
        try {
            // A cold Load can make an evicted slot warm again. Reserve its projected clone cost just like Set
            // does, or repeatedly loading cold slots can rebuild an unbounded in-memory population.
            if (!PrepareWarmStartPosCapture(level.Session.Area.GetSID(), out _, out _)) {
                LastPersistentSnapshotError = "restored StartPos could not be cached inside the warm memory limit";
                return AkronSaveLoadResult.Failed;
            }

            long allocatedBeforeCapture = GC.GetAllocatedBytesForCurrentThread();
            AkronSaveLoadSlot cachedSlot = CaptureRuntimeState(
                level,
                slotName,
                saveTimeAndDeaths: false,
                capturePersistentResources: false);
            if (cachedSlot == null) {
                LastPersistentSnapshotError = "restored StartPos could not be cached in memory";
                return AkronSaveLoadResult.Failed;
            }
            StoreRuntimeSlot(slotName, cachedSlot);
            RecordWarmStartPosCost(slotName, allocatedBeforeCapture);
            AkronStartPosPersistence.AttachRuntimeFreshBaseline(slotName, freshBaseline);
            AkronStartPosPersistence.UseRuntimeFreshBaseline(slotName);
            if (protectedWarmStartPosSlots != null &&
                WarmStartPosBytes > WarmStartPosBudgetBytes) {
                // The restored level stays live. Release only its rejected cache clone;
                // global ClearState callbacks would erase helper state from that level.
                ReleaseRuntimeStateMemory(slotName);
                LastPersistentSnapshotError =
                    "warming every StartPos would exceed this machine's " +
                    (WarmStartPosBudgetBytes / (1024d * 1024d))
                        .ToString("F0", CultureInfo.InvariantCulture) +
                    " MB warm-state budget";
                return AkronSaveLoadResult.Failed;
            }
            // The measured clone can exceed the projection used above. Reconcile against the exact cost before
            // returning; this slot has a restart copy, so it remains loadable even if it is the one dropped.
            TrimWarmStartPosSlots(out _);
            return AkronSaveLoadResult.Success;
        } catch (Exception exception) {
            LastPersistentSnapshotError = "restored StartPos cache failed: " +
                                          exception.GetType().Name + ": " + exception.Message;
            return AkronSaveLoadResult.Failed;
        }
    }

    private static AkronSaveLoadResult RestorePersistentRuntimeStateCore(
        Level level,
        AkronReconstructionDocument document,
        out AkronSaveLoadSlotLease freshBaseline
    ) {
        freshBaseline = null;
        AkronCumulativeStats cumulativeStats = AkronCumulativeStats.Capture(level);
        foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
            action.BeforeLoadState?.Invoke(level);
        }
        if (!TryLoadFreshRoom(level, document.Room, out string freshRoomError)) {
            LastPersistentSnapshotError = freshRoomError;
            return AkronSaveLoadResult.Failed;
        }

        freshBaseline = CaptureFreshRuntimeState(
            level,
            "Akron restored fresh-room baseline " + document.MapSid + "|" + document.Room,
            document.SlotName);
        if (freshBaseline?.Slot == null) {
            freshBaseline?.Dispose();
            freshBaseline = null;
            LastPersistentSnapshotError = "fresh-room baseline capture returned no state";
            return AkronSaveLoadResult.Failed;
        }
        CurrentSlotName = document.SlotName;
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshActionState =
            freshBaseline.Slot.ActionState;
        AkronReconstructionRestore actionRestore = AkronStartPosReconstruction.RestoreActionState(
            document.ActionStateDocument,
            freshActionState);
        if (!actionRestore.Success) {
            SetPersistentSnapshotFailure(
                "registered action state " + actionRestore.Error,
                actionRestore.RefusedTypeName,
                actionRestore.RefusedKind);
            AkronDeepClone.ClearSharedState();
            return AkronSaveLoadResult.Failed;
        }
        // The snapshot was measured against a fresh room with its playback ghosts
        // filtered out, and the rebuild resolves every saved object by its path in
        // the live fresh room. One extra entity in one list would shift every later
        // index in that list, so the live room has to match the shape the snapshot
        // was measured against for as long as anything reads it.
        List<Entity> detachedGhosts = AkronSnapshotExclusion.DetachFromLevel(level);
        try {
            return RestorePersistentRuntimeStateAfterActionState(
                level,
                document,
                cumulativeStats,
                freshActionState,
                actionRestore);
        } finally {
            // Registered callbacks receive their own clones. The reconstructed
            // action graph is only an intermediate owner and must not retain
            // its dormant FMOD instances after the callbacks finish.
            AkronStartPosReconstruction.ReleaseEventInstances(actionRestore);
            // Reached on success, on a returned failure and on an exception on its
            // way to the rollback. The failure paths reload the room, which builds
            // its own ghosts, and ReattachToLevel drops the stale ones when it sees
            // them. The exception path does not reload, so this is what keeps the
            // rolled-back room from losing its ghost.
            AkronSnapshotExclusion.ReattachToLevel(level, detachedGhosts);
        }
    }

    private static AkronSaveLoadResult RestorePersistentRuntimeStateAfterActionState(
        Level level,
        AkronReconstructionDocument document,
        AkronCumulativeStats cumulativeStats,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshActionState,
        AkronReconstructionRestore actionRestore
    ) {
        using IDisposable hookOwnerScope = AkronStartPosReconstruction.UseHookOwnerRegistrations();
        AkronReconstructionVerification actionVerification = AkronStartPosReconstruction.Verify(
            document.ActionStateDocument,
            actionRestore,
            Array.Empty<string>());
        if (!actionVerification.Success) {
            SetPersistentSnapshotFailure(
                "registered action state " + actionVerification.Error,
                actionVerification.RefusedTypeName,
                actionVerification.RefusedKind);
            AkronDeepClone.ClearSharedState();
            return AkronSaveLoadResult.Failed;
        }

        AkronPersistentRuntimeState freshRuntimeState = AkronPersistentRuntimeState.CaptureCurrent(level);
        AkronReconstructionRestore restore = AkronStartPosReconstruction.Restore(document, freshRuntimeState);
        if (!restore.Success) {
            SetPersistentSnapshotFailure("rebuild " + restore.Error, restore.RefusedTypeName, restore.RefusedKind);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }
        if (!ApplyPersistentRuntimeState(level, freshRuntimeState)) {
            AkronStartPosReconstruction.ReleaseEventInstances(restore);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }

        // The saved tracker lists preserve the Set-frame lookup order. Refresh
        // them against the restored entity graph so Everest can also add any
        // process-owned tracked types registered during this game launch.
        // Without this merge, a helper hook can index a type that the restored
        // room did not use and crash before the first restored frame renders.
        Tracker.Refresh(level, force: true);

        foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
            if (freshActionState.TryGetValue(action.Id, out Dictionary<Type, Dictionary<string, object>> savedValues)) {
                action.LoadState?.Invoke((Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues), level);
            }
        }
        AkronDeepClone.ClearSharedState();

        // Registered helper callbacks restore their own state, but some also
        // rebuild derived Celeste render caches as a side effect. Reapply the
        // saved room fields to the same reconstructed objects so the final
        // room state still belongs to the exact Set frame.
        AkronReconstructionVerification reapply = AkronStartPosReconstruction.Reapply(document, restore);
        if (!reapply.Success) {
            SetPersistentSnapshotFailure("reapply " + reapply.Error, reapply.RefusedTypeName, reapply.RefusedKind);
            AkronStartPosReconstruction.ReleaseEventInstances(restore);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }
        if (!ApplyPersistentRuntimeState(level, freshRuntimeState)) {
            AkronStartPosReconstruction.ReleaseEventInstances(restore);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }
        Tracker.Refresh(level, force: true);

        // These cumulative values are the only state that StartPos does not
        // rewind. Apply them before verification and mask their graph fields.
        cumulativeStats.RestoreWithoutRewinding(level);
        AkronReconstructionVerification verification = AkronStartPosReconstruction.Verify(
            document,
            restore,
            AkronStartPosReconstruction.GetPostRestoreVerificationMasks(document));
        if (!verification.Success) {
            SetPersistentSnapshotFailure(
                "verify " + verification.Error,
                verification.RefusedTypeName,
                verification.RefusedKind);
            AkronStartPosReconstruction.ReleaseEventInstances(restore);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }

        AkronLevelGraphRepair.RelinkEntitiesToLevel(level);
        AkronLevelRenderState.RelinkRendererCameras(level);
        Audio.SetCamera(level.Camera);
        AkronVirtualAssetReloadTracker.ReloadDisposedAssets(level);
        AkronGameplayBufferState.RestoreBestEffort(document.GameplayBuffers);
        // Berry progress is persistent save data. Apply it only after the
        // remaining restore work can no longer report a normal failure.
        if (document.BerryProgress != null &&
            !document.BerryProgress.TryRestore(level, out string berryRestoreError)) {
            LastPersistentSnapshotError = berryRestoreError;
            AkronStartPosReconstruction.ReleaseEventInstances(restore);
            TryLoadFreshRoom(level, document.Room, out _);
            return AkronSaveLoadResult.Failed;
        }
        AkronGameplayBufferState.ArmLevelPresentation(level, document.GameplayBuffers);
        AkronStartPosReconstruction.ActivateEventInstances(restore);
        return AkronSaveLoadResult.Success;
    }

    private static bool ApplyPersistentRuntimeState(Level level, AkronPersistentRuntimeState state) {
        if (!ReferenceEquals(state?.Level, level)) {
            LastPersistentSnapshotError = "snapshot level identity differs";
            return false;
        }
        if (!float.IsFinite(state.EngineTimeRate) ||
            !float.IsFinite(state.GlitchValue) ||
            !float.IsFinite(state.DistortAnxiety) ||
            !float.IsFinite(state.DistortGameRate)) {
            LastPersistentSnapshotError = "snapshot process-global float is not finite";
            return false;
        }
        Settings.Instance.GrabMode = state.GrabMode;
        Settings.Instance.CrouchDashMode = state.CrouchDashMode;
#pragma warning disable CS0618
        Engine.TimeRate = state.EngineTimeRate;
#pragma warning restore CS0618
        Glitch.Value = state.GlitchValue;
        Distort.Anxiety = state.DistortAnxiety;
        Distort.GameRate = state.DistortGameRate;

        foreach (EverestModule module in Everest.Modules.Where(module =>
                     module is not AkronModule && module.GetType().Name != "NullModule")) {
            string key = module.GetType().FullName ?? module.GetType().Name;
            if (state.ModuleSessions.TryGetValue(key, out EverestModuleSession moduleSession)) {
                module._Session = moduleSession;
            }
        }
        return true;
    }

    private static bool TryLoadFreshRoom(Level level, string roomName, out string error) {
        error = string.Empty;
        LevelData room = level?.Session?.MapData?.Get(roomName ?? string.Empty);
        if (level == null || room == null) {
            error = "saved room is unavailable: " + (roomName ?? string.Empty);
            return false;
        }

        try {
            Vector2 probe = new Vector2(room.Bounds.Left, room.Bounds.Bottom);
            level.Session.Level = room.Name;
            level.Session.RespawnPoint = level.Session.GetSpawnPoint(probe);
            level.StartPosition = null;
            level.Tracker.GetEntitiesCopy<Player>().ForEach(player => player.RemoveSelf());
            // Celeste's own room reload clears the trails before it unloads:
            // Level.Reload runs TrailManager.Clear() immediately before
            // UnloadLevel() and LoadLevel(Respawn). That is not decoration and it
            // is not optional here. UnloadLevel keeps every Tags.Global entity,
            // and TrailManager.Snapshot takes Tags.Global in Snapshot.Init while
            // holding the live PlayerSprite and PlayerHair of the entity it was
            // made from. Monocle never clears Component.Entity on removal, so a
            // snapshot outlives the entity this reload destroys and keeps it
            // reachable; LoadLevel then rebuilds the same map entity with the same
            // EntityID, and the fresh room ends up holding two copies of one
            // identity, one of them dead with a null Scene. A saved node then
            // pairs with the dead copy and the restore refuses an edge that is
            // genuinely unprovable.
            //
            // Level.Reload is also what a death respawn runs, so it is what
            // produces the fresh-room baseline this restore is measured against.
            // Do not drop this call to "simplify" the sequence: the fresh room has
            // to be the room Celeste's own reload produces, because that is the
            // room every authenticity rule in the reconstruction graph is written
            // against.
            //
            // The UpdateLists is Akron's own and it is required for the clear to
            // reach anything. A StartPos load runs at the render boundary after
            // Engine.Update has returned (AkronModule.EngineOnRenderCore ->
            // RunAfterEngineUpdateActions), so a trail created during the update
            // that just ran - Player.CreateTrail on a dash is the common one - is
            // still in EntityList.toAdd with a null Scene. TrailManager.Clear
            // calls RemoveSelf, which does nothing to an entity that is not
            // installed yet, and EntityList.UpdateLists installs toAdd before it
            // processes toRemove. Without this line UnloadLevel's own UpdateLists
            // installs that trail after the clear has already run, and the newest
            // trail - the one most likely to hold the entity this reload is about
            // to destroy - survives into the fresh room. Celeste's Level.Reload
            // needs no equivalent because it runs from a screen-wipe callback a
            // second after the player died, with no trail in flight.
            level.Entities.UpdateLists();
            TrailManager.Clear();
            level.UnloadLevel();
            level.Completed = false;
            level.InCutscene = false;
            level.SkippingCutscene = false;
            using (AkronStartPosPersistence.SuppressBaselineCapture()) {
                level.LoadLevel(Player.IntroTypes.Respawn);
                AkronModule.RunFreshRoomInitializationUpdate(level);
            }
            DrainFreshRoomEntityLists(level.Entities);
            AkronLevelRenderState.RelinkRendererCameras(level);
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    internal static void DrainFreshRoomEntityLists(EntityList entities) {
        DrainFreshRoomEntityListsCore(
            entities.UpdateLists,
            () => (List<Entity>) EntityListEntitiesField.GetValue(entities),
            () => ((List<Entity>) EntityListToAddField.GetValue(entities)).Count,
            entity => {
                ComponentList components = entity == null
                    ? null
                    : (ComponentList) EntityComponentsField.GetValue(entity);
                if (components == null) {
                    return false;
                }
                List<Component> current = (List<Component>) ComponentListComponentsField.GetValue(components);
                int previousComponentCount = current.Count;
                DrainFreshRoomComponentList(components);
                return current.Count != previousComponentCount;
            });
    }

    internal static void DrainFreshRoomEntityListsCore(
        Action updateLists,
        Func<IReadOnlyList<Entity>> getEntities,
        Func<int> getPendingCount,
        Func<Entity, bool> drainComponents
    ) {
        int previousCount = -1;
        for (int pass = 0; pass < MaxFreshRoomEntityListDrainPasses; pass++) {
            updateLists();
            IReadOnlyList<Entity> entities = getEntities();
            bool componentsChanged = false;
            for (int index = 0; index < entities.Count; index++) {
                componentsChanged |= drainComponents(entities[index]);
            }
            if (getPendingCount() == 0 && entities.Count == previousCount && !componentsChanged) {
                return;
            }
            previousCount = entities.Count;
        }

        throw new InvalidOperationException(
            "fresh room entity additions did not settle within " +
            MaxFreshRoomEntityListDrainPasses.ToString(CultureInfo.InvariantCulture) +
            " passes");
    }

    internal static void DrainFreshRoomComponentList(ComponentList components) {
        // ComponentList flushes its pending Add/Remove callbacks when its
        // internal lock returns to Open. A cold room load can finish its entity
        // queue one frame before this component queue, so run the engine's own
        // transition before the reconstruction graph indexes the fresh room.
        object openLockMode = Enum.ToObject(ComponentListLockModeProperty.PropertyType, 0);
        ComponentListLockModeProperty.SetValue(components, openLockMode);
    }

    private sealed class AkronCumulativeStats {
        private long sessionTime;
        private int sessionDeaths;
        private int roomDeaths;
        private long saveDataTime;
        private int totalDeaths;
        private long areaTime;
        private int areaDeaths;
        private AreaKey area;

        public static AkronCumulativeStats Capture(Level level) {
            AreaKey currentArea = level.Session.Area;
            AreaModeStats mode = TryGetAreaModeStats(currentArea);
            return new AkronCumulativeStats {
                sessionTime = level.Session.Time,
                sessionDeaths = level.Session.Deaths,
                roomDeaths = level.Session.DeathsInCurrentLevel,
                saveDataTime = SaveData.Instance?.Time ?? 0L,
                totalDeaths = SaveData.Instance?.TotalDeaths ?? 0,
                areaTime = mode?.TimePlayed ?? 0L,
                areaDeaths = mode?.Deaths ?? 0,
                area = currentArea
            };
        }

        public void RestoreWithoutRewinding(Level level) {
            // Capture runs immediately before the room unloads. Restore these
            // recipient values exactly so an imported pack cannot add the
            // snapshot author's larger counters.
            level.Session.Time = sessionTime;
            level.Session.Deaths = sessionDeaths;
            level.Session.DeathsInCurrentLevel = roomDeaths;
            if (SaveData.Instance == null) {
                return;
            }

            SaveData.Instance.Time = saveDataTime;
            SaveData.Instance.TotalDeaths = totalDeaths;
            AreaModeStats mode = TryGetAreaModeStats(area);
            if (mode == null) {
                return;
            }
            mode.TimePlayed = areaTime;
            mode.Deaths = areaDeaths;
        }

        private static AreaModeStats TryGetAreaModeStats(AreaKey areaKey) {
            if (SaveData.Instance?.Areas_Safe == null ||
                areaKey.ID < 0 || areaKey.ID >= SaveData.Instance.Areas_Safe.Count) {
                return null;
            }

            AreaStats areaStats = SaveData.Instance.Areas_Safe[areaKey.ID];
            int modeIndex = (int) areaKey.Mode;
            if (areaStats?.Modes == null || modeIndex < 0 || modeIndex >= areaStats.Modes.Length) {
                return null;
            }
            return areaStats.Modes[modeIndex];
        }
    }

    public static bool HasRuntimeState(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        return RuntimeSlots.ContainsKey(normalizedSlotName) ||
               AkronStartPosReconstruction.HasSnapshot(normalizedSlotName) ||
               ShouldBrokerRuntimeState(normalizedSlotName) && AkronSpeedrunToolBroker.IsSaved(normalizedSlotName);
    }

    public static AkronSaveLoadSlot GetRuntimeStateForDebug(string slotName) {
        RuntimeSlots.TryGetValue(NormalizeRuntimeSlotName(slotName), out AkronSaveLoadSlotOwner saveSlot);
        return saveSlot?.Slot;
    }

    internal static AkronSaveLoadSlotLease RetainRuntimeState(string slotName) {
        return RuntimeSlots.TryGetValue(NormalizeRuntimeSlotName(slotName), out AkronSaveLoadSlotOwner saveSlot)
            ? saveSlot.Retain()
            : null;
    }

    public static void ClearRuntimeState(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        AkronSpeedrunToolBroker.Clear(normalizedSlotName);
        AkronStartPosReconstruction.DeleteSnapshot(normalizedSlotName);
        if (ReleaseRuntimeStateMemory(normalizedSlotName)) {
            RunClearStateActions();
        }
    }

    internal static void DiscardRuntimeStateMemory(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        if (ReleaseRuntimeStateMemory(normalizedSlotName)) {
            RunClearStateActions();
        }
    }

    private static bool ReleaseRuntimeStateMemory(string normalizedSlotName) {
        AkronStartPosPersistence.RemoveRuntimeFreshBaseline(normalizedSlotName);
        if (RuntimeSlots.Remove(normalizedSlotName, out AkronSaveLoadSlotOwner removedSlot)) {
            removedSlot.ReleaseOwnership();
            MarkRuntimeSlotsChanged();
            if (activeWarmStartPosBudgetBytes == null &&
                WarmStartPosBytes <= MaxWarmStartPosBytes) {
                retainedWarmStartPosBudgetBytes = MaxWarmStartPosBytes;
            }
            return true;
        }
        return false;
    }

    // A StartPos Set has to be atomic with respect to the state its slot already held: if
    // the replacement never becomes durable, the previous warm clone must still be there.
    // Parking moves the owner to a private key instead of releasing it, which frees the
    // canonical name for the new capture and keeps the previous clone alive, referenced by
    // no catalog entry, until the Set either commits or rolls back. The parked key never
    // reaches a StartPos entry, so nothing looks it up by name.
    internal static string ParkRuntimeState(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        if (!RuntimeSlots.Remove(normalizedSlotName, out AkronSaveLoadSlotOwner parkedSlot)) {
            return null;
        }

        string parkedName = normalizedSlotName + " (parked " + Guid.NewGuid().ToString("N") + ")";
        RuntimeSlots[parkedName] = parkedSlot;
        // Carry the warm cost across with the clone. Parking is a rename, and a rename is
        // the one move the reconcile in WarmStartPosBytes cannot follow: the bytes are
        // still resident, but under a key it no longer recognises, so it would write them
        // off as freed while the clone is very much still there.
        MoveWarmStartPosCost(normalizedSlotName, parkedName);
        MarkRuntimeSlotsChanged();
        return parkedName;
    }

    private static void MoveWarmStartPosCost(string fromSlotName, string toSlotName) {
        if (WarmStartPosCosts.Remove(fromSlotName, out WarmStartPosCost cost)) {
            WarmStartPosCosts[toSlotName] = cost;
        }
    }

    internal static void RestoreParkedRuntimeState(string parkedName, string slotName) {
        // The failed Set's own capture holds the canonical name. It never committed, so it
        // is released rather than parked in turn, together with the fresh-room baseline it
        // attached. This is the same release StoreRuntimeSlot performs when a Set replaces
        // a warm clone, applied to the capture that is being abandoned instead.
        //
        // It runs even when there is no parked clone to put back. A slot whose warm clone
        // was already gone - after a restart, or after a session mismatch dropped it - has
        // only its snapshot, and leaving the abandoned capture on the canonical name would
        // pair the new state with the previous metadata on the next load.
        DiscardRuntimeStateMemory(slotName);
        if (string.IsNullOrWhiteSpace(parkedName) ||
            !RuntimeSlots.Remove(parkedName, out AkronSaveLoadSlotOwner parkedSlot)) {
            return;
        }

        RuntimeSlots[NormalizeRuntimeSlotName(slotName)] = parkedSlot;
        MoveWarmStartPosCost(parkedName, NormalizeRuntimeSlotName(slotName));
        MarkRuntimeSlotsChanged();
    }

    internal static void DiscardParkedRuntimeState(string parkedName) {
        if (string.IsNullOrWhiteSpace(parkedName) ||
            !RuntimeSlots.Remove(parkedName, out AkronSaveLoadSlotOwner parkedSlot)) {
            return;
        }

        // No RunClearStateActions here: those callbacks are global rather than per slot,
        // and this runs on the successful-Set path, where the slot still holds live state
        // that the callbacks would tell helper mods to throw away.
        parkedSlot.ReleaseOwnership();
        MarkRuntimeSlotsChanged();
    }

    internal static void ClearRuntimeStateExceptPersistentSnapshot(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        AkronSpeedrunToolBroker.Clear(normalizedSlotName);
        DiscardRuntimeStateMemory(normalizedSlotName);
    }

    public static bool HasSlot(int slot) {
        return AkronSpeedrunToolBroker.IsSaved(slot);
    }

    // ModInterop export. Akron holds no numbered slots of its own; the slot is Speedrun
    // Tool's, so clearing it means asking Speedrun Tool to clear it.
    public static void ClearSlot(int slot) {
        AkronSpeedrunToolBroker.Clear(GetSlotName(slot));
    }

    public static string GetSlotName(int slot) {
        return slot == 1 ? "Default Slot" : "SaveSlot@" + slot;
    }

    private static string NormalizeRuntimeSlotName(string slotName) {
        return string.IsNullOrWhiteSpace(slotName) ? "Runtime Slot" : slotName.Trim();
    }

    private static bool ShouldBrokerRuntimeState(string slotName) {
        // StartPos practice needs the current Level graph to remain the canonical
        // live scene after load so later room transitions rebuild gameplay
        // renderers normally. The Speedrun Tool TAS broker is stable for ordinary
        // savestates, but its freeze/wipe path can leave StartPos loads with stale
        // visual state on the next room warp. Keep StartPos on Akron's own
        // runtime clone path; every other slot is Speedrun Tool's.
        return !slotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
    }

    internal static void SaveStaticMembers(Dictionary<Type, Dictionary<string, object>> savedValues, Type type, params string[] memberNames) {
        SaveStaticMemberValues(savedValues, type, memberNames);
    }

    internal static void LoadStaticMembers(Dictionary<Type, Dictionary<string, object>> savedValues, Type type, params string[] memberNames) {
        LoadStaticMemberValues(savedValues, type, memberNames);
    }

    internal static void LoadStaticMembers(Dictionary<Type, Dictionary<string, object>> savedValues) {
        foreach (KeyValuePair<Type, Dictionary<string, object>> pair in savedValues) {
            LoadStaticMemberValues(savedValues, pair.Key, pair.Value.Keys.ToArray());
        }
    }

    private static AkronSaveLoadSlot BuildNativeSlot(Level level, string slotName, bool saveTimeAndDeaths, bool includeLevelSnapshot = true) {
        Player player = level.Tracker.GetEntity<Player>();
        AkronSaveLoadSlot saveSlot = new AkronSaveLoadSlot(
            slotName,
            level.Session.Level,
            level.Session.Area.GetSID(),
            saveTimeAndDeaths
        );

        try {
            saveSlot.SessionNonce = AkronModule.Session.CurrentSessionNonce;
            if (includeLevelSnapshot) {
                saveSlot.SavedLevel = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
                saveSlot.SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant(level, saveSlot.SavedLevel);
                ClearDeadCutsceneSkipCallback(saveSlot.SavedLevel);
            }
            saveSlot.SessionState = (Session) DeepClone(level.Session);
            CaptureCuratedSessionState(level.Session, saveSlot);
            if (SaveData.Instance != null) {
                saveSlot.SaveDataState = (SaveData) DeepClone(SaveData.Instance);
            }

            if (player != null) {
                saveSlot.PlayerPosition = player.Position;
                saveSlot.PlayerSpeed = player.Speed;
                saveSlot.PlayerState = player.StateMachine.State;
                saveSlot.Stamina = player.Stamina;
                saveSlot.Dashes = player.Dashes;
                saveSlot.Facing = player.Facing;
            }

            saveSlot.RespawnPoint = level.Session.RespawnPoint;
            saveSlot.Time = level.Session.Time;
            saveSlot.Deaths = level.Session.Deaths;
            saveSlot.DeathsInCurrentLevel = level.Session.DeathsInCurrentLevel;
            saveSlot.FileSlot = SaveData.Instance?.FileSlot ?? -1;
            saveSlot.BerryProgress = AkronBerryProgressSnapshot.Capture(level);
            saveSlot.SaveDataTime = SaveData.Instance?.Time ?? 0L;
            saveSlot.SaveDataTotalDeaths = SaveData.Instance?.TotalDeaths ?? 0;
            if (SaveData.Instance != null) {
                AreaKey areaKey = level.Session.Area;
                saveSlot.AreaTimePlayed = SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int) areaKey.Mode].TimePlayed;
                saveSlot.AreaDeaths = SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int) areaKey.Mode].Deaths;
            }
            saveSlot.LevelTimeActive = level.TimeActive;
            saveSlot.LevelRawTimeActive = level.RawTimeActive;
            saveSlot.GrabMode = Settings.Instance.GrabMode;
            saveSlot.CrouchDashMode = Settings.Instance.CrouchDashMode;
#pragma warning disable CS0618
            saveSlot.EngineTimeRate = Engine.TimeRate;
#pragma warning restore CS0618
            saveSlot.GlitchValue = Glitch.Value;
            saveSlot.DistortAnxiety = Distort.Anxiety;
            saveSlot.DistortGameRate = Distort.GameRate;

            foreach (EverestModule module in Everest.Modules.Where(module => module.GetType().Name != "NullModule")) {
                if (module._Session != null) {
                    saveSlot.ModuleSessions[module.GetType().FullName ?? module.GetType().Name] = (EverestModuleSession) DeepClone(module._Session);
                }
                if (module._SaveData != null) {
                    saveSlot.ModuleSaveData[module.GetType().FullName ?? module.GetType().Name] = (EverestModuleSaveData) DeepClone(module._SaveData);
                }
            }

            return saveSlot;
        } catch {
            ReleaseDormantEventInstances(saveSlot);
            throw;
        }
    }

    private static AkronSaveLoadSlot BuildPersistentBaselineSlot(Level level, string slotName) {
        AkronSaveLoadSlot saveSlot = new AkronSaveLoadSlot(
            slotName,
            level.Session.Level,
            level.Session.Area.GetSID(),
            saveTimeAndDeaths: true);
        try {
            saveSlot.SessionNonce = AkronModule.Session.CurrentSessionNonce;
            saveSlot.SavedLevel = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
            saveSlot.SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant(level, saveSlot.SavedLevel);
            ClearDeadCutsceneSkipCallback(saveSlot.SavedLevel);
            saveSlot.FileSlot = SaveData.Instance?.FileSlot ?? -1;
            saveSlot.LevelTimeActive = level.TimeActive;
            saveSlot.LevelRawTimeActive = level.RawTimeActive;
            saveSlot.GrabMode = Settings.Instance.GrabMode;
            saveSlot.CrouchDashMode = Settings.Instance.CrouchDashMode;
#pragma warning disable CS0618
            saveSlot.EngineTimeRate = Engine.TimeRate;
#pragma warning restore CS0618
            saveSlot.GlitchValue = Glitch.Value;
            saveSlot.DistortAnxiety = Distort.Anxiety;
            saveSlot.DistortGameRate = Distort.GameRate;
            foreach (EverestModule module in Everest.Modules.Where(module => module.GetType().Name != "NullModule")) {
                if (module._Session != null) {
                    saveSlot.ModuleSessions[module.GetType().FullName ?? module.GetType().Name] =
                        (EverestModuleSession) DeepClone(module._Session);
                }
            }
            return saveSlot;
        } catch {
            ReleaseDormantEventInstances(saveSlot);
            throw;
        }
    }

    private static void PrepareSlotPreClone(AkronSaveLoadSlot saveSlot) {
        AkronDeepClone.ClearSharedState();
        List<EventInstance> previousEventInstances = saveSlot?.PreClonedEventInstances;
        if (saveSlot != null) {
            saveSlot.PreClonedEventInstances = null;
            saveSlot.PreCloneState = null;
        }
        AkronEventInstanceUtils.ReleaseDormantEventInstances(previousEventInstances);
        saveSlot.PreCloneState = AkronDeepClone.CreateSharedEntityState(saveSlot);
    }

    private static void PrepareRuntimeSlotPreClone(AkronSaveLoadSlot saveSlot) {
        // RestoreNativeSlot consumes the previous reload batch before this cache
        // is rebuilt. Replace that ownership record with only the assets found
        // by the new pre-clone so repeated warm Loads stay bounded.
        AkronVirtualAssetReloadTracker.Remove(saveSlot.TrackedVirtualAssetRegistrations);
        saveSlot.TrackedVirtualAssetRegistrations = Array.Empty<AkronTrackedVirtualAssetRegistration>();
        int virtualAssetMarker = AkronVirtualAssetReloadTracker.Mark();
        try {
            PrepareSlotPreClone(saveSlot);
            saveSlot.TrackedVirtualAssetRegistrations =
                AkronVirtualAssetReloadTracker.GetRegistrationsSince(virtualAssetMarker);
        } catch {
            AkronVirtualAssetReloadTracker.DiscardSince(virtualAssetMarker);
            throw;
        }
    }

    private static bool RestoreNativeSlot(
        Level level,
        AkronSaveLoadSlot saveSlot,
        bool restoreAkronModuleState = true,
        bool restoreGlobalSaveData = true
    ) {
        List<EventInstance> restoredEventInstances = new List<EventInstance>(saveSlot.PreClonedEventInstances ?? Enumerable.Empty<EventInstance>());
        DeepCloneState preCloneState = saveSlot.PreCloneState;
        saveSlot.PreClonedEventInstances = null;
        saveSlot.PreCloneState = null;
        try {
            AkronDeepClone.SetSharedState(preCloneState);
            // SavedLevel is the immutable source for both warm restores and the
            // background restart-copy worker. Copy outward into the live Level.
            // Per-Load caches live on the slot and may rotate, but never write
            // gameplay state back into this graph while a worker lease can read it.
            Level savedLevel = saveSlot.SavedLevel;
            Session savedSession = savedLevel?.Session ?? (saveSlot.SessionState != null ? (Session) DeepClone(saveSlot.SessionState) : null);
            SaveData savedSaveData = restoreGlobalSaveData && saveSlot.SaveDataState != null
                ? (SaveData) DeepClone(saveSlot.SaveDataState)
                : null;
            long currentSessionTime = level.Session.Time;
            int currentDeaths = level.Session.Deaths;
            int currentDeathsInRoom = level.Session.DeathsInCurrentLevel;
            long currentSaveDataTime = SaveData.Instance?.Time ?? 0L;
            int currentTotalDeaths = SaveData.Instance?.TotalDeaths ?? 0;
            AreaKey currentAreaKey = level.Session.Area;
            long currentAreaTimePlayed = SaveData.Instance?.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].TimePlayed ?? 0L;
            int currentAreaDeaths = SaveData.Instance?.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].Deaths ?? 0;

            if (savedSaveData != null && !saveSlot.SaveTimeAndDeaths) {
                savedSaveData.Time = Math.Max(currentSaveDataTime, savedSaveData.Time);
                savedSaveData.TotalDeaths = Math.Max(currentTotalDeaths, savedSaveData.TotalDeaths);
                savedSaveData.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].TimePlayed =
                    Math.Max(currentAreaTimePlayed, savedSaveData.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].TimePlayed);
                savedSaveData.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].Deaths =
                    Math.Max(currentAreaDeaths, savedSaveData.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].Deaths);
            }

            if (savedLevel != null) {
                UnloadLevel(level);
                restoredEventInstances.AddRange(AkronDeepClone.CopyIntoDormant(savedLevel, level));
                AkronLevelGraphRepair.RelinkEntitiesToLevel(level);
                AkronLevelRenderState.RelinkRendererCameras(level);
                // Audio keeps a static camera reference. Copying the saved Level
                // replaces Level.Camera, so positional sounds must use the
                // restored camera before any saved FMOD handles start.
                Audio.SetCamera(level.Camera);
                AkronVirtualAssetReloadTracker.ReloadDisposedAssets(level);
            } else {
                string restoredRoom = savedSession?.Level ?? saveSlot.LevelName;
                Vector2? restoredRespawnPoint = savedSession?.RespawnPoint ?? saveSlot.RespawnPoint;
                bool roomChanged = level.Session.Level != restoredRoom || level.Session.RespawnPoint != restoredRespawnPoint;

                if (savedSession != null) {
                    AkronDeepClone.CopyInto(savedSession, level.Session);
                } else {
                    level.Session.Level = saveSlot.LevelName;
                    level.Session.RespawnPoint = saveSlot.RespawnPoint;
                    RestoreCuratedSessionState(level.Session, saveSlot);
                }

                if (roomChanged) {
                    level.Tracker.GetEntitiesCopy<Player>().ForEach(entity => entity.RemoveSelf());
                    level.UnloadLevel();
                    level.Completed = false;
                    level.InCutscene = false;
                    level.SkippingCutscene = false;
                    using (AkronStartPosPersistence.SuppressBaselineCapture()) {
                        level.LoadLevel(Player.IntroTypes.Respawn);
                    }
                    level.Entities.UpdateLists();
                }
            }

            Player player = level.Tracker.GetEntity<Player>();
            if (player != null && savedLevel == null) {
                player.Position = saveSlot.PlayerPosition;
                player.Speed = saveSlot.PlayerSpeed;
                player.StateMachine.ForceState(saveSlot.PlayerState);
                player.Stamina = saveSlot.Stamina;
                player.Dashes = saveSlot.Dashes;
                player.Facing = saveSlot.Facing;
            }

            if (savedSaveData != null) {
                SaveData.Instance = savedSaveData;
            }

            if (!saveSlot.SaveTimeAndDeaths) {
                level.Session.Time = Math.Max(currentSessionTime, level.Session.Time);
                level.Session.Deaths = Math.Max(currentDeaths, level.Session.Deaths);
                level.Session.DeathsInCurrentLevel = Math.Max(currentDeathsInRoom, level.Session.DeathsInCurrentLevel);
            }

            // These are room simulation clocks, not cumulative player stats.
            // Restore them exactly so time-driven visuals and entity behavior
            // resume from the Set frame. Session and save-file time remain
            // monotonic above when SaveTimeAndDeaths is disabled.
            level.TimeActive = saveSlot.LevelTimeActive;
            level.RawTimeActive = saveSlot.LevelRawTimeActive;

            Settings.Instance.GrabMode = saveSlot.GrabMode;
            Settings.Instance.CrouchDashMode = saveSlot.CrouchDashMode;
#pragma warning disable CS0618
            Engine.TimeRate = saveSlot.EngineTimeRate;
#pragma warning restore CS0618
            Glitch.Value = saveSlot.GlitchValue;
            Distort.Anxiety = saveSlot.DistortAnxiety;
            Distort.GameRate = saveSlot.DistortGameRate;

            foreach (EverestModule module in Everest.Modules.Where(module => module.GetType().Name != "NullModule")) {
                if (!restoreAkronModuleState && module is AkronModule) {
                    continue;
                }

                string key = module.GetType().FullName ?? module.GetType().Name;
                if (saveSlot.ModuleSessions.TryGetValue(key, out EverestModuleSession moduleSession)) {
                    module._Session = (EverestModuleSession) DeepClone(moduleSession);
                }
                if (restoreGlobalSaveData &&
                    saveSlot.ModuleSaveData.TryGetValue(key, out EverestModuleSaveData moduleSaveData)) {
                    module._SaveData = (EverestModuleSaveData) DeepClone(moduleSaveData);
                }
            }

            if (savedLevel != null) {
                // No saved-graph handle may start until every restored owner is live.
                AkronEventInstanceUtils.ActivateDormantEventInstances(restoredEventInstances);
                RepairClonedSoundSources(level);
            }

            // Helper callbacks run immediately after this method. Rebuild all
            // tracked type keys first, including keys registered after Set.
            Tracker.Refresh(level, force: true);

            return true;
        } catch {
            AkronEventInstanceUtils.ReleaseDormantEventInstances(restoredEventInstances);
            throw;
        }
    }

    private static bool MatchesCurrentNativeSession(Level level, AkronSaveLoadSlot saveSlot) {
        return level.Session.Area.GetSID() == saveSlot.MapSid &&
               (SaveData.Instance?.FileSlot ?? -1) == saveSlot.FileSlot &&
               string.Equals(saveSlot.SessionNonce, AkronModule.Session.CurrentSessionNonce, StringComparison.Ordinal);
    }

    private static void CaptureCuratedSessionState(Session session, AkronSaveLoadSlot saveSlot) {
        if (session == null || saveSlot == null) {
            return;
        }

        saveSlot.SessionFlags = new HashSet<string>(session.Flags ?? new HashSet<string>());
        saveSlot.SessionLevelFlags = new HashSet<string>(session.LevelFlags ?? new HashSet<string>());
        saveSlot.SessionCounters = (session.Counters ?? new List<Session.Counter>())
            .Where(counter => counter != null && !string.IsNullOrWhiteSpace(counter.Key))
            .GroupBy(counter => counter.Key)
            .ToDictionary(group => group.Key, group => group.Last().Value);
        saveSlot.SessionStrawberries = (session.Strawberries ?? new HashSet<EntityID>())
            .Select(AkronSessionEntityId.FromEntityId)
            .ToList();
        saveSlot.SessionDoNotLoad = (session.DoNotLoad ?? new HashSet<EntityID>())
            .Select(AkronSessionEntityId.FromEntityId)
            .ToList();
        saveSlot.SessionKeys = (session.Keys ?? new HashSet<EntityID>())
            .Select(AkronSessionEntityId.FromEntityId)
            .ToList();
        saveSlot.SessionSummitGems = session.SummitGems == null ? null : (bool[]) session.SummitGems.Clone();
        saveSlot.InventoryDashes = session.Inventory.Dashes;
        saveSlot.InventoryDreamDash = session.Inventory.DreamDash;
        saveSlot.InventoryBackpack = session.Inventory.Backpack;
        saveSlot.InventoryNoRefills = session.Inventory.NoRefills;
        saveSlot.SessionDashes = session.Dashes;
        saveSlot.SessionDashesAtLevelStart = session.DashesAtLevelStart;
        saveSlot.SessionDreaming = session.Dreaming;
        saveSlot.SessionStartCheckpoint = session.StartCheckpoint ?? string.Empty;
        saveSlot.SessionFurthestSeenLevel = session.FurthestSeenLevel ?? string.Empty;
        saveSlot.SessionCoreMode = session.CoreMode;
    }

    private static void RestoreCuratedSessionState(Session session, AkronSaveLoadSlot saveSlot) {
        if (session == null || saveSlot == null) {
            return;
        }

        // Persistent StartPos snapshots restore map/session gameplay state, but
        // they do not rewind stats unless the explicit SaveTimeAndDeaths setting
        // was enabled when the slot was captured.
        if (saveSlot.SaveTimeAndDeaths) {
            session.Time = saveSlot.Time;
            session.Deaths = saveSlot.Deaths;
            session.DeathsInCurrentLevel = saveSlot.DeathsInCurrentLevel;
        }

        session.Flags = new HashSet<string>(saveSlot.SessionFlags ?? new HashSet<string>());
        session.LevelFlags = new HashSet<string>(saveSlot.SessionLevelFlags ?? new HashSet<string>());
        session.Counters = (saveSlot.SessionCounters ?? new Dictionary<string, int>())
            .Select(pair => new Session.Counter {
                Key = pair.Key,
                Value = pair.Value
            })
            .ToList();
        session.Strawberries = new HashSet<EntityID>((saveSlot.SessionStrawberries ?? new List<AkronSessionEntityId>())
            .Select(id => id.ToEntityId()));
        session.DoNotLoad = new HashSet<EntityID>((saveSlot.SessionDoNotLoad ?? new List<AkronSessionEntityId>())
            .Select(id => id.ToEntityId()));
        session.Keys = new HashSet<EntityID>((saveSlot.SessionKeys ?? new List<AkronSessionEntityId>())
            .Select(id => id.ToEntityId()));
        if (saveSlot.SessionSummitGems != null) {
            session.SummitGems = (bool[]) saveSlot.SessionSummitGems.Clone();
        }
        session.Inventory = new PlayerInventory(
            saveSlot.InventoryDashes,
            saveSlot.InventoryDreamDash,
            saveSlot.InventoryBackpack,
            saveSlot.InventoryNoRefills);
        session.Dashes = saveSlot.SessionDashes;
        session.DashesAtLevelStart = saveSlot.SessionDashesAtLevelStart;
        session.Dreaming = saveSlot.SessionDreaming;
        session.StartCheckpoint = saveSlot.SessionStartCheckpoint ?? string.Empty;
        session.FurthestSeenLevel = saveSlot.SessionFurthestSeenLevel ?? string.Empty;
        session.CoreMode = saveSlot.SessionCoreMode;

        // The restored state owns the core mode now, so a Core Mode override captured before the
        // restore has nothing left to put back.
        AkronActions.ClearCoreModeRestoreSnapshot(AkronModule.Session);
    }

    private static void RepairClonedSoundSources(Level level) {
        if (level == null) {
            return;
        }

        foreach (SoundSource soundSource in AkronEntityListInternals.GetAll(level.Entities)
                     .Concat(level.Entities.ToList())
                     .SelectMany(entity => entity.Components.GetAll<SoundSource>())
                     .Distinct()) {
            if (!soundSource.Playing ||
                soundSource.InstancePlaying ||
                string.IsNullOrWhiteSpace(soundSource.EventName)) {
                continue;
            }

            // Full-level StartPos restores clone the logical component state, but
            // FMOD handles can still be dead after the live level graph is removed.
            // Replaying only components that were saved as playing keeps looped
            // object/player sounds alive without inventing new one-shot sounds.
            soundSource.Play(soundSource.EventName);
        }
    }

    private static void ReleaseDormantEventInstances(AkronSaveLoadSlot saveSlot) {
        if (saveSlot == null) {
            return;
        }

        AkronEventInstanceUtils.ReleaseDormantEventInstances(saveSlot.SavedLevelEventInstances);
        AkronEventInstanceUtils.ReleaseDormantEventInstances(saveSlot.PreClonedEventInstances);
        saveSlot.SavedLevelEventInstances = null;
        saveSlot.PreClonedEventInstances = null;
    }

    private static void ReleaseRuntimeSlotResources(AkronSaveLoadSlot saveSlot) {
        if (saveSlot == null) {
            return;
        }

        AkronVirtualAssetReloadTracker.Remove(saveSlot.TrackedVirtualAssetRegistrations);
        saveSlot.TrackedVirtualAssetRegistrations = Array.Empty<AkronTrackedVirtualAssetRegistration>();
        ReleaseDormantEventInstances(saveSlot);
    }

    private static void ReleaseFreshRuntimeBaseline(
        AkronSaveLoadSlot saveSlot,
        long capturedBytes
    ) {
        try {
            ReleaseDormantEventInstances(saveSlot);
        } finally {
            Interlocked.Add(ref retainedFreshBaselineBytes, -capturedBytes);
        }
    }

    internal static int RemoveClonedDustEdges(Level level) {
        return RemoveClonedVisualRuntimeEntities(level);
    }

    // Level.StartCutscene stores the skip callback and nothing ever clears it:
    // SkipCutscene's routine, EndCutscene, and CancelCutscene all leave
    // onCutsceneSkip pointing at the finished cutscene entity. Every slot set
    // after any skipped vanilla cutscene then drags a removed CutsceneEntity
    // into the graph through a callback that can never fire again - the skip
    // path only runs while InCutscene - and a cold load refuses the room over
    // that zombie. The dormant clone is Akron's own copy, so the dead callback
    // is dropped there; a running cutscene's callback is real state and stays.
    private static readonly FieldInfo LevelOnCutsceneSkipField = typeof(Level).GetField(
        "onCutsceneSkip",
        BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void ClearDeadCutsceneSkipCallback(Level savedLevel) {
        if (savedLevel == null || savedLevel.InCutscene) {
            return;
        }
        LevelOnCutsceneSkipField?.SetValue(savedLevel, null);
    }

    private static readonly string[] FrostHelperSpinnerRendererTypeNames = {
        "SpinnerConnectorRenderer",
        "SpinnerDecoRenderer",
        "SpinnerBorderRenderer"
    };

    internal static int RemoveClonedVisualRuntimeEntities(Level level) {
        if (level == null) {
            return 0;
        }

        int removedRenderers = RemoveVisualRuntimeRenderers(level);
        List<Entity> runtimeVisuals = GetVisualRuntimeEntities(level)
            .Distinct()
            .ToList();

        foreach (Entity entity in runtimeVisuals) {
            RemoveClonedVisualRuntimeEntity(level, entity);
        }

        level.Entities.UpdateLists();
        RebuildFrostHelperSpinnerRendererRegistrations(level);
        return runtimeVisuals.Count + removedRenderers;
    }

    private static int RemoveVisualRuntimeRenderers(Level level) {
        object rendererList = AkronLevelRenderState.RendererListField?.GetValue(level);
        if (rendererList == null) {
            return 0;
        }

        int removed = 0;
        foreach (FieldInfo field in rendererList.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            if (field.GetValue(rendererList) is not IList list) {
                continue;
            }

            for (int index = list.Count - 1; index >= 0; index--) {
                object candidate = list[index];
                if (IsVisualRuntimeObject(candidate)) {
                    list.RemoveAt(index);
                    removed++;
                }
            }
        }

        return removed;
    }

    private static IEnumerable<Entity> GetVisualRuntimeEntities(Level level) {
        return AkronEntityListInternals.GetAll(level.Entities)
            .Concat(level.Entities.ToList())
            .Concat(level.Tracker.GetEntities<DustEdges>())
            .Concat(level.Tracker.GetEntities<LightningRenderer>())
            .Concat(level.Tracker.GetEntities<MirrorSurfaces>())
            .Concat(level.Tracker.GetEntities<SeekerBarrierRenderer>())
            .Where(IsClonedVisualRuntimeEntity);
    }

    private static bool IsClonedVisualRuntimeEntity(Entity entity) {
        return entity is DustEdges ||
               entity is LightningRenderer ||
               entity is MirrorSurfaces ||
               entity is SeekerBarrierRenderer ||
               IsFrostHelperSpinnerRenderer(entity);
    }

    private static bool IsVisualRuntimeObject(object value) {
        return value is Entity entity && IsClonedVisualRuntimeEntity(entity) ||
               string.Equals(value?.GetType().Name, nameof(DustEdges), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(LightningRenderer), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(MirrorSurfaces), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(SeekerBarrierRenderer), StringComparison.Ordinal) ||
               IsFrostHelperSpinnerRenderer(value);
    }

    private static bool IsFrostHelperSpinnerRenderer(object value) {
        if (value == null || !string.Equals(value.GetType().Namespace, "FrostHelper", StringComparison.Ordinal)) {
            return false;
        }

        string typeName = value.GetType().Name;
        return FrostHelperSpinnerRendererTypeNames.Any(name => string.Equals(typeName, name, StringComparison.Ordinal));
    }

    private static void RemoveClonedVisualRuntimeEntity(Level level, Entity entity) {
        try {
            entity.Removed(level);
        } catch (NullReferenceException) {
        }

        level.TagLists.EntityRemoved(entity);
        level.Tracker.EntityRemoved(entity);
        Engine.Pooler.EntityRemoved(entity);
        AkronEntityListInternals.Remove(level.Entities, entity);
    }

    private static void RebuildFrostHelperSpinnerRendererRegistrations(Level level) {
        List<Entity> spinners = AkronEntityListInternals.GetAll(level.Entities)
            .Concat(level.Entities.ToList())
            .Where(IsFrostHelperCustomSpinner)
            .Distinct()
            .ToList();
        if (spinners.Count == 0) {
            return;
        }

        // FrostHelper's spinner border, connector, and deco renderers are tracked
        // persistent entities that cache live spinner/image references. Akron drops
        // cloned copies, then asks the live spinners to create fresh renderer
        // entities and register themselves before the next gameplay render.
        foreach (Entity spinner in spinners) {
            ResetFrostHelperSpinnerRendererRegistration(spinner);
        }
        foreach (Entity spinner in spinners) {
            InvokeFrostHelperSpinnerMethod(spinner, "CreateRenderersIfNeeded");
        }
        level.Entities.UpdateLists();
        foreach (Entity spinner in spinners) {
            InvokeFrostHelperSpinnerMethod(spinner, "RegisterToRenderers");
        }
        level.Entities.UpdateLists();
    }

    private static bool IsFrostHelperCustomSpinner(Entity entity) {
        return string.Equals(entity?.GetType().FullName, "FrostHelper.CustomSpinner", StringComparison.Ordinal);
    }

    private static void ResetFrostHelperSpinnerRendererRegistration(Entity spinner) {
        FieldInfo registeredField = spinner.GetType().GetField("RegisteredToRenderers", BindingFlags.Instance | BindingFlags.Public);
        if (registeredField?.FieldType == typeof(bool)) {
            registeredField.SetValue(spinner, false);
        }
    }

    private static void InvokeFrostHelperSpinnerMethod(Entity spinner, string methodName) {
        MethodInfo method = spinner.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        try {
            method?.Invoke(spinner, Array.Empty<object>());
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronSaveLoadService),
                "failed to rebuild FrostHelper spinner renderer via " + methodName + "; skipping. " +
                exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void UnloadLevel(Level level) {
        List<Entity> entities = new List<Entity>();
        entities.AddRange(level.Tracker.GetEntities<Player>());
        entities.AddRange(level.Entities);

        foreach (Entity entity in entities.Distinct()) {
            try {
                entity.Removed(level);
                level.TagLists.EntityRemoved(entity);
                level.Tracker.EntityRemoved(entity);
                Engine.Pooler.EntityRemoved(entity);
            } catch (NullReferenceException) {
            }
        }
    }

    private static void RunClearStateActions() {
        AkronDeepClone.ClearSharedState();
        foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
            try {
                action.ClearState?.Invoke();
            } catch (Exception exception) {
                // ClearState is an interop callback. One helper mod must not
                // abort cleanup for the remaining registrations or make a
                // committed setup import report a rollback that cannot happen.
                Logger.Log(LogLevel.Warn, nameof(AkronSaveLoadService),
                    "Registered ClearState callback failed for " + action.Id + ": " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    private static void SaveStaticMemberValues(Dictionary<Type, Dictionary<string, object>> savedValues, Type type, params string[] memberNames) {
        Dictionary<string, object> typeValues = new Dictionary<string, object>();
        foreach (string memberName in memberNames) {
            MemberInfo[] memberInfos = type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (memberInfos.Length == 0) {
                continue;
            }

            MemberInfo memberInfo = memberInfos[0];
            if (memberInfo is FieldInfo fieldInfo) {
                if (ShouldSkipStaticField(fieldInfo)) {
                    LogSkippedStaticMember(fieldInfo, "save");
                    continue;
                }

                try {
                    typeValues[memberName] = DeepClone(fieldInfo.GetValue(null));
                } catch (Exception e) {
                    LogStaticMemberFailure(fieldInfo, "save", e);
                }
            } else if (memberInfo is PropertyInfo propertyInfo && propertyInfo.CanRead) {
                if (ShouldSkipStaticProperty(propertyInfo, requireWrite: false)) {
                    LogSkippedStaticMember(propertyInfo, "save");
                    continue;
                }

                try {
                    typeValues[memberName] = DeepClone(propertyInfo.GetValue(null));
                } catch (Exception e) {
                    LogStaticMemberFailure(propertyInfo, "save", e);
                }
            }
        }

        savedValues[type] = typeValues;
    }

    private static void LoadStaticMemberValues(Dictionary<Type, Dictionary<string, object>> savedValues, Type type, params string[] memberNames) {
        if (!savedValues.TryGetValue(type, out Dictionary<string, object> typeValues)) {
            return;
        }

        foreach (string memberName in memberNames) {
            if (!typeValues.TryGetValue(memberName, out object value)) {
                continue;
            }

            MemberInfo[] memberInfos = type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (memberInfos.Length == 0) {
                continue;
            }

            MemberInfo memberInfo = memberInfos[0];
            if (memberInfo is FieldInfo fieldInfo) {
                if (ShouldSkipStaticField(fieldInfo)) {
                    LogSkippedStaticMember(fieldInfo, "restore");
                    continue;
                }

                try {
                    fieldInfo.SetValue(null, DeepClone(value));
                } catch (Exception e) {
                    LogStaticMemberFailure(fieldInfo, "restore", e);
                }
            } else if (memberInfo is PropertyInfo propertyInfo && propertyInfo.CanWrite) {
                if (ShouldSkipStaticProperty(propertyInfo, requireWrite: true)) {
                    LogSkippedStaticMember(propertyInfo, "restore");
                    continue;
                }

                try {
                    propertyInfo.SetValue(null, DeepClone(value));
                } catch (Exception e) {
                    LogStaticMemberFailure(propertyInfo, "restore", e);
                }
            }
        }
    }

    private static bool ShouldSkipStaticField(FieldInfo fieldInfo) {
        // External save/load registrations can name fields owned by other mods.
        // Readonly and literal static fields are runtime constants after type
        // initialization, so trying to restore them can crash StartPos loads.
        return fieldInfo.IsLiteral || fieldInfo.IsInitOnly || fieldInfo.IsSpecialName;
    }

    private static bool ShouldSkipStaticProperty(PropertyInfo propertyInfo, bool requireWrite) {
        return propertyInfo.IsSpecialName ||
               !propertyInfo.CanRead ||
               requireWrite && !propertyInfo.CanWrite;
    }

    private static void LogSkippedStaticMember(MemberInfo memberInfo, string operation) {
        if (AkronModule.Instance == null) {
            return;
        }

        AkronLog.Verbose(nameof(AkronSaveLoadService),
            "skipped static " + operation + " member: " + FormatStaticMemberName(memberInfo));
    }

    private static void LogStaticMemberFailure(MemberInfo memberInfo, string operation, Exception exception) {
        if (AkronModule.Instance == null) {
            return;
        }

        AkronLog.Warn(nameof(AkronSaveLoadService),
            "failed to " + operation + " static member " + FormatStaticMemberName(memberInfo) + "; skipping. " +
            exception.GetType().Name + ": " + exception.Message);
    }

    private static string FormatStaticMemberName(MemberInfo memberInfo) {
        return (memberInfo.DeclaringType?.FullName ?? "unknown") + "." + memberInfo.Name;
    }
}
