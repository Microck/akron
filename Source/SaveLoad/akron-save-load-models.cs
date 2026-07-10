using System;
using System.Collections.Generic;
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
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action clearState,
        Action<Level> beforeSaveState,
        Action<Level> beforeLoadState,
        Action preCloneEntities
    ) {
        Id = Guid.NewGuid().ToString("N");
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
