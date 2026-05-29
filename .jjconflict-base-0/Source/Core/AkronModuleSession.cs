using Celeste.Mod;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
namespace Celeste.Mod.Akron;

public sealed class AkronStartPos {
    public Vector2 Position { get; set; }
    public string Room { get; set; } = string.Empty;
    public string AreaSid { get; set; } = string.Empty;
    public bool UsesSpawnConfig { get; set; }
    public int Dashes { get; set; } = -1;
    public int StaminaPercent { get; set; } = -1;
    public AkronStartPosFacing Facing { get; set; } = AkronStartPosFacing.Current;
    public bool Idle { get; set; }
    public bool Grab { get; set; }
    internal string StateSlotName { get; set; } = string.Empty;
}

public class AkronModuleSession : EverestModuleSession {
    public string CurrentSessionNonce { get; set; } = string.Empty;
    public AkronStatus AttemptStatus { get; set; } = AkronStatus.GoldberryHardlistClean;
    public string AttemptReason { get; set; } = "No modifying Akron feature has been used in this attempt.";
    public bool UsedBrokeredSavestate { get; set; }
    public bool UsedUnsafeSavestateOverride { get; set; }
    public string TrackedRoom { get; set; } = string.Empty;
    public long RoomEnteredAt { get; set; }
    public long LastRoomTime { get; set; }
    public long AttemptStartedAt { get; set; }
    public long RoomStatAliveStartedAt { get; set; }
    public long RoomStatFrozenStartedAt { get; set; } = -1;
    public long RoomStatFrozenDuration { get; set; }
    public long RoomStatAliveFrozenStartedAt { get; set; } = -1;
    public long RoomStatAliveFrozenDuration { get; set; }
    public int RoomStatStrawberries { get; set; }
    public int DeathsSinceLevelLoad { get; set; }
    public int DeathsSinceRoomTransition { get; set; }
    public float DeathStatsAfterDeathTimer { get; set; }
    public bool DeathPbLossPromptShown { get; set; }
    public float LevelEnterSkipHoldSeconds { get; set; }
    public string LastRoomStatsExportPath { get; set; } = string.Empty;
    public Dictionary<int, AkronStartPos> StartPositions { get; set; } = new Dictionary<int, AkronStartPos>();
    public bool FreezeGameplay { get; set; }
    public bool StepFrameRequested { get; set; }
    public int StepFrameHoldFrames { get; set; }
    public int StepFrameRepeatCountdown { get; set; }
    public bool TimescaleEnabled { get; set; }
    public float TimescaleMultiplier { get; set; } = 1f;
    public int EditableFlagIndex { get; set; }
    public int SelectedRoomIndex { get; set; }
    public Vector2? LastDeathPosition { get; set; }
    public Rectangle? LastDeathPlayerBounds { get; set; }
    public Rectangle? LastDeathHitbox { get; set; }
    public bool LastDeathHitboxVisible { get; set; }
    public bool LastDeathHitboxSawDeathState { get; set; }
    public ulong LastDeathHitboxRecordedFrame { get; set; }
    public string LastDeathEntityType { get; set; } = string.Empty;
    public string LastScreenshotPath { get; set; } = string.Empty;
    public int AkronDashCountAtLevelStart { get; set; }
    public int AkronJumpCount { get; set; }
    public int AkronJumpCountAtLevelStart { get; set; }
    public float AkronAutosaveTimer { get; set; }
    public float AkronAutosaveCooldown { get; set; }
    public bool DeloadSpinnersApplied { get; set; }
    public int PauseTrackerPauseCount { get; set; }
    public int PauseTrackerRapidPauseCount { get; set; }
    public float PauseTrackerPausedSeconds { get; set; }
    public float PauseTrackerLastPauseAt { get; set; } = -1f;
    public float PauseTrackerCurrentPauseStartedAt { get; set; } = -1f;
    public int LagPauserTriggerCount { get; set; }
    public float LagPauserLastSpikeMs { get; set; }
    public bool UsedGoldenStartHelper { get; set; }
    public bool UsedJournalSnapshotCompare { get; set; }
    public string LastJournalSnapshotPath { get; set; } = string.Empty;
    public string LastJournalCompareSummary { get; set; } = string.Empty;
}
