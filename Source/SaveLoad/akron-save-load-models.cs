using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using FMOD.Studio;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public enum AkronSaveLoadResult {
    Success,
    Blocked,
    NoState,
    SessionMismatch,
    BrokerUnavailable,
    Failed
}

public delegate void AkronInteropSaveLoadAction(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);

internal sealed class AkronRegisteredSaveLoadAction {
    public AkronRegisteredSaveLoadAction(
        string id,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        Id = id;
        SaveState = saveState;
        LoadState = loadState;
        ClearState = clearState;
        BeforeSaveState = beforeSaveState;
        BeforeLoadState = beforeLoadState;
        PreCloneEntities = preCloneEntities;
    }

    public string Id { get; }
    public Action<Dictionary<Type, Dictionary<string, object>>, Level> SaveState { get; }
    public Action<Dictionary<Type, Dictionary<string, object>>, Level> LoadState { get; }
    public Action ClearState { get; }
    public Action<Level> BeforeSaveState { get; }
    public Action<Level> BeforeLoadState { get; }
    public Action PreCloneEntities { get; }
}

public sealed class AkronSaveLoadSlot {
    public AkronSaveLoadSlot(string slotName, string levelName, string mapSid, bool saveTimeAndDeaths) {
        SlotName = slotName;
        LevelName = levelName;
        MapSid = mapSid;
        SaveTimeAndDeaths = saveTimeAndDeaths;
        CreatedAtUtc = DateTime.UtcNow;
        ActionState = new Dictionary<string, Dictionary<Type, Dictionary<string, object>>>();
        ModuleSessions = new Dictionary<string, EverestModuleSession>();
        ModuleSaveData = new Dictionary<string, EverestModuleSaveData>();
    }

    public string SlotName { get; }
    public string LevelName { get; set; }
    public string MapSid { get; }
    public bool SaveTimeAndDeaths { get; }
    public DateTime CreatedAtUtc { get; }
    public string SessionNonce { get; set; }
    public Level SavedLevel { get; set; }
    public Session SessionState { get; set; }
    public SaveData SaveDataState { get; set; }
    internal DeepCloneState PreCloneState { get; set; }
    internal List<EventInstance> SavedLevelEventInstances { get; set; }
    internal List<EventInstance> PreClonedEventInstances { get; set; }
    public Vector2 PlayerPosition { get; set; }
    public Vector2 PlayerSpeed { get; set; }
    public int PlayerState { get; set; }
    public float Stamina { get; set; }
    public int Dashes { get; set; }
    public Facings Facing { get; set; }
    public Vector2? RespawnPoint { get; set; }
    public long Time { get; set; }
    public int Deaths { get; set; }
    public int DeathsInCurrentLevel { get; set; }
    public int FileSlot { get; set; }
    public long SaveDataTime { get; set; }
    public int SaveDataTotalDeaths { get; set; }
    public long AreaTimePlayed { get; set; }
    public int AreaDeaths { get; set; }
    internal AkronBerryProgressSnapshot BerryProgress { get; set; }
    public float LevelTimeActive { get; set; }
    public float LevelRawTimeActive { get; set; }
    public GrabModes GrabMode { get; set; }
    public CrouchDashModes CrouchDashMode { get; set; }
    public float EngineTimeRate { get; set; }
    public float GlitchValue { get; set; }
    public float DistortAnxiety { get; set; }
    public float DistortGameRate { get; set; }
    public Dictionary<string, Dictionary<Type, Dictionary<string, object>>> ActionState { get; }
    internal List<AkronGameplayBufferSnapshot> GameplayBuffers { get; set; } = new List<AkronGameplayBufferSnapshot>();
    internal IReadOnlyDictionary<object, AkronReconstructionResourcePayload> PersistentRenderTargets { get; set; } =
        new Dictionary<object, AkronReconstructionResourcePayload>();
    internal IReadOnlyDictionary<object, string> HookOwnerRegistrations { get; set; }
    internal IReadOnlyList<AkronTrackedVirtualAssetRegistration> TrackedVirtualAssetRegistrations { get; set; } =
        Array.Empty<AkronTrackedVirtualAssetRegistration>();
    public Dictionary<string, EverestModuleSession> ModuleSessions { get; }
    public Dictionary<string, EverestModuleSaveData> ModuleSaveData { get; }
    public HashSet<string> SessionFlags { get; set; } = new HashSet<string>();
    public HashSet<string> SessionLevelFlags { get; set; } = new HashSet<string>();
    public Dictionary<string, int> SessionCounters { get; set; } = new Dictionary<string, int>();
    public List<AkronSessionEntityId> SessionStrawberries { get; set; } = new List<AkronSessionEntityId>();
    public List<AkronSessionEntityId> SessionDoNotLoad { get; set; } = new List<AkronSessionEntityId>();
    public List<AkronSessionEntityId> SessionKeys { get; set; } = new List<AkronSessionEntityId>();
    public bool[] SessionSummitGems { get; set; }
    public int InventoryDashes { get; set; }
    public bool InventoryDreamDash { get; set; }
    public bool InventoryBackpack { get; set; }
    public bool InventoryNoRefills { get; set; }
    public int SessionDashes { get; set; }
    public int SessionDashesAtLevelStart { get; set; }
    public bool SessionDreaming { get; set; }
    public string SessionStartCheckpoint { get; set; } = string.Empty;
    public string SessionFurthestSeenLevel { get; set; } = string.Empty;
    public Session.CoreModes SessionCoreMode { get; set; }
}

