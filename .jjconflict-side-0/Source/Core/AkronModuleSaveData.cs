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

public class AkronModuleSaveData : EverestModuleSaveData {
    public Dictionary<string, long> BestRoomTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, long> BestSegmentTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, AkronRoomStatRecord> RoomStats { get; set; } = new Dictionary<string, AkronRoomStatRecord>();
}
