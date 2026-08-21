using Celeste.Mod;
using System;
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

    // The saved-state format the slot's room state was written under, as
    // AkronReconstructionDocument.CurrentFormat read it at the moment the slot was
    // set. This entry outlives that state: a format move makes the state file
    // unreadable and the sweep then deletes it, so by the time a player loads the
    // slot the file is the one thing that can no longer say why the slot came up
    // empty. Recording the format here is what lets the message name a format move
    // and, just as importantly, refuse to name one when the state went missing for
    // some other reason.
    //
    // Empty means the entry was written before this was recorded. That reads as an
    // older format rather than an unknown one, and it is not a guess: the field
    // arrived in the same release that moved the format, so a build that did not
    // write it also wrote its states under a format this build no longer reads.
    public string SnapshotFormat { get; set; } = string.Empty;
}

public sealed class AkronPersistedStartPosMap {
    public Dictionary<int, AkronPersistedStartPos> Slots { get; set; } = new Dictionary<int, AkronPersistedStartPos>();
}

public class AkronModuleSaveData : EverestModuleSaveData {
    // Celeste reuses numbered file slots after deletion. This identity follows the
    // profile through ordinary save and savestate cloning, while a newly created
    // profile receives a different value even when it occupies the same slot.
    public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, long> BestRoomTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, long> BestSegmentTimes { get; set; } = new Dictionary<string, long>();
    public Dictionary<string, AkronRoomStatRecord> RoomStats { get; set; } = new Dictionary<string, AkronRoomStatRecord>();
    public Dictionary<string, AkronPersistedStartPosMap> StartPositionsByMap { get; set; } = new Dictionary<string, AkronPersistedStartPosMap>();
}