// StartPos restores the active chapter's berry progress without replacing the
// whole SaveData object. Replacing all SaveData would also rewind progress the
// player earned in other chapters after placing this StartPos.
internal sealed class AkronBerryProgressSnapshot {
    public List<AkronSessionEntityId> Strawberries { get; set; } = new List<AkronSessionEntityId>();
    public int TotalStrawberries { get; set; }

    public static AkronBerryProgressSnapshot Capture(Level level) {
        return Capture(GetAreaModeStats(level));
    }

    internal static AkronBerryProgressSnapshot Capture(AreaModeStats areaStats) {
        return areaStats == null
            ? null
            : new AkronBerryProgressSnapshot {
                Strawberries = (areaStats.Strawberries ?? new HashSet<EntityID>())
                    .Select(AkronSessionEntityId.FromEntityId)
                    .ToList(),
                // Golden berry IDs are collected, but Celeste intentionally
                // excludes them from the regular strawberry totals.
                TotalStrawberries = areaStats.TotalStrawberries
            };
    }

    public bool TryRestore(Level level, out string error) {
        error = string.Empty;
        if (SaveData.Instance == null) {
            return true;
        }

        AreaModeStats areaStats = GetAreaModeStats(level);
        if (!TryRestore(
                areaStats,
                SaveData.Instance.TotalStrawberries_Safe,
                out int restoredTotal,
                out error)) {
            return false;
        }

        SaveData.Instance.TotalStrawberries_Safe = restoredTotal;
        return true;
    }

    internal bool TryRestore(
        AreaModeStats areaStats,
        int currentTotal,
        out int restoredTotal,
        out string error
    ) {
        restoredTotal = currentTotal;
        error = string.Empty;
        if (areaStats == null) {
            error = "active map berry statistics are unavailable";
            return false;
        }
        if (currentTotal < 0 || areaStats.TotalStrawberries < 0 ||
            currentTotal < areaStats.TotalStrawberries) {
            error = "current berry statistics are inconsistent";
            return false;
        }

        List<AkronSessionEntityId> savedStrawberries = Strawberries ?? new List<AkronSessionEntityId>();
        if (savedStrawberries.Any(id => id == null)) {
            error = "saved berry identifier is missing";
            return false;
        }
        if (TotalStrawberries < 0 || TotalStrawberries > savedStrawberries.Count) {
            error = "saved berry total is inconsistent with its identifiers";
            return false;
        }
        HashSet<(string Level, int ID)> restoredIds = new HashSet<(string Level, int ID)>();
        List<EntityID> restoredStrawberries = new List<EntityID>(savedStrawberries.Count);
        foreach (AkronSessionEntityId savedStrawberry in savedStrawberries) {
            string level = savedStrawberry.Level ?? string.Empty;
            if (!restoredIds.Add((level, savedStrawberry.ID))) {
                error = "saved berry identifiers contain duplicates";
                return false;
            }
            restoredStrawberries.Add(new EntityID(level, savedStrawberry.ID));
        }

        long totalOutsideActiveMap = (long) currentTotal - areaStats.TotalStrawberries;
        long restoredTotalLong = totalOutsideActiveMap + TotalStrawberries;
        if (restoredTotalLong < 0 || restoredTotalLong > int.MaxValue) {
            error = "restored berry total is outside the supported range";
            return false;
        }

        areaStats.Strawberries ??= new HashSet<EntityID>();
        areaStats.Strawberries.Clear();
        areaStats.Strawberries.UnionWith(restoredStrawberries);
        areaStats.TotalStrawberries = TotalStrawberries;
        restoredTotal = (int) restoredTotalLong;
        return true;
    }

    private static AreaModeStats GetAreaModeStats(Level level) {
        AreaKey? area = level?.Session?.Area;
        if (!area.HasValue || SaveData.Instance?.Areas_Safe == null ||
            area.Value.ID < 0 || area.Value.ID >= SaveData.Instance.Areas_Safe.Count) {
            return null;
        }

        AreaStats areaStats = SaveData.Instance.Areas_Safe[area.Value.ID];
        int mode = (int) area.Value.Mode;
        return areaStats?.Modes != null && mode >= 0 && mode < areaStats.Modes.Length
            ? areaStats.Modes[mode]
            : null;
    }
}

