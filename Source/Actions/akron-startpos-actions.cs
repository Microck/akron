using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronActions {
    private const int MinPositionSlot = 1;
    private const int MaxPositionSlot = 9999;

    public static void SetStartPos(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            return;
        }

        CaptureStartPos(level, player.Position, useSpawnConfig: false, "StartPos " + AkronModule.Settings.ActiveStartPosSlot + " captured.");
    }

    public static void SetStartPosAtMouse(Level level, Vector2 worldPosition) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        CaptureStartPos(level, ClampToRoom(level, worldPosition), useSpawnConfig: true, "StartPos " + AkronModule.Settings.ActiveStartPosSlot + " placed.");
    }

    private static void CaptureStartPos(Level level, Vector2 position, bool useSpawnConfig, string toast) {
        int slot = AkronModule.Settings.ActiveStartPosSlot;
        string stateSlotName = GetStartPosStateSlotName(slot);
        AkronSaveLoadResult saveResult = AkronSaveLoadResult.Failed;
        StartPosPlayerSnapshot playerSnapshot = null;
        Vector2? originalRespawnPoint = level.Session.RespawnPoint;
        Vector2 clampedPosition = ClampToRoom(level, position);

        try {
            if (useSpawnConfig && level.Tracker.GetEntity<Player>() is Player player) {
                playerSnapshot = StartPosPlayerSnapshot.Capture(player);
                ApplyPlacedStartPosBeforeCapture(level, player, clampedPosition);
                level.Session.RespawnPoint = clampedPosition;
            }

            bool restoreRespawnAtStartPos = AkronModule.Settings.RespawnAtStartPos;
            AkronModule.Settings.RespawnAtStartPos = false;
            try {
                saveResult = AkronSaveLoadService.SaveRuntimeState(level, stateSlotName, AkronModule.Settings.SaveTimeAndDeaths);
            } finally {
                AkronModule.Settings.RespawnAtStartPos = restoreRespawnAtStartPos;
            }
        } finally {
            if (playerSnapshot != null && level.Tracker.GetEntity<Player>() is Player player) {
                playerSnapshot.Restore(player);
            }
            level.Session.RespawnPoint = originalRespawnPoint;
        }

        if (saveResult != AkronSaveLoadResult.Success) {
            Engine.Scene?.Add(new AkronToast("StartPos capture failed: " + saveResult + "."));
            return;
        }

        AkronModule.Session.StartPositions[slot] = new AkronStartPos {
            Position = clampedPosition,
            Room = level.Session.Level,
            AreaSid = level.Session.Area.GetSID(),
            UsesSpawnConfig = useSpawnConfig,
            Dashes = useSpawnConfig ? AkronModuleSettings.ClampStartPosDashes(AkronModule.Settings.StartPosConfiguredDashes) : -1,
            StaminaPercent = useSpawnConfig ? AkronModuleSettings.ClampStartPosStaminaPercent(AkronModule.Settings.StartPosConfiguredStaminaPercent) : -1,
            Facing = useSpawnConfig ? AkronModule.Settings.StartPosConfiguredFacing : AkronStartPosFacing.Current,
            Idle = useSpawnConfig && AkronModule.Settings.StartPosConfiguredIdle,
            Grab = useSpawnConfig && AkronModule.Settings.StartPosConfiguredGrab,
            StateSlotName = stateSlotName
        };
        Engine.Scene?.Add(new AkronToast(toast));
        SetStartPosSlot(NextStartPosSlot(slot));
    }

    private static void ApplyPlacedStartPosBeforeCapture(Level level, Player player, Vector2 position) {
        player.Position = ClampToRoom(level, position);
        player.Dead = false;
        player.Collidable = true;
        player.Active = true;
        player.Visible = true;
        player.Depth = Depths.Player;

        if (AkronModule.Settings.StartPosConfiguredIdle) {
            player.Speed = Vector2.Zero;
            player.StateMachine.ForceState(Player.StNormal);
        }

        int configuredDashes = AkronModuleSettings.ClampStartPosDashes(AkronModule.Settings.StartPosConfiguredDashes);
        if (configuredDashes >= 0) {
            player.Dashes = configuredDashes;
        }

        int configuredStamina = AkronModuleSettings.ClampStartPosStaminaPercent(AkronModule.Settings.StartPosConfiguredStaminaPercent);
        if (configuredStamina >= 0) {
            player.Stamina = 110f * configuredStamina / 100f;
        }

        if (AkronModule.Settings.StartPosConfiguredFacing == AkronStartPosFacing.Left) {
            player.Facing = Facings.Left;
        } else if (AkronModule.Settings.StartPosConfiguredFacing == AkronStartPosFacing.Right) {
            player.Facing = Facings.Right;
        }

        if (AkronModule.Settings.StartPosConfiguredGrab) {
            player.Stamina = 110f;
            player.StateMachine.ForceState(Player.StClimb);
        }
    }

    public static void LoadStartPos(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        AkronStartPos startPos = GetActiveStartPos();
        if (startPos == null) {
            Engine.Scene?.Add(new AkronToast("No StartPos saved in slot " + AkronModule.Settings.ActiveStartPosSlot + "."));
            return;
        }
        if (!IsStartPosInArea(startPos, level.Session.Area.GetSID())) {
            Engine.Scene?.Add(new AkronToast("StartPos " + AkronModule.Settings.ActiveStartPosSlot + " belongs to " + startPos.AreaSid + "."));
            return;
        }

        level.OnEndOfFrame += () => RestoreStartPos(level, startPos, "Loaded StartPos " + AkronModule.Settings.ActiveStartPosSlot + ".");
    }

    public static void LoadStartPosSlot(Level level, int slot) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        SetStartPosSlot(slot);
        LoadStartPos(level);
    }

    public static void ClearActiveStartPos() {
        ClearStartPos(AkronModule.Settings.ActiveStartPosSlot);
    }

    public static void ClearStartPos(int slot) {
        if (AkronModule.Session?.StartPositions == null) {
            return;
        }

        int clampedSlot = NormalizePositionSlot(slot);
        if (AkronModule.Session.StartPositions.TryGetValue(clampedSlot, out AkronStartPos startPos) &&
            !string.IsNullOrWhiteSpace(startPos.StateSlotName)) {
            AkronSaveLoadService.ClearRuntimeState(startPos.StateSlotName);
        }

        AkronModule.Session.StartPositions.Remove(clampedSlot);
        Engine.Scene?.Add(new AkronToast("StartPos " + clampedSlot + " cleared."));
    }

    public static void ShiftStartPos(Level level, int delta) {
        if (level == null || delta == 0 || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        IReadOnlyList<AkronStartPosEntry> entries = GetStartPositions(level);
        if (entries.Count == 0) {
            Engine.Scene?.Add(new AkronToast("No StartPos entries in this chapter."));
            return;
        }

        int current = -1;
        for (int index = 0; index < entries.Count; index++) {
            if (entries[index].Slot == AkronModule.Settings.ActiveStartPosSlot) {
                current = index;
                break;
            }
        }
        if (current < 0) {
            current = delta > 0 ? -1 : 0;
        }

        int next = (current + delta) % entries.Count;
        if (next < 0) {
            next += entries.Count;
        }

        SetStartPosSlot(entries[next].Slot);
        LoadStartPos(level);
    }

    public static IReadOnlyList<AkronStartPosEntry> GetStartPositions(Level level) {
        if (level == null || AkronModule.Session?.StartPositions == null) {
            return Array.Empty<AkronStartPosEntry>();
        }

        string areaSid = level.Session.Area.GetSID();
        Dictionary<string, int> roomOrder = BuildRoomOrder(level);
        return AkronModule.Session.StartPositions
            .Where(pair => IsStartPosInArea(pair.Value, areaSid))
            .OrderBy(pair => RoomSortIndex(roomOrder, pair.Value.Room))
            .ThenBy(pair => pair.Value.Position.X)
            .ThenBy(pair => pair.Value.Position.Y)
            .ThenBy(pair => pair.Key)
            .Select(pair => new AkronStartPosEntry(NormalizePositionSlot(pair.Key), pair.Value))
            .ToList();
    }

    public static string DescribeStartPosIndex(Level level) {
        IReadOnlyList<AkronStartPosEntry> entries = GetStartPositions(level);
        if (entries.Count == 0) {
            return "0/0";
        }

        int index = entries.ToList().FindIndex(entry => entry.Slot == AkronModule.Settings.ActiveStartPosSlot);
        return (index >= 0 ? index + 1 : 0) + "/" + entries.Count;
    }

    public static void ApplyStartPosConfiguration(AkronStartPos startPos) {
        if (startPos == null) {
            return;
        }

        startPos.UsesSpawnConfig = true;
        startPos.Dashes = AkronModuleSettings.ClampStartPosDashes(AkronModule.Settings.StartPosConfiguredDashes);
        startPos.StaminaPercent = AkronModuleSettings.ClampStartPosStaminaPercent(AkronModule.Settings.StartPosConfiguredStaminaPercent);
        startPos.Facing = AkronModule.Settings.StartPosConfiguredFacing;
        startPos.Idle = AkronModule.Settings.StartPosConfiguredIdle;
        startPos.Grab = AkronModule.Settings.StartPosConfiguredGrab;
    }

    private static void RestoreStartPos(Level level, AkronStartPos startPos, string toast) {
        bool restoreRespawnAtStartPos = AkronModule.Settings.RespawnAtStartPos;
        AkronModule.Settings.RespawnAtStartPos = false;
        try {
            if (!AkronModule.EndStartPosPlacementForLoad()) {
                AkronModule.Settings.StartPosMousePlacement = false;
            }
            if (string.IsNullOrWhiteSpace(startPos.StateSlotName)) {
                RestoreImportedStartPosPosition(level, startPos, toast);
                return;
            }

            AkronSaveLoadResult restored = AkronSaveLoadService.LoadRuntimeState(level, startPos.StateSlotName, allowDeadPlayer: true);
            if (restored != AkronSaveLoadResult.Success) {
                Engine.Scene?.Add(new AkronToast("StartPos state restore failed: " + restored + "."));
                return;
            }

            Level currentLevel = Engine.Scene as Level ?? level;
            Player player = currentLevel.Tracker.GetEntity<Player>();
            if (player != null) {
                RemoveStartPosDeathArtifacts(currentLevel);
                if (startPos.UsesSpawnConfig) {
                    ApplyStartPosToPlayer(player, startPos);
                }
                currentLevel.Session.RespawnPoint = player.Position;
                StartStartPosCameraFollow(currentLevel, player);
            }
            RelinkRuntimeRenderState(currentLevel);

            if (!string.IsNullOrWhiteSpace(toast)) {
                Engine.Scene?.Add(new AkronToast(toast));
            }
        } finally {
            AkronModule.Settings.RespawnAtStartPos = restoreRespawnAtStartPos;
        }
    }

    private static void RestoreImportedStartPosPosition(Level level, AkronStartPos startPos, string toast) {
        Level currentLevel = Engine.Scene as Level ?? level;
        if (!string.Equals(currentLevel.Session?.Level, startPos.Room, StringComparison.Ordinal)) {
            Engine.Scene?.Add(new AkronToast("Imported StartPos is in room " + startPos.Room + "."));
            return;
        }

        Player player = currentLevel.Tracker.GetEntity<Player>();
        if (player == null) {
            Engine.Scene?.Add(new AkronToast("Imported StartPos needs a live player."));
            return;
        }

        RemoveStartPosDeathArtifacts(currentLevel);
        player.Position = startPos.Position;
        if (startPos.UsesSpawnConfig) {
            ApplyStartPosToPlayer(player, startPos);
        }

        currentLevel.Session.RespawnPoint = player.Position;
        StartStartPosCameraFollow(currentLevel, player);
        RelinkRuntimeRenderState(currentLevel);
        Engine.Scene?.Add(new AkronToast(string.IsNullOrWhiteSpace(toast) ? "Loaded imported StartPos position." : toast));
    }

    private static void StartStartPosCameraFollow(Level level, Player player) {
        if (level == null || player == null) {
            return;
        }

        level.Camera.Position = ClampCameraToRoom(level, player.CameraTarget);
        RelinkRuntimeRenderState(level);
        level.Add(new AkronStartPosCameraFollow());
    }

    private sealed class AkronStartPosCameraFollow : Entity {
        private int framesRemaining = 12;

        public override void Update() {
            base.Update();
            if (Scene is not Level level) {
                RemoveSelf();
                return;
            }

            Player player = level.Tracker.GetEntity<Player>();
            if (player == null || player.Dead) {
                RemoveSelf();
                return;
            }

            // StartPos restores do not run the vanilla respawn camera setup. Keep
            // the camera attached briefly so copied savestate/free-camera state
            // cannot pin the viewport at the load frame.
            level.Camera.Position = ClampCameraToRoom(level, player.CameraTarget);
            framesRemaining--;
            if (framesRemaining <= 0) {
                RemoveSelf();
            }
        }
    }

    private static void RelinkRuntimeRenderState(Level level) {
        if (level == null) {
            return;
        }

        // StartPos loads replace the live Level graph with a cloned graph.
        // Celeste's GameplayRenderer uses a private static instance in Begin(),
        // so relink that static/camera state after replacing the live graph.
        AkronLevelRenderState.RelinkRendererCameras(level);
    }

    private sealed class StartPosPlayerSnapshot {
        private readonly Vector2 position;
        private readonly Vector2 speed;
        private readonly float stamina;
        private readonly int dashes;
        private readonly Facings facing;
        private readonly int state;
        private readonly bool dead;
        private readonly bool collidable;
        private readonly bool active;
        private readonly bool visible;
        private readonly int depth;

        private StartPosPlayerSnapshot(Player player) {
            position = player.Position;
            speed = player.Speed;
            stamina = player.Stamina;
            dashes = player.Dashes;
            facing = player.Facing;
            state = player.StateMachine.State;
            dead = player.Dead;
            collidable = player.Collidable;
            active = player.Active;
            visible = player.Visible;
            depth = player.Depth;
        }

        public static StartPosPlayerSnapshot Capture(Player player) {
            return new StartPosPlayerSnapshot(player);
        }

        public void Restore(Player player) {
            player.Position = position;
            player.Speed = speed;
            player.Stamina = stamina;
            player.Dashes = dashes;
            player.Facing = facing;
            player.Dead = dead;
            player.Collidable = collidable;
            player.Active = active;
            player.Visible = visible;
            player.Depth = depth;
            player.StateMachine.ForceState(state);
        }
    }

    private static void ApplyStartPosToPlayer(Player player, AkronStartPos startPos) {
        player.Dead = false;
        player.Collidable = true;
        player.Active = true;
        player.Visible = true;
        player.Depth = Depths.Player;

        if (startPos.Idle) {
            player.Speed = Vector2.Zero;
            player.StateMachine.ForceState(Player.StNormal);
        }

        if (startPos.Dashes >= 0) {
            player.Dashes = AkronModuleSettings.ClampStartPosDashes(startPos.Dashes);
        }

        if (startPos.StaminaPercent >= 0) {
            player.Stamina = 110f * AkronModuleSettings.ClampStartPosStaminaPercent(startPos.StaminaPercent) / 100f;
        }

        if (startPos.Facing == AkronStartPosFacing.Left) {
            player.Facing = Facings.Left;
        } else if (startPos.Facing == AkronStartPosFacing.Right) {
            player.Facing = Facings.Right;
        }

        if (startPos.Grab) {
            player.Stamina = 110f;
            player.StateMachine.ForceState(Player.StClimb);
        }
    }

    private static void RemoveStartPosDeathArtifacts(Level level) {
        if (level == null) {
            return;
        }

        foreach (PlayerDeadBody deadBody in level.Entities.OfType<PlayerDeadBody>().ToList()) {
            deadBody.RemoveSelf();
        }
    }

    private static Vector2 ClampToRoom(Level level, Vector2 position) {
        return new Vector2(
            Calc.Clamp(position.X, level.Bounds.Left, level.Bounds.Right),
            Calc.Clamp(position.Y, level.Bounds.Top, level.Bounds.Bottom));
    }

    private static Vector2 ClampCameraToRoom(Level level, Vector2 position) {
        Rectangle bounds = level.Bounds;
        float maxX = Math.Max(bounds.Left, bounds.Right - 320f);
        float maxY = Math.Max(bounds.Top, bounds.Bottom - 180f);
        return new Vector2(
            Calc.Clamp(position.X, bounds.Left, maxX),
            Calc.Clamp(position.Y, bounds.Top, maxY));
    }

    public static AkronStartPos GetActiveStartPos() {
        return GetStartPos(AkronModule.Settings.ActiveStartPosSlot);
    }

    public static AkronStartPos GetStartPos(int slot) {
        if (AkronModule.Session?.StartPositions == null) {
            return null;
        }

        int clampedSlot = NormalizePositionSlot(slot);
        return AkronModule.Session.StartPositions.TryGetValue(clampedSlot, out AkronStartPos startPos)
            ? startPos
            : null;
    }

    public static AkronStartPos GetSmartRespawnStartPos(Level level, Vector2 referencePosition) {
        AkronStartPos active = GetActiveStartPos();
        if (IsStartPosUsableInCurrentRoom(level, active)) {
            return active;
        }

        if (level == null || AkronModule.Session?.StartPositions == null) {
            return null;
        }

        string areaSid = level.Session.Area.GetSID();
        return AkronModule.Session.StartPositions.Values
            .Where(startPos => IsStartPosUsableInCurrentRoom(level, startPos) &&
                               (string.IsNullOrWhiteSpace(startPos.AreaSid) ||
                                string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal)))
            .OrderBy(startPos => Vector2.DistanceSquared(startPos.Position, referencePosition))
            .FirstOrDefault();
    }

    private static bool IsStartPosUsableInCurrentRoom(Level level, AkronStartPos startPos) {
        return level != null &&
               startPos != null &&
               string.Equals(startPos.Room, level.Session.Level, StringComparison.Ordinal) &&
               AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName);
    }

    private static bool IsStartPosInArea(AkronStartPos startPos, string areaSid) {
        return startPos != null &&
               AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName) &&
               (string.IsNullOrWhiteSpace(startPos.AreaSid) ||
                string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal));
    }

    private static Dictionary<string, int> BuildRoomOrder(Level level) {
        Dictionary<string, int> order = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<LevelData> levels = level.Session?.MapData?.Levels;
        if (levels == null) {
            return order;
        }

        for (int index = 0; index < levels.Count; index++) {
            if (!string.IsNullOrWhiteSpace(levels[index].Name) && !order.ContainsKey(levels[index].Name)) {
                order[levels[index].Name] = index;
            }
        }

        return order;
    }

    private static int RoomSortIndex(Dictionary<string, int> roomOrder, string room) {
        return room != null && roomOrder.TryGetValue(room, out int index) ? index : int.MaxValue;
    }

    public static void SetStartPosSlot(int slot) {
        AkronModule.Settings.ActiveStartPosSlot = NormalizePositionSlot(slot);
        Engine.Scene?.Add(new AkronToast("Active StartPos slot: " + AkronModule.Settings.ActiveStartPosSlot));
    }

    public static void ShiftStartPosSlot(int delta) {
        SetStartPosSlot(WrapStartPosSlot(AkronModule.Settings.ActiveStartPosSlot + delta));
    }

    private static int NextStartPosSlot(int slot) {
        return WrapStartPosSlot(slot + 1);
    }

    private static int WrapStartPosSlot(int slot) {
        int count = AkronModuleSettings.ClampStartPosSlotCount(AkronModule.Settings.StartPosSlotCount);
        int zeroBased = (slot - MinPositionSlot) % count;
        if (zeroBased < 0) {
            zeroBased += count;
        }
        return MinPositionSlot + zeroBased;
    }

    private static string GetStartPosStateSlotName(int slot) {
        return "Akron StartPos " + NormalizePositionSlot(slot);
    }

    private static int NormalizePositionSlot(int slot) {
        return Calc.Clamp(slot, MinPositionSlot, MaxPositionSlot);
    }
}
