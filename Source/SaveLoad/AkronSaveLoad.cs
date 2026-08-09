using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using FMOD.Studio;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronSaveLoadService {
    private const int MaxFreshRoomEntityListDrainPasses = 64;
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
    private static readonly Dictionary<int, AkronSaveLoadSlot> Slots = new Dictionary<int, AkronSaveLoadSlot>();
    private static readonly Dictionary<string, AkronSaveLoadSlotOwner> RuntimeSlots = new Dictionary<string, AkronSaveLoadSlotOwner>(StringComparer.Ordinal);
    private static readonly List<AkronRegisteredSaveLoadAction> RegisteredActions = new List<AkronRegisteredSaveLoadAction>();
    private static readonly List<AkronSaveLoadRiskHandler> RiskHandlers = new List<AkronSaveLoadRiskHandler>();
    private static readonly List<Func<Type, bool>> ReturnSameObjectPredicates = new List<Func<Type, bool>>();
    private static readonly List<Func<object, object>> CustomCloneProcessors = new List<Func<object, object>>();

    public static string LastPersistentSnapshotError { get; private set; } = string.Empty;

    public static string CurrentSlotName { get; private set; } = GetSlotName(1);

    public static void OnLevelBegin(Level level) {
        if (level != null) {
            CurrentSlotName = GetSlotName(AkronModule.Settings.ActiveSavestateSlot);
        }
    }

    public static void ClearRuntimeState() {
        AkronStartPosReconstruction.ReleaseOwnedResources();
        AkronStartPosPersistence.ClearRuntimeFreshBaselines();
        RunClearStateActions();
        foreach (AkronSaveLoadSlot saveSlot in Slots.Values.Distinct()) {
            ReleaseDormantEventInstances(saveSlot);
        }
        foreach (AkronSaveLoadSlotOwner runtimeSlot in RuntimeSlots.Values.Distinct()) {
            runtimeSlot.ReleaseOwnership();
        }
        // Per-slot release removes owned registrations. This final reset also
        // clears any stale generation left after a live asset reload.
        AkronVirtualAssetReloadTracker.Clear();
        RegisteredActions.Clear();
        RiskHandlers.Clear();
        ReturnSameObjectPredicates.Clear();
        CustomCloneProcessors.Clear();
        Slots.Clear();
        RuntimeSlots.Clear();
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
        } else if (obj is AkronSaveLoadRiskHandler handler) {
            RiskHandlers.Remove(handler);
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

        if (ShouldBrokerSavestatesInsteadOfNative()) {
            return TryBrokerSave(slot);
        }

        if (!CanAccessNativeState(level, out _)) {
            return AkronSaveLoadResult.Blocked;
        }

        bool isRisky = IsRisky(level, slot, out _);
        bool usedUnsafeNativeOverride = isRisky && TryUseUnsafeNativeOverride(level);
        if (isRisky && !usedUnsafeNativeOverride) {
            return TryBrokerSave(slot);
        }

        AkronIgnoreSaveStateComponent.RemoveAll(level);
        AkronSaveLoadSlot capturedSlot = null;
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }

            capturedSlot = BuildNativeSlot(level, GetSlotName(slot), AkronModule.Settings.SaveTimeAndDeaths);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                CaptureRegisteredActionState(capturedSlot, action, level);
            }
            PrepareSlotPreClone(capturedSlot);
            if (Slots.TryGetValue(slot, out AkronSaveLoadSlot previousSlot)) {
                ReleaseDormantEventInstances(previousSlot);
            }
            Slots[slot] = capturedSlot;
            capturedSlot = null;
        } catch {
            ReleaseDormantEventInstances(capturedSlot);
            throw;
        } finally {
            AkronDeepClone.ClearSharedState();
            AkronIgnoreSaveStateComponent.ReAddAll(level);
        }

        if (!usedUnsafeNativeOverride) {
            AkronPolicy.RecordFeatureUse(AkronFeatureKind.Savestates);
        }
        if (AkronModule.Settings.ProofModeOverlay) {
            AkronProof.WriteSidecar(level, "startpos-capture");
        }
        return AkronSaveLoadResult.Success;
    }

    public static AkronSaveLoadResult Load(Level level, int slot) {
        if (level == null) {
            return AkronSaveLoadResult.Failed;
        }

        CurrentSlotName = GetSlotName(slot);

        if (ShouldBrokerSavestatesInsteadOfNative()) {
            return TryBrokerLoad(level, slot);
        }

        if (!Slots.TryGetValue(slot, out AkronSaveLoadSlot saveSlot)) {
            return AkronSaveLoadResult.NoState;
        }

        AkronPolicyDecision policy = AkronPolicy.CanUse(AkronFeatureKind.Savestates);
        if (!policy.Allowed) {
            return AkronSaveLoadResult.Blocked;
        }

        if (!CanAccessNativeState(level, out _)) {
            return AkronSaveLoadResult.Blocked;
        }
        if (!MatchesCurrentNativeSession(level, saveSlot)) {
            return AkronSaveLoadResult.SessionMismatch;
        }

        bool isRisky = IsRisky(level, slot, out _);
        bool usedUnsafeNativeOverride = isRisky && TryUseUnsafeNativeOverride(level);
        if (isRisky && !usedUnsafeNativeOverride) {
            return TryBrokerLoad(level, slot);
        }

        AkronIgnoreSaveStateComponent.RemoveAll(level);
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeLoadState?.Invoke(level);
            }

            if (!RestoreNativeSlot(level, saveSlot)) {
                return AkronSaveLoadResult.SessionMismatch;
            }

            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                if (saveSlot.ActionState.TryGetValue(action.Id, out Dictionary<Type, Dictionary<string, object>> savedValues)) {
                    action.LoadState?.Invoke((Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues), level);
                }
            }
            PrepareSlotPreClone(saveSlot);
        } finally {
            AkronDeepClone.ClearSharedState();
            AkronIgnoreSaveStateComponent.ReAddAll(level);
        }

        if (!usedUnsafeNativeOverride) {
            AkronPolicy.RecordFeatureUse(AkronFeatureKind.Savestates);
        }
        if (AkronModule.Settings.ProofModeOverlay) {
            AkronProof.WriteSidecar(level, "startpos-restore");
        }
        return AkronSaveLoadResult.Success;
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
        AkronLevelRenderState renderState = AkronLevelRenderState.Capture(level);
        List<AkronGameplayBufferSnapshot> gameplayBuffers = new List<AkronGameplayBufferSnapshot>();
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> persistentRenderTargets =
            new Dictionary<object, AkronReconstructionResourcePayload>();
        if (CurrentSlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal)) {
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
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }

            // StartPos needs full-state semantics: the room is cloned as a whole,
            // then restored as a whole. A player-only snapshot cannot preserve
            // collected objects, entity cycles, triggers, or room-local runtime
            // state accurately enough for practice starts.
            saveSlot = BuildNativeSlot(level, CurrentSlotName, saveTimeAndDeaths, includeLevelSnapshot: true);
            saveSlot.GameplayBuffers = gameplayBuffers;
            saveSlot.PersistentRenderTargets = persistentRenderTargets;
            AkronIgnoreSaveStateComponent.RemoveAllFromSnapshot(saveSlot.SavedLevel);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                CaptureRegisteredActionState(saveSlot, action, level);
            }
            if (prepareForRestore) {
                PrepareSlotPreClone(saveSlot);
            }
            if (capturePersistentResources &&
                CurrentSlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal)) {
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
                }
                AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
                return AkronSaveLoadResult.Success;
            }
            if (brokerResult != AkronSaveLoadResult.BrokerUnavailable) {
                return brokerResult;
            }
        }

        AkronSaveLoadSlot saveSlot = CaptureRuntimeState(level, normalizedSlotName, saveTimeAndDeaths);
        if (saveSlot == null) {
            return AkronSaveLoadResult.Blocked;
        }

        StoreRuntimeSlot(normalizedSlotName, saveSlot);
        AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
        return AkronSaveLoadResult.Success;
    }

    private static void StoreRuntimeSlot(string slotName, AkronSaveLoadSlot saveSlot) {
        AkronStartPosPersistence.RemoveRuntimeFreshBaseline(slotName);
        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(saveSlot, ReleaseRuntimeSlotResources);
        if (RuntimeSlots.Remove(slotName, out AkronSaveLoadSlotOwner previousSlot)) {
            previousSlot.ReleaseOwnership();
        }
        RuntimeSlots[slotName] = owner;
    }

    internal static AkronSaveLoadSlotLease CaptureFreshRuntimeState(Level level, string slotName) {
        if (level == null) {
            return null;
        }

        CurrentSlotName = string.IsNullOrWhiteSpace(slotName) ? "fresh baseline" : slotName;
        AkronLevelRenderState renderState = AkronLevelRenderState.Capture(level);
        int virtualAssetMarker = AkronVirtualAssetReloadTracker.Mark();
        ScreenWipe entryWipe = level.Wipe;
        List<Renderer> renderers = level.RendererList?.Renderers;
        int entryWipeRendererIndex = entryWipe == null || renderers == null
            ? -1
            : renderers.IndexOf(entryWipe);
        if (entryWipe != null) {
            // The entry wipe is a transition owned by the current process, not a
            // stable room object. Exclude it from both warm and cold baselines so
            // their graph boundary stays identical after one initialization update.
            level.Wipe = null;
            if (entryWipeRendererIndex >= 0) {
                level.RendererList.Renderers.RemoveAt(entryWipeRendererIndex);
            }
        }
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
        } catch {
            ReleaseDormantEventInstances(saveSlot);
            throw;
        } finally {
            AkronVirtualAssetReloadTracker.DiscardSince(virtualAssetMarker);
            AkronDeepClone.ClearSharedState();
            renderState.Restore(level);
            if (entryWipe != null) {
                level.Wipe = entryWipe;
                if (entryWipeRendererIndex >= 0 && !renderers.Contains(entryWipe)) {
                    level.RendererList.Renderers.Insert(
                        Math.Min(entryWipeRendererIndex, level.RendererList.Renderers.Count),
                        entryWipe);
                }
            }
        }

        AkronSaveLoadSlotOwner owner = new AkronSaveLoadSlotOwner(saveSlot, ReleaseDormantEventInstances);
        AkronSaveLoadSlotLease lease = owner.Retain();
        owner.ReleaseOwnership();
        return lease;
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

    public static AkronSaveLoadResult RestoreRuntimeState(Level level, AkronSaveLoadSlot saveSlot, bool allowDeadPlayer = false) {
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

        bool suppressLagPauserForStartPos = saveSlot.SlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
        if (suppressLagPauserForStartPos) {
            AkronModule.SuppressLagPauserForNativeStartPosRestore();
        }
        AkronModule.SuppressAkronRenderSurfacesAfterStateTransition();
        AkronIgnoreSaveStateComponent.RemoveAll(level);
        try {
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
            AkronStartPosPersistence.UseRuntimeFreshBaseline(saveSlot.SlotName);
            // Berry progress is persistent save data. Apply it only after the
            // remaining restore work can no longer report a normal failure.
            if (saveSlot.BerryProgress != null &&
                !saveSlot.BerryProgress.TryRestore(level, out string berryRestoreError)) {
                LastPersistentSnapshotError = berryRestoreError;
                return AkronSaveLoadResult.Failed;
            }
            AkronGameplayBufferState.ArmLevelPresentation(level, saveSlot.GameplayBuffers);
        } finally {
            AkronDeepClone.ClearSharedState();
            AkronIgnoreSaveStateComponent.ReAddAll(level);
            if (suppressLagPauserForStartPos) {
                AkronModule.SuppressLagPauserForNativeStartPosRestore();
            }
        }

        return AkronSaveLoadResult.Success;
    }

    public static AkronSaveLoadResult LoadRuntimeState(Level level, string slotName, bool allowDeadPlayer = false) {
        LastPersistentSnapshotError = string.Empty;
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
            return AkronSaveLoadResult.NoState;
        }
        return AkronStartPosReconstruction.HasSnapshot(normalizedSlotName)
            ? RestorePersistentRuntimeState(level, normalizedSlotName, allowDeadPlayer)
            : AkronSaveLoadResult.NoState;
    }

    internal static bool HasRuntimeStateInMemory(string slotName) {
        return RuntimeSlots.ContainsKey(NormalizeRuntimeSlotName(slotName));
    }

    private static AkronSaveLoadResult RestorePersistentRuntimeState(
        Level level,
        string slotName,
        bool allowDeadPlayer
    ) {
        if (!CanAccessNativeState(level, out _, allowDeadPlayer)) {
            return AkronSaveLoadResult.Blocked;
        }
        if (!AkronStartPosReconstruction.TryLoadSnapshot(slotName, out AkronReconstructionDocument document, out string loadError)) {
            LastPersistentSnapshotError = loadError;
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
                LastPersistentSnapshotError = exception.GetType().Name + ": " + exception.Message;
                restoreResult = AkronSaveLoadResult.Failed;
            } finally {
                AkronDeepClone.ClearSharedState();
                AkronIgnoreSaveStateComponent.ReAddAll(level);
            }

            if (restoreResult == AkronSaveLoadResult.Success) {
                AkronSaveLoadResult cacheResult = CacheRestoredRuntimeState(level, slotName, freshBaseline);
                if (cacheResult != AkronSaveLoadResult.Success) {
                    Logger.Log(LogLevel.Warn, nameof(AkronSaveLoadService),
                        "Restored StartPos could not be cached; later Loads will use the cold path. " +
                        LastPersistentSnapshotError);
                }
                return AkronSaveLoadResult.Success;
            }

            string persistentFailure = LastPersistentSnapshotError;
            AkronSaveLoadResult rollbackResult = RestoreRuntimeState(level, rollbackSlot, allowDeadPlayer: true);
            LastPersistentSnapshotError = rollbackResult == AkronSaveLoadResult.Success
                ? persistentFailure
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
            AkronStartPosPersistence.AttachRuntimeFreshBaseline(slotName, freshBaseline);
            AkronStartPosPersistence.UseRuntimeFreshBaseline(slotName);
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
            "Akron restored fresh-room baseline " + document.MapSid + "|" + document.Room);
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
            LastPersistentSnapshotError = "registered action state " + actionRestore.Error;
            AkronDeepClone.ClearSharedState();
            return AkronSaveLoadResult.Failed;
        }
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
        }
    }

    private static AkronSaveLoadResult RestorePersistentRuntimeStateAfterActionState(
        Level level,
        AkronReconstructionDocument document,
        AkronCumulativeStats cumulativeStats,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshActionState,
        AkronReconstructionRestore actionRestore
    ) {
        AkronReconstructionVerification actionVerification = AkronStartPosReconstruction.Verify(
            document.ActionStateDocument,
            actionRestore,
            Array.Empty<string>());
        if (!actionVerification.Success) {
            LastPersistentSnapshotError = "registered action state " + actionVerification.Error;
            AkronDeepClone.ClearSharedState();
            return AkronSaveLoadResult.Failed;
        }

        AkronPersistentRuntimeState freshRuntimeState = AkronPersistentRuntimeState.CaptureCurrent(level);
        AkronReconstructionRestore restore = AkronStartPosReconstruction.Restore(document, freshRuntimeState);
        if (!restore.Success) {
            LastPersistentSnapshotError = restore.Error;
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
            LastPersistentSnapshotError = reapply.Error;
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
            LastPersistentSnapshotError = verification.Error;
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
        AkronStartPosPersistence.RemoveRuntimeFreshBaseline(normalizedSlotName);
        if (RuntimeSlots.Remove(normalizedSlotName, out AkronSaveLoadSlotOwner removedSlot)) {
            removedSlot.ReleaseOwnership();
            RunClearStateActions();
        }
    }

    internal static void DiscardRuntimeStateMemory(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        AkronStartPosPersistence.RemoveRuntimeFreshBaseline(normalizedSlotName);
        if (RuntimeSlots.Remove(normalizedSlotName, out AkronSaveLoadSlotOwner removedSlot)) {
            removedSlot.ReleaseOwnership();
            RunClearStateActions();
        }
    }

    internal static void ClearRuntimeStateExceptPersistentSnapshot(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        AkronSpeedrunToolBroker.Clear(normalizedSlotName);
        DiscardRuntimeStateMemory(normalizedSlotName);
    }

    public static bool HasSlot(int slot) {
        if (ShouldBrokerSavestatesInsteadOfNative()) {
            return AkronSpeedrunToolBroker.IsSaved(slot);
        }

        return Slots.ContainsKey(slot);
    }

    public static void ClearSlot(int slot) {
        if (Slots.Remove(slot, out AkronSaveLoadSlot removedSlot)) {
            ReleaseDormantEventInstances(removedSlot);
            RunClearStateActions();
        }
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
        // visual state on the next room warp. Keep StartPos on Akron's native
        // runtime clone path while preserving broker behavior for normal slots.
        return ShouldBrokerSavestatesInsteadOfNative() &&
               !slotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal);
    }

    public static void RegisterRiskHandler(AkronSaveLoadRiskHandler handler) {
        if (handler != null && !RiskHandlers.Contains(handler)) {
            RiskHandlers.Add(handler);
        }
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

    public static bool ShouldPromptForBroker(Level level, int slot, out string reason) {
        if (level == null || !AkronSpeedrunToolBroker.Available) {
            reason = string.Empty;
            return false;
        }

        if (!CanAccessNativeState(level, out reason)) {
            return false;
        }

        if (!AkronModule.Settings.SpeedrunToolBrokerWarnings || AkronMapOverrides.ShouldForceBroker(level)) {
            reason = string.Empty;
            return false;
        }

        if (IsUnsafeNativeOverrideEnabled(level) && CanUseUnsafeNativeOverride(level)) {
            reason = string.Empty;
            return false;
        }

        return IsRisky(level, slot, out reason);
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
            saveSlot.SavedLevel = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
            saveSlot.SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant(level, saveSlot.SavedLevel);
            saveSlot.FileSlot = SaveData.Instance?.FileSlot ?? -1;
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

    internal static int RemoveClonedDustEdges(Level level) {
        return RemoveClonedVisualRuntimeEntities(level);
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
