using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronSaveLoadService {
    private static readonly Dictionary<int, AkronSaveLoadSlot> Slots = new Dictionary<int, AkronSaveLoadSlot>();
    private static readonly Dictionary<string, AkronSaveLoadSlot> RuntimeSlots = new Dictionary<string, AkronSaveLoadSlot>(StringComparer.Ordinal);
    private static readonly List<AkronRegisteredSaveLoadAction> RegisteredActions = new List<AkronRegisteredSaveLoadAction>();
    private static readonly List<AkronSaveLoadRiskHandler> RiskHandlers = new List<AkronSaveLoadRiskHandler>();
    private static readonly List<Func<Type, bool>> ReturnSameObjectPredicates = new List<Func<Type, bool>>();
    private static readonly List<Func<object, object>> CustomCloneProcessors = new List<Func<object, object>>();

    public static string CurrentSlotName { get; private set; } = GetSlotName(1);

    public static void OnLevelBegin(Level level) {
        if (level != null) {
            CurrentSlotName = GetSlotName(AkronModule.Settings.ActiveSavestateSlot);
        }
    }

    public static void ClearRuntimeState() {
        RunClearStateActions();
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
        AkronRegisteredSaveLoadAction action = new AkronRegisteredSaveLoadAction(saveState, loadState, clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        RegisteredActions.Add(action);
        return action;
    }

    public static object RegisterStaticTypes(Type type, params string[] memberNames) {
        AkronRegisteredSaveLoadAction action = new AkronRegisteredSaveLoadAction(
            (savedValues, _) => SaveStaticMemberValues(savedValues, type, memberNames),
            (savedValues, _) => LoadStaticMemberValues(savedValues, type, memberNames),
            null,
            null,
            null,
            null
        );
        RegisteredActions.Add(action);
        return action;
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
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }

            AkronSaveLoadSlot saveSlot = BuildNativeSlot(level, GetSlotName(slot), AkronModule.Settings.SaveTimeAndDeaths);
            Slots[slot] = saveSlot;

            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                Dictionary<Type, Dictionary<string, object>> savedValues = new Dictionary<Type, Dictionary<string, object>>();
                action.SaveState?.Invoke(savedValues, level);
                saveSlot.ActionState[action.Id] = (Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues);
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

    public static AkronSaveLoadResult ForceUnsafeLoad(Level level, int slot) {
        if (level == null || !Slots.ContainsKey(slot)) {
            return AkronSaveLoadResult.NoState;
        }

        AkronPolicyDecision policy = AkronPolicy.CanUse(AkronFeatureKind.UnsafeNativeSavestateOverride);
        if (!policy.Allowed) {
            return AkronSaveLoadResult.Blocked;
        }

        AkronSaveLoadSlot saveSlot = Slots[slot];
        if (!RestoreNativeSlot(level, saveSlot)) {
            return AkronSaveLoadResult.SessionMismatch;
        }
        PrepareSlotPreClone(saveSlot);
        AkronPolicy.RecordFeatureUse(AkronFeatureKind.UnsafeNativeSavestateOverride);
        return AkronSaveLoadResult.Success;
    }

    public static AkronSaveLoadSlot CaptureRuntimeState(Level level, string slotName, bool saveTimeAndDeaths) {
        if (level == null || !CanAccessNativeState(level, out _)) {
            return null;
        }

        CurrentSlotName = string.IsNullOrWhiteSpace(slotName) ? "StartPos" : slotName;
        AkronLevelRenderState renderState = AkronLevelRenderState.Capture(level);
        IgnoreVisualRuntimeEntities(level);
        AkronIgnoreSaveStateComponent.RemoveAll(level);
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeSaveState?.Invoke(level);
                action.PreCloneEntities?.Invoke();
            }

            // StartPos needs full-state semantics: the room is cloned as a whole,
            // then restored as a whole. A player-only snapshot cannot preserve
            // collected objects, entity cycles, triggers, or room-local runtime
            // state accurately enough for practice starts.
            AkronSaveLoadSlot saveSlot = BuildNativeSlot(level, CurrentSlotName, saveTimeAndDeaths, includeLevelSnapshot: true);
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                Dictionary<Type, Dictionary<string, object>> savedValues = new Dictionary<Type, Dictionary<string, object>>();
                action.SaveState?.Invoke(savedValues, level);
                saveSlot.ActionState[action.Id] = (Dictionary<Type, Dictionary<string, object>>) DeepClone(savedValues);
            }
            PrepareSlotPreClone(saveSlot);

            return saveSlot;
        } finally {
            AkronDeepClone.ClearSharedState();
            AkronIgnoreSaveStateComponent.ReAddAll(level);
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
                RuntimeSlots.Remove(normalizedSlotName);
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

        RuntimeSlots[normalizedSlotName] = saveSlot;
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

        AkronIgnoreSaveStateComponent.RemoveAll(level);
        try {
            foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
                action.BeforeLoadState?.Invoke(level);
            }

            if (!RestoreNativeSlot(level, saveSlot, restoreAkronModuleState: false)) {
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

        return AkronSaveLoadResult.Success;
    }

    public static AkronSaveLoadResult LoadRuntimeState(Level level, string slotName, bool allowDeadPlayer = false) {
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

        return RuntimeSlots.TryGetValue(normalizedSlotName, out AkronSaveLoadSlot saveSlot)
            ? RestoreRuntimeState(level, saveSlot, allowDeadPlayer)
            : AkronSaveLoadResult.NoState;
    }

    public static bool HasRuntimeState(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        return RuntimeSlots.ContainsKey(normalizedSlotName) ||
               ShouldBrokerRuntimeState(normalizedSlotName) && AkronSpeedrunToolBroker.IsSaved(normalizedSlotName);
    }

    public static AkronSaveLoadSlot GetRuntimeStateForDebug(string slotName) {
        RuntimeSlots.TryGetValue(NormalizeRuntimeSlotName(slotName), out AkronSaveLoadSlot saveSlot);
        return saveSlot;
    }

    public static void ClearRuntimeState(string slotName) {
        string normalizedSlotName = NormalizeRuntimeSlotName(slotName);
        AkronSpeedrunToolBroker.Clear(normalizedSlotName);
        if (RuntimeSlots.Remove(normalizedSlotName)) {
            RunClearStateActions();
        }
    }

    public static bool HasSlot(int slot) {
        if (ShouldBrokerSavestatesInsteadOfNative()) {
            return AkronSpeedrunToolBroker.IsSaved(slot);
        }

        return Slots.ContainsKey(slot);
    }

    public static void ClearSlot(int slot) {
        if (Slots.Remove(slot)) {
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
               !slotName.StartsWith("Akron StartPos ", StringComparison.Ordinal);
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

        saveSlot.SessionNonce = AkronModule.Session.CurrentSessionNonce;
        if (includeLevelSnapshot) {
            saveSlot.SavedLevel = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
            AkronDeepClone.CopyInto(level, saveSlot.SavedLevel);
        }
        saveSlot.SessionState = (Session) DeepClone(level.Session);
        if (SaveData.Instance != null) {
            saveSlot.SaveDataState = (SaveData) DeepClone(SaveData.Instance);
        }

        if (player != null) {
            saveSlot.PlayerPosition = player.Position;
            saveSlot.PlayerSpeed = player.Speed;
            saveSlot.Stamina = player.Stamina;
            saveSlot.Dashes = player.Dashes;
            saveSlot.Facing = player.Facing;
        }

        saveSlot.RespawnPoint = level.Session.RespawnPoint;
        saveSlot.Time = level.Session.Time;
        saveSlot.Deaths = level.Session.Deaths;
        saveSlot.DeathsInCurrentLevel = level.Session.DeathsInCurrentLevel;
        saveSlot.FileSlot = SaveData.Instance?.FileSlot ?? -1;
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
    }

    private static void PrepareSlotPreClone(AkronSaveLoadSlot saveSlot) {
        AkronDeepClone.ClearSharedState();
        saveSlot.PreCloneState = AkronDeepClone.CreateSharedEntityState(saveSlot);
    }

    private static bool RestoreNativeSlot(Level level, AkronSaveLoadSlot saveSlot, bool restoreAkronModuleState = true) {
        if (level.Session.Area.GetSID() != saveSlot.MapSid ||
            (SaveData.Instance?.FileSlot ?? -1) != saveSlot.FileSlot ||
            !string.Equals(saveSlot.SessionNonce, AkronModule.Session.CurrentSessionNonce, StringComparison.Ordinal)) {
            return false;
        }

        AkronDeepClone.SetSharedState(saveSlot.PreCloneState);
        Level savedLevel = saveSlot.SavedLevel;
        Session savedSession = savedLevel?.Session ?? (saveSlot.SessionState != null ? (Session) DeepClone(saveSlot.SessionState) : null);
        SaveData savedSaveData = saveSlot.SaveDataState != null ? (SaveData) DeepClone(saveSlot.SaveDataState) : null;
        long currentSessionTime = level.Session.Time;
        int currentDeaths = level.Session.Deaths;
        int currentDeathsInRoom = level.Session.DeathsInCurrentLevel;
        long currentSaveDataTime = SaveData.Instance?.Time ?? 0L;
        int currentTotalDeaths = SaveData.Instance?.TotalDeaths ?? 0;
        float currentLevelTimeActive = level.TimeActive;
        float currentLevelRawTimeActive = level.RawTimeActive;
        AreaKey currentAreaKey = level.Session.Area;
        long currentAreaTimePlayed = SaveData.Instance?.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].TimePlayed ?? 0L;
        int currentAreaDeaths = SaveData.Instance?.Areas_Safe[currentAreaKey.ID].Modes[(int) currentAreaKey.Mode].Deaths ?? 0;

        if (savedSession != null && !saveSlot.SaveTimeAndDeaths) {
            savedSession.Time = Math.Max(currentSessionTime, savedSession.Time);
            savedSession.Deaths = Math.Max(currentDeaths, savedSession.Deaths);
            savedSession.DeathsInCurrentLevel = Math.Max(currentDeathsInRoom, savedSession.DeathsInCurrentLevel);
        }

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
            AkronDeepClone.CopyInto(savedLevel, level);
            AkronLevelGraphRepair.RelinkEntitiesToLevel(level);
            AkronLevelRenderState.RelinkRendererCameras(level);
            RemoveClonedVisualRuntimeEntities(level);
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
            }

            if (roomChanged) {
                level.Tracker.GetEntitiesCopy<Player>().ForEach(entity => entity.RemoveSelf());
                level.UnloadLevel();
                level.Completed = false;
                level.InCutscene = false;
                level.SkippingCutscene = false;
                level.LoadLevel(Player.IntroTypes.Respawn);
                level.Entities.UpdateLists();
            }
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player != null && savedLevel == null) {
            player.Position = saveSlot.PlayerPosition;
            player.Speed = saveSlot.PlayerSpeed;
            player.Stamina = saveSlot.Stamina;
            player.Dashes = saveSlot.Dashes;
            player.Facing = saveSlot.Facing;
        }

        if (savedSaveData != null) {
            SaveData.Instance = savedSaveData;
        }

        if (saveSlot.SaveTimeAndDeaths) {
            level.TimeActive = saveSlot.LevelTimeActive;
            level.RawTimeActive = saveSlot.LevelRawTimeActive;
        } else {
            level.TimeActive = Math.Max(currentLevelTimeActive, saveSlot.LevelTimeActive);
            level.RawTimeActive = Math.Max(currentLevelRawTimeActive, saveSlot.LevelRawTimeActive);
        }

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
            if (saveSlot.ModuleSaveData.TryGetValue(key, out EverestModuleSaveData moduleSaveData)) {
                module._SaveData = (EverestModuleSaveData) DeepClone(moduleSaveData);
            }
        }

        return true;
    }

    internal static int RemoveClonedDustEdges(Level level) {
        return RemoveClonedVisualRuntimeEntities(level);
    }

    private static void IgnoreVisualRuntimeEntities(Level level) {
        if (level == null) {
            return;
        }

        foreach (Entity entity in GetVisualRuntimeEntities(level)) {
            IgnoreSaveState(entity);
        }
    }

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
               entity is SeekerBarrierRenderer;
    }

    private static bool IsVisualRuntimeObject(object value) {
        return value is Entity entity && IsClonedVisualRuntimeEntity(entity) ||
               string.Equals(value?.GetType().Name, nameof(DustEdges), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(LightningRenderer), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(MirrorSurfaces), StringComparison.Ordinal) ||
               string.Equals(value?.GetType().Name, nameof(SeekerBarrierRenderer), StringComparison.Ordinal);
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
        AkronVirtualAssetReloadTracker.Clear();
        AkronDeepClone.ClearSharedState();
        foreach (AkronRegisteredSaveLoadAction action in RegisteredActions) {
            action.ClearState?.Invoke();
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