// A disk worker reads the immutable saved graph while the runtime dictionary can
// replace that slot with a newer Set. Keep the old graph alive until both owners
// are finished instead of racing FMOD handle release against graph capture.
internal sealed class AkronSaveLoadSlotOwner {
    private readonly object sync = new object();
    private readonly Action<AkronSaveLoadSlot> release;
    private int references = 1;

    public AkronSaveLoadSlotOwner(AkronSaveLoadSlot slot, Action<AkronSaveLoadSlot> release) {
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        this.release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public AkronSaveLoadSlot Slot { get; }

    public AkronSaveLoadSlotLease Retain() {
        lock (sync) {
            if (references == 0) {
                throw new ObjectDisposedException(nameof(AkronSaveLoadSlotOwner));
            }
            references++;
        }
        return new AkronSaveLoadSlotLease(this);
    }

    public void ReleaseOwnership() {
        ReleaseReference();
    }

    internal void ReleaseReference() {
        bool releaseSlot;
        lock (sync) {
            if (references == 0) {
                return;
            }
            references--;
            releaseSlot = references == 0;
        }
        if (releaseSlot) {
            release(Slot);
        }
    }
}

internal sealed class AkronSaveLoadSlotLease : IDisposable {
    private AkronSaveLoadSlotOwner owner;

    public AkronSaveLoadSlotLease(AkronSaveLoadSlotOwner owner) {
        this.owner = owner;
    }

    public AkronSaveLoadSlot Slot => owner?.Slot;

    public AkronSaveLoadSlotLease Retain() {
        return owner?.Retain();
    }

    public void Dispose() {
        AkronSaveLoadSlotOwner releasedOwner = owner;
        owner = null;
        releasedOwner?.ReleaseReference();
    }
}

// Persistent StartPos uses one graph root for the Level and its run-scoped
// process state. Global save data stays owned by the active file. The berry
// snapshot above restores only the active map's collection progress.
internal sealed class AkronPersistentRuntimeState {
    public Level Level { get; set; }
    public GrabModes GrabMode { get; set; }
    public CrouchDashModes CrouchDashMode { get; set; }
    public float EngineTimeRate { get; set; }
    public float GlitchValue { get; set; }
    public float DistortAnxiety { get; set; }
    public float DistortGameRate { get; set; }
    public Dictionary<string, EverestModuleSession> ModuleSessions { get; set; } =
        new Dictionary<string, EverestModuleSession>();

    public static AkronPersistentRuntimeState CaptureSaved(AkronSaveLoadSlot slot) {
        AkronPersistentRuntimeState state = new AkronPersistentRuntimeState {
            Level = slot.SavedLevel,
            GrabMode = slot.GrabMode,
            CrouchDashMode = slot.CrouchDashMode,
            EngineTimeRate = slot.EngineTimeRate,
            GlitchValue = slot.GlitchValue,
            DistortAnxiety = slot.DistortAnxiety,
            DistortGameRate = slot.DistortGameRate
        };
        CopyNonAkronModuleState(slot.ModuleSessions, state.ModuleSessions);
        return state;
    }

    public static AkronPersistentRuntimeState CaptureCurrent(Level level) {
        AkronPersistentRuntimeState state = new AkronPersistentRuntimeState {
            Level = level,
            GrabMode = Settings.Instance.GrabMode,
            CrouchDashMode = Settings.Instance.CrouchDashMode,
#pragma warning disable CS0618
            EngineTimeRate = Engine.TimeRate,
#pragma warning restore CS0618
            GlitchValue = Glitch.Value,
            DistortAnxiety = Distort.Anxiety,
            DistortGameRate = Distort.GameRate
        };
        foreach (EverestModule module in Everest.Modules.Where(module =>
                     module is not AkronModule && module.GetType().Name != "NullModule")) {
            string key = module.GetType().FullName ?? module.GetType().Name;
            if (module._Session != null) {
                state.ModuleSessions[key] = module._Session;
            }
        }
        return state;
    }

    private static void CopyNonAkronModuleState<T>(
        IReadOnlyDictionary<string, T> source,
        IDictionary<string, T> destination
    ) {
        string akronKey = typeof(AkronModule).FullName ?? typeof(AkronModule).Name;
        foreach (KeyValuePair<string, T> pair in source) {
            if (!string.Equals(pair.Key, akronKey, StringComparison.Ordinal)) {
                destination[pair.Key] = pair.Value;
            }
        }
    }
}

public sealed class AkronSessionEntityId {
    public string Level { get; set; } = string.Empty;
    public int ID { get; set; }

    public static AkronSessionEntityId FromEntityId(EntityID id) {
        return new AkronSessionEntityId {
            Level = id.Level ?? string.Empty,
            ID = id.ID
        };
    }

    public EntityID ToEntityId() {
        return new EntityID(Level ?? string.Empty, ID);
    }
}
