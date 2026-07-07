using Celeste.Mod;
using System.Collections.Generic;
namespace Celeste.Mod.Akron;

public sealed class AkronRoomStatRecord {
    public string AreaSid { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public int Visits { get; set; }
    public int Deaths { get; set; }
    public int Strawberries { get; set; }
    public long LastInGameTime { get; set; }
    public long BestInGameTime { get; set; }
    public long LastAliveTime { get; set; }
}

public sealed class AkronPersistedStartPos {
    public float X { get; set; }
    public float Y { get; set; }
    public string Room { get; set; } = string.Empty;
    public string AreaSid { get; set; } = string.Empty;
    public bool UsesSpawnConfig { get; set; }
    public int Dashes { get; set; } = -1;
    public int StaminaPercent { get; set; } = -1;
    public AkronStartPosFacing Facing { get; set; } = AkronStartPosFacing.Current;
    public bool Idle { get; set; }
    public bool Grab { get; set; }
    public string SnapshotPath { get; set; } = string.Empty;
}

public sealed class AkronPersistedStartPosMap {
    public Dictionary<int, AkronPersistedStartPos> Slots { get; set; } = new Dictionary<int, AkronPersistedStartPos>();
}

public class AkronModuleSaveData : EverestModuleSaveData {
    public Dictionary<string, long> BestRoomTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, long> BestSegmentTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, AkronRoomStatRecord> RoomStats { get; set; } = new Dictionary<string, AkronRoomStatRecord>();
    public Dictionary<string, AkronPersistedStartPosMap> StartPositionsByMap { get; set; } = new Dictionary<string, AkronPersistedStartPosMap>();
}
