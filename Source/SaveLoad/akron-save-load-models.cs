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
public delegate bool AkronSaveLoadRiskHandler(Level level, int slot, out string reason);

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
// process state. Global save data stays owned by the active file so restoring
// a local or imported StartPos cannot rewind or replace player progression.
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
