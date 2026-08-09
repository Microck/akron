using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronActions {
    internal const string StartPosStateSlotPrefix = "Akron StartPos ";
    private const int MinPositionSlot = 1;
    private const int MaxPositionSlot = 9999;

    // Level.Update reads this generation before processing pre-update actions.
    // A successful Set or Load increments it so the exact StartPos frame can
    // render before Celeste advances the room simulation.
    internal static ulong StartPosFrameGeneration { get; private set; }
    private static bool startPosCaptureInProgress;
    private static readonly Dictionary<string, Dictionary<int, AkronStartPos>> PendingStartPositionsByFileAndMap =
        new Dictionary<string, Dictionary<int, AkronStartPos>>(StringComparer.Ordinal);

    public static void SetStartPos(Level level) {
        SetStartPos(level, null);
    }

    internal static void SetStartPos(Level level, Action<bool> completion) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            completion?.Invoke(false);
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            completion?.Invoke(false);
            return;
        }

        CaptureStartPos(
            level,
            player.Position,
            useSpawnConfig: false,
            "StartPos " + AkronModule.Settings.ActiveStartPosSlot + " captured.",
            completion);
    }

    public static void SetStartPosAtMouse(Level level, Vector2 worldPosition) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        CaptureStartPos(
            level,
            ClampToRoom(level, worldPosition),
            useSpawnConfig: true,
            "StartPos " + AkronModule.Settings.ActiveStartPosSlot + " placed.",
            null);
    }

    private static void CaptureStartPos(
        Level level,
        Vector2 position,
        bool useSpawnConfig,
        string toast,
        Action<bool> completion
    ) {
        if (startPosCaptureInProgress) {
            Engine.Scene?.Add(new AkronToast("StartPos capture is still finishing."));
            completion?.Invoke(false);
            return;
        }
        startPosCaptureInProgress = true;

        int slot = AkronModule.Settings.ActiveStartPosSlot;
        int fileSlot = GetCurrentFileSlot();
        AkronModuleSaveData saveData = AkronModule.SaveData;
        string areaSid = GetAreaSid(level);
        string stateSlotName = GetStartPosStateSlotName(areaSid, slot, fileSlot);
        AkronSaveLoadResult saveResult = AkronSaveLoadResult.Failed;
        Stopwatch captureTimer = Stopwatch.StartNew();
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
                // StartPos always keeps cumulative time and deaths instead of
                // rewinding those statistics with the captured room state.
                saveResult = AkronSaveLoadService.SaveRuntimeState(level, stateSlotName, saveTimeAndDeaths: false);
            } finally {
                AkronModule.Settings.RespawnAtStartPos = restoreRespawnAtStartPos;
            }
        } catch {
            startPosCaptureInProgress = false;
            completion?.Invoke(false);
            throw;
        } finally {
            if (playerSnapshot != null && level.Tracker.GetEntity<Player>() is Player player) {
                playerSnapshot.Restore(player);
            }
            level.Session.RespawnPoint = originalRespawnPoint;
        }

        if (saveResult != AkronSaveLoadResult.Success) {
            startPosCaptureInProgress = false;
            Engine.Scene?.Add(new AkronToast("StartPos capture failed: " + saveResult + "."));
            completion?.Invoke(false);
            return;
        }

        captureTimer.Stop();
        Logger.Log(LogLevel.Info, nameof(AkronActions),
            "StartPos warm capture finished in " +
            captureTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms.");

        AkronStartPos startPos = new AkronStartPos {
            Position = clampedPosition,
            Room = level.Session.Level,
            AreaSid = areaSid,
            UsesSpawnConfig = useSpawnConfig,
            Dashes = useSpawnConfig ? AkronModuleSettings.ClampStartPosDashes(AkronModule.Settings.StartPosConfiguredDashes) : -1,
            StaminaPercent = useSpawnConfig ? AkronModuleSettings.ClampStartPosStaminaPercent(AkronModule.Settings.StartPosConfiguredStaminaPercent) : -1,
            Facing = useSpawnConfig ? AkronModule.Settings.StartPosConfiguredFacing : AkronStartPosFacing.Current,
            Idle = useSpawnConfig && AkronModule.Settings.StartPosConfiguredIdle,
            Grab = useSpawnConfig && AkronModule.Settings.StartPosConfiguredGrab,
            StateSlotName = stateSlotName
        };
        PublishPendingStartPos(fileSlot, slot, startPos);
        startPosCaptureInProgress = false;
        long persistenceGeneration = AkronStartPosPersistence.Enqueue(
            fileSlot,
            saveData,
            slot,
            startPos,
            stateSlotName);
        if (persistenceGeneration == 0) {
            const string persistenceError = "StartPos was captured in memory, but its restart copy could not start.";
            Logger.Log(LogLevel.Warn, nameof(AkronActions), persistenceError);
            Engine.Scene?.Add(new AkronToast(persistenceError));
        } else {
            Engine.Scene?.Add(new AkronToast(toast));
        }
        completion?.Invoke(true);
        if (!useSpawnConfig) {
            StartPosFrameGeneration++;
        }
    }

    internal static void CompletePersistentStartPosCapture(
        int fileSlot,
        AkronModuleSaveData saveData,
        int slot,
        AkronStartPos startPos,
        string stateSlotName,
        long generation,
        AkronSaveLoadResult persistResult,
        string persistError,
        string stagingDirectory,
        TimeSpan elapsed
    ) {
        if (!AkronStartPosPersistence.IsCurrent(stateSlotName, generation)) {
            return;
        }
        if (!IsOriginatingSaveFileActive(fileSlot, saveData)) {
            Logger.Log(LogLevel.Warn, nameof(AkronActions),
                "StartPos restart copy was discarded because the active save file changed.");
            return;
        }

        AkronStartPosReconstruction.PreparedSnapshotInstall installedSnapshot = null;
        try {
            if (persistResult == AkronSaveLoadResult.Success) {
                installedSnapshot = AkronStartPosReconstruction.PrepareSnapshotInstall(
                    stateSlotName,
                    stagingDirectory);
                if (!installedSnapshot.Install(out string installError)) {
                    persistResult = AkronSaveLoadResult.Failed;
                    persistError = installError;
                }
            }

            if (persistResult != AkronSaveLoadResult.Success) {
                string message = "StartPos is ready in memory, but its restart copy failed" +
                                 (string.IsNullOrWhiteSpace(persistError) ? "." : ": " + persistError);
                Logger.Log(LogLevel.Warn, nameof(AkronActions), message);
                Engine.Scene?.Add(new AkronToast(message));
                return;
            }

            if (!PersistStartPos(slot, startPos, fileSlot, saveData)) {
                const string message = "StartPos is ready in memory, but its restart metadata could not be saved.";
                Logger.Log(LogLevel.Warn, nameof(AkronActions), message);
                Engine.Scene?.Add(new AkronToast(message));
                return;
            }

            installedSnapshot.Commit();
            RemovePendingStartPos(fileSlot, slot, startPos);
            Logger.Log(LogLevel.Debug, nameof(AkronActions),
                "Restart-safe StartPos copy finished in " +
                elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms.");
        } finally {
            installedSnapshot?.Dispose();
        }
    }

    private static void PublishPendingStartPos(int fileSlot, int slot, AkronStartPos startPos) {
        string areaSid = NormalizeAreaSid(startPos?.AreaSid);
        if (startPos == null || string.IsNullOrWhiteSpace(areaSid)) {
            return;
        }

        string pendingKey = BuildPendingStartPosKey(fileSlot, areaSid);
        if (!PendingStartPositionsByFileAndMap.TryGetValue(pendingKey, out Dictionary<int, AkronStartPos> pending)) {
            pending = new Dictionary<int, AkronStartPos>();
            PendingStartPositionsByFileAndMap[pendingKey] = pending;
        }
        int normalizedSlot = NormalizePositionSlot(slot);
        pending[normalizedSlot] = startPos;
        if (AkronModule.Session != null) {
            AkronModule.Session.LoadedStartPositionsAreaSid = areaSid;
            AkronModule.Session.StartPositions ??= new Dictionary<int, AkronStartPos>();
            AkronModule.Session.StartPositions[normalizedSlot] = startPos;
        }
    }

    private static void RemovePendingStartPos(int fileSlot, int slot, AkronStartPos startPos) {
        string areaSid = NormalizeAreaSid(startPos?.AreaSid);
        string pendingKey = BuildPendingStartPosKey(fileSlot, areaSid);
        int normalizedSlot = NormalizePositionSlot(slot);
        if (!PendingStartPositionsByFileAndMap.TryGetValue(pendingKey, out Dictionary<int, AkronStartPos> pending) ||
            !pending.TryGetValue(normalizedSlot, out AkronStartPos current) ||
            !ReferenceEquals(current, startPos)) {
            return;
        }
        pending.Remove(normalizedSlot);
        if (pending.Count == 0) {
            PendingStartPositionsByFileAndMap.Remove(pendingKey);
        }
    }

    internal static bool HasPendingStartPosForArea(string areaSid) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (string.IsNullOrWhiteSpace(normalizedAreaSid)) {
            return false;
        }

        string pendingKey = BuildPendingStartPosKey(GetCurrentFileSlot(), normalizedAreaSid);
        return PendingStartPositionsByFileAndMap.TryGetValue(
            pendingKey,
            out Dictionary<int, AkronStartPos> pending) && pending.Count > 0;
    }

    internal static bool HasPendingStartPosState(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName)) {
            return false;
        }

        return PendingStartPositionsByFileAndMap.Values.Any(pending =>
            pending.Values.Any(startPos =>
                string.Equals(startPos?.StateSlotName, stateSlotName, StringComparison.Ordinal)));
    }

    internal static void ClearPendingStartPosState() {
        PendingStartPositionsByFileAndMap.Clear();
        startPosCaptureInProgress = false;
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
        if (startPosCaptureInProgress) {
            Engine.Scene?.Add(new AkronToast("StartPos capture is still finishing."));
            return;
        }

        int slot = AkronModule.Settings.ActiveStartPosSlot;
        AkronStartPos startPos = GetStartPos(slot);
        if (startPos == null) {
            Engine.Scene?.Add(new AkronToast("No StartPos saved in slot " + AkronModule.Settings.ActiveStartPosSlot + "."));
            return;
        }
        if (!IsStartPosInArea(startPos, level.Session.Area.GetSID())) {
            Engine.Scene?.Add(new AkronToast("StartPos " + AkronModule.Settings.ActiveStartPosSlot + " belongs to " + startPos.AreaSid + "."));
            return;
        }

        AkronModule.ScheduleAfterStableEngineUpdate(() => {
            if (Engine.Scene != level) {
                return;
            }
            if (startPosCaptureInProgress) {
                return;
            }

            if (!RestoreStartPos(
                level,
                startPos,
                "Loaded StartPos " + slot + ".",
                slot,
                enableRespawnAtStartPosAfterRestore: true)) {
                return;
            }

            Level currentLevel = Engine.Scene as Level ?? level;
            BeginStartPosInputWait(currentLevel, waitingForWipe: false);
        });
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
        if (AkronModule.Session?.StartPositions == null || startPosCaptureInProgress) {
            return;
        }

        int clampedSlot = NormalizePositionSlot(slot);
        string areaSid = GetLoadedAreaSid();
        int fileSlot = GetCurrentFileSlot();
        string pendingKey = BuildPendingStartPosKey(fileSlot, areaSid);
        AkronStartPosPersistence.Cancel(GetStartPosStateSlotName(areaSid, clampedSlot, fileSlot));
        if (PendingStartPositionsByFileAndMap.TryGetValue(pendingKey, out Dictionary<int, AkronStartPos> pending)) {
            pending.Remove(clampedSlot);
            if (pending.Count == 0) {
                PendingStartPositionsByFileAndMap.Remove(pendingKey);
            }
        }
        if (AkronModule.Session.StartPositions.TryGetValue(clampedSlot, out AkronStartPos startPos) &&
            !string.IsNullOrWhiteSpace(startPos.StateSlotName)) {
            AkronSaveLoadService.ClearRuntimeState(startPos.StateSlotName);
        }
        AkronModule.Session.StartPositions.Remove(clampedSlot);
        RemovePersistedStartPos(areaSid, clampedSlot);
        if (AkronModule.Session.LastLoadedStartPosSlot == clampedSlot) {
            AkronModule.Session.LastLoadedStartPosSlot = 0;
        }
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

        EnsureStartPositionsLoaded(level);
        string areaSid = GetAreaSid(level);
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

    internal static void RestoreStartPosAfterDeath(Level level, AkronStartPos startPos) {
        if (level == null || startPos == null || startPosCaptureInProgress) {
            return;
        }

        AkronModule.ScheduleAfterStableEngineUpdate(() => {
            if (Engine.Scene != level) {
                return;
            }
            if (startPosCaptureInProgress) {
                return;
            }

            if (!RestoreStartPos(level, startPos, string.Empty, FindStartPosSlot(startPos), endPlacementForLoad: false)) {
                level.Reload();
                return;
            }

            Level restoredLevel = Engine.Scene as Level ?? level;
            if (restoredLevel.Session.RespawnPoint is Vector2 respawnPoint) {
                SpotlightWipe.FocusPoint = respawnPoint - restoredLevel.Camera.Position;
            }
            BeginStartPosInputWait(restoredLevel, waitingForWipe: true);
            restoredLevel.DoScreenWipe(wipeIn: true, () => CompleteStartPosInputWaitWipe(restoredLevel));
        });
    }

    private static bool RestoreStartPos(Level level, AkronStartPos startPos, string toast, int loadedSlot = 0, bool endPlacementForLoad = true, bool enableRespawnAtStartPosAfterRestore = false) {
        ClearStartPosInputWait();
        bool restoreRespawnAtStartPos = AkronModule.Settings.RespawnAtStartPos;
        bool restoredStartPos = false;
        AkronModule.Settings.RespawnAtStartPos = false;
        try {
            if (endPlacementForLoad && !AkronModule.EndStartPosPlacementForLoad()) {
                AkronModule.Settings.StartPosMousePlacement = false;
            }
            bool wasInMemory = AkronSaveLoadService.HasRuntimeStateInMemory(startPos.StateSlotName);
            Stopwatch restoreTimer = Stopwatch.StartNew();
            AkronSaveLoadResult restored = AkronSaveLoadService.LoadRuntimeState(level, startPos.StateSlotName, allowDeadPlayer: true);
            restoreTimer.Stop();
            if (restored != AkronSaveLoadResult.Success) {
                string detail = AkronSaveLoadService.LastPersistentSnapshotError;
                string message = "StartPos state restore failed: " + restored +
                                 (string.IsNullOrWhiteSpace(detail) ? "." : " at " + detail);
                Logger.Log(LogLevel.Warn, nameof(AkronActions), message);
                Engine.Scene?.Add(new AkronToast(message));
                return false;
            }
            Logger.Log(LogLevel.Info, nameof(AkronActions),
                "StartPos " + (wasInMemory ? "warm" : "cold") + " restore finished in " +
                restoreTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms.");

            Level currentLevel = Engine.Scene as Level ?? level;
            RelinkRuntimeRenderState(currentLevel);
            StartPosFrameGeneration++;

            // The room snapshot contains the Akron session from its Set boundary. Rebuild the
            // cumulative slot registry from persisted metadata so loading an older slot cannot
            // erase slots that the player created later.
            LoadStartPositionsForLevel(currentLevel);
            if (loadedSlot > 0) {
                AkronModule.Session.LastLoadedStartPosSlot = loadedSlot;
            }
            restoredStartPos = true;

            if (!string.IsNullOrWhiteSpace(toast)) {
                Engine.Scene?.Add(new AkronToast(toast));
            }
        } finally {
            AkronModule.Settings.RespawnAtStartPos = (enableRespawnAtStartPosAfterRestore && restoredStartPos)
                || restoreRespawnAtStartPos;
        }

        return restoredStartPos;
    }

    internal static void RelinkRuntimeRenderState(Level level) {
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

    private static Vector2 ClampToRoom(Level level, Vector2 position) {
        return new Vector2(
            Calc.Clamp(position.X, level.Bounds.Left, level.Bounds.Right),
            Calc.Clamp(position.Y, level.Bounds.Top, level.Bounds.Bottom));
    }

    public static AkronStartPos GetActiveStartPos() {
        return GetStartPos(AkronModule.Settings.ActiveStartPosSlot);
    }

    public static AkronStartPos GetStartPos(int slot) {
        if (AkronModule.Session?.StartPositions == null) {
            return null;
        }

        if (slot < MinPositionSlot) {
            return null;
        }

        int clampedSlot = NormalizePositionSlot(slot);
        return AkronModule.Session.StartPositions.TryGetValue(clampedSlot, out AkronStartPos startPos)
            ? startPos
            : null;
    }

    public static AkronStartPos GetSmartRespawnStartPos(Level level, Vector2 referencePosition) {
        EnsureStartPositionsLoaded(level);
        AkronStartPos active = GetActiveStartPos();
        if (IsStartPosUsableInCurrentRoom(level, active)) {
            return active;
        }

        if (level == null || AkronModule.Session?.StartPositions == null) {
            return null;
        }

        string areaSid = GetAreaSid(level);
        return AkronModule.Session.StartPositions.Values
            .Where(startPos => IsStartPosUsableInCurrentRoom(level, startPos) &&
                               (string.IsNullOrWhiteSpace(startPos.AreaSid) ||
                                string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal)))
            .OrderBy(startPos => Vector2.DistanceSquared(startPos.Position, referencePosition))
            .FirstOrDefault();
    }

    public static AkronStartPos GetDeathRespawnStartPos(Level level, Vector2 referencePosition) {
        AkronStartPos lastLoaded = GetStartPos(AkronModule.Session?.LastLoadedStartPosSlot ?? 0);
        if (IsStartPosUsableForDeath(level, lastLoaded)) {
            return lastLoaded;
        }

        if (AkronModule.Settings.SmartStartPos) {
            return GetSmartRespawnStartPos(level, referencePosition);
        }

        AkronStartPos active = GetActiveStartPos();
        return IsStartPosUsableForDeath(level, active) ? active : null;
    }

    private static int FindStartPosSlot(AkronStartPos startPos) {
        if (startPos == null || AkronModule.Session?.StartPositions == null) {
            return 0;
        }

        foreach (KeyValuePair<int, AkronStartPos> pair in AkronModule.Session.StartPositions) {
            if (ReferenceEquals(pair.Value, startPos)) {
                return NormalizePositionSlot(pair.Key);
            }
        }

        return 0;
    }

    private static bool IsStartPosUsableInCurrentRoom(Level level, AkronStartPos startPos) {
        return level != null &&
               startPos != null &&
               string.Equals(startPos.Room, level.Session.Level, StringComparison.Ordinal) &&
               HasRestorableStartPosState(startPos);
    }

    private static bool IsStartPosUsableForDeath(Level level, AkronStartPos startPos) {
        if (level == null || !IsStartPosInArea(startPos, GetAreaSid(level))) {
            return false;
        }

        return string.Equals(startPos.Room, level.Session.Level, StringComparison.Ordinal) ||
               !string.IsNullOrWhiteSpace(startPos.StateSlotName);
    }

    private static bool IsStartPosInArea(AkronStartPos startPos, string areaSid) {
        return startPos != null &&
               HasRestorableStartPosState(startPos) &&
               (string.IsNullOrWhiteSpace(startPos.AreaSid) ||
                string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal));
    }

    private static bool HasRestorableStartPosState(AkronStartPos startPos) {
        return startPos != null &&
               !string.IsNullOrWhiteSpace(startPos.StateSlotName) &&
               AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName);
    }

    internal static void LoadStartPositionsForLevel(Level level) {
        if (level == null || AkronModule.Session == null) {
            return;
        }

        string areaSid = GetAreaSid(level);
        AkronModule.Session.LoadedStartPositionsAreaSid = areaSid;
        Dictionary<int, AkronStartPos> startPositions = BuildRuntimeStartPositions(
            areaSid,
            GetPersistedStartPositions(areaSid));
        string pendingKey = BuildPendingStartPosKey(GetCurrentFileSlot(), areaSid);
        if (PendingStartPositionsByFileAndMap.TryGetValue(pendingKey, out Dictionary<int, AkronStartPos> pending)) {
            foreach (KeyValuePair<int, AkronStartPos> pair in pending) {
                if (pair.Value != null && AkronSaveLoadService.HasRuntimeState(pair.Value.StateSlotName)) {
                    startPositions[pair.Key] = pair.Value;
                }
            }
        }
        AkronModule.Session.StartPositions = startPositions;
    }

    internal static IEnumerable<KeyValuePair<int, AkronStartPos>> GetStartPositionsForArea(string areaSid) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (string.IsNullOrWhiteSpace(normalizedAreaSid)) {
            return Enumerable.Empty<KeyValuePair<int, AkronStartPos>>();
        }

        if (AkronModule.Session != null &&
            string.Equals(AkronModule.Session.LoadedStartPositionsAreaSid, normalizedAreaSid, StringComparison.Ordinal)) {
            return AkronModule.Session.StartPositions ?? new Dictionary<int, AkronStartPos>();
        }

        return BuildRuntimeStartPositions(normalizedAreaSid, GetPersistedStartPositions(normalizedAreaSid));
    }

    internal static void ReplaceAllStartPositions(
        Dictionary<int, AkronStartPos> startPositions,
        AkronModuleSession targetSession = null,
        string targetAreaSid = "",
        bool persistMetadata = true,
        StartPosReplacementTransaction replacementTransaction = null
    ) {
        Dictionary<int, AkronStartPos> normalizedStartPositions = startPositions ?? new Dictionary<int, AkronStartPos>();
        AkronModuleSaveData saveData = AkronModule.Instance == null ? null : AkronModule.SaveData;
        if (saveData == null) {
            if (targetSession != null) {
                targetSession.StartPositions = normalizedStartPositions;
            }
            return;
        }

        string areaSid = NormalizeAreaSid(targetAreaSid);
        if (string.IsNullOrWhiteSpace(areaSid)) {
            string[] areaSids = normalizedStartPositions
                .Values
                .Where(startPos => startPos != null && !string.IsNullOrWhiteSpace(startPos.AreaSid))
                .Select(startPos => NormalizeAreaSid(startPos.AreaSid))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (areaSids.Length > 1) {
                throw new InvalidDataException("StartPos import contains entries for multiple maps.");
            }
            areaSid = areaSids.SingleOrDefault();
        }
        if (string.IsNullOrWhiteSpace(areaSid)) {
            areaSid = GetLoadedAreaSid();
        }
        if (string.IsNullOrWhiteSpace(areaSid)) {
            throw new InvalidDataException("StartPos import does not identify a target map.");
        }

        ReplacePersistedStartPositionsForMap(
            saveData,
            areaSid,
            normalizedStartPositions,
            replacementTransaction);
        if (Engine.Scene is Level level) {
            LoadStartPositionsForLevel(level);
        } else if (targetSession != null &&
                   string.Equals(NormalizeAreaSid(targetSession.LoadedStartPositionsAreaSid), areaSid, StringComparison.Ordinal)) {
            targetSession.StartPositions = BuildRuntimeStartPositions(areaSid, GetPersistedStartPositions(areaSid));
        }
        if (persistMetadata && !SaveAkronStartPosData()) {
            throw new IOException("Could not persist StartPos metadata.");
        }
    }

    internal static void ReplacePersistedStartPositionsForMap(
        AkronModuleSaveData saveData,
        string targetAreaSid,
        Dictionary<int, AkronStartPos> startPositions,
        StartPosReplacementTransaction replacementTransaction = null
    ) {
        if (saveData == null) {
            throw new ArgumentNullException(nameof(saveData));
        }

        string areaSid = NormalizeAreaSid(targetAreaSid);
        if (string.IsNullOrWhiteSpace(areaSid)) {
            throw new InvalidDataException("StartPos import does not identify a target map.");
        }

        // Validate the complete import before deleting any existing state.
        foreach (AkronStartPos startPos in (startPositions ?? new Dictionary<int, AkronStartPos>()).Values) {
            string entryAreaSid = NormalizeAreaSid(startPos?.AreaSid);
            if (!string.IsNullOrWhiteSpace(entryAreaSid) && !string.Equals(entryAreaSid, areaSid, StringComparison.Ordinal)) {
                throw new InvalidDataException("StartPos import contains entries for a different map.");
            }
        }

        int[] pendingOnlySlots = Array.Empty<int>();
        if (replacementTransaction == null) {
            // Direct replacement has no rollback boundary, so invalidate older Sets now.
            int fileSlot = GetCurrentFileSlot();
            string pendingKey = BuildPendingStartPosKey(fileSlot, areaSid);
            if (PendingStartPositionsByFileAndMap.TryGetValue(pendingKey, out Dictionary<int, AkronStartPos> pending)) {
                pendingOnlySlots = pending.Keys
                    .Select(NormalizePositionSlot)
                    .Distinct()
                    .ToArray();
                foreach (int pendingSlot in pendingOnlySlots) {
                    AkronStartPosPersistence.Cancel(GetStartPosStateSlotName(areaSid, pendingSlot, fileSlot));
                }
                PendingStartPositionsByFileAndMap.Remove(pendingKey);
            }
        }

        int[] previousSlots = (saveData.StartPositionsByMap != null &&
                               saveData.StartPositionsByMap.TryGetValue(areaSid, out AkronPersistedStartPosMap previousMap)
            ? previousMap?.Slots?.Keys ?? Enumerable.Empty<int>()
            : Enumerable.Empty<int>())
            .Select(NormalizePositionSlot)
            .Distinct()
            .ToArray();
        HashSet<int> replacementSlots = (startPositions ?? new Dictionary<int, AkronStartPos>())
            .Where(pair => pair.Value != null)
            .Select(pair => NormalizePositionSlot(pair.Key))
            .ToHashSet();
        if (AkronModule.Instance != null) {
            HashSet<int> coveredSlots = previousSlots.Concat(replacementSlots).ToHashSet();
            foreach (int pendingSlot in pendingOnlySlots.Where(slot => !coveredSlots.Contains(slot))) {
                ClearStartPosRuntimeState(areaSid, pendingSlot);
            }
            foreach (int previousSlot in previousSlots) {
                if (replacementSlots.Contains(previousSlot)) {
                    RunOrDeferReplacementCleanup(
                        replacementTransaction,
                        previousSlot,
                        () => DiscardStartPosRuntimeStateMemory(areaSid, previousSlot));
                } else {
                    RunOrDeferReplacementCleanup(
                        replacementTransaction,
                        previousSlot,
                        () => ClearStartPosRuntimeState(areaSid, previousSlot));
                }
            }
        }
        AkronPersistedStartPosMap replacement = new AkronPersistedStartPosMap();
        foreach (KeyValuePair<int, AkronStartPos> pair in startPositions ?? new Dictionary<int, AkronStartPos>()) {
            AkronStartPos startPos = pair.Value;
            if (startPos == null) {
                continue;
            }

            string entryAreaSid = NormalizeAreaSid(startPos.AreaSid);
            if (!string.IsNullOrWhiteSpace(entryAreaSid) && !string.Equals(entryAreaSid, areaSid, StringComparison.Ordinal)) {
                throw new InvalidDataException("StartPos import contains entries for a different map.");
            }

            int slot = NormalizePositionSlot(pair.Key);
            RunOrDeferReplacementCleanup(
                replacementTransaction,
                slot,
                () => DiscardStartPosRuntimeStateMemory(areaSid, slot));
            startPos.AreaSid = areaSid;
            startPos.StateSlotName = string.Empty;
            replacement.Slots[slot] = ToPersistedStartPos(startPos);
        }

        saveData.StartPositionsByMap ??= new Dictionary<string, AkronPersistedStartPosMap>();
        if (replacement.Slots.Count == 0) {
            saveData.StartPositionsByMap.Remove(areaSid);
        } else {
            saveData.StartPositionsByMap[areaSid] = replacement;
        }
    }

    private static void RunOrDeferReplacementCleanup(
        StartPosReplacementTransaction replacementTransaction,
        int slot,
        Action cleanup
    ) {
        if (replacementTransaction == null) {
            cleanup();
        } else {
            replacementTransaction.DeferCleanup(slot, cleanup);
        }
    }

    internal static StartPosReplacementTransaction BeginStartPosReplacement(string areaSid) {
        return new StartPosReplacementTransaction(GetCurrentFileSlot(), NormalizeAreaSid(areaSid));
    }

    internal sealed class StartPosReplacementTransaction : IDisposable {
        private readonly string areaSid;
        private readonly int fileSlot;
        private readonly string pendingKey;
        private readonly Dictionary<int, AkronStartPos> pending;
        private readonly List<Action> deferredCleanup = new List<Action>();
        private readonly HashSet<int> deferredCleanupSlots = new HashSet<int>();
        private bool committed;

        public StartPosReplacementTransaction(int fileSlot, string areaSid) {
            this.fileSlot = fileSlot;
            this.areaSid = areaSid;
            pendingKey = BuildPendingStartPosKey(fileSlot, areaSid);
            if (PendingStartPositionsByFileAndMap.Remove(pendingKey, out Dictionary<int, AkronStartPos> previousPending)) {
                pending = previousPending;
            }
        }

        public void DeferCleanup(int slot, Action cleanup) {
            deferredCleanupSlots.Add(NormalizePositionSlot(slot));
            deferredCleanup.Add(cleanup);
        }

        public void Commit() {
            if (committed) {
                return;
            }

            // The snapshot files and metadata have already committed before
            // this point. Cleanup is best-effort and must not turn a successful
            // import into a reported rollback that can no longer be performed.
            committed = true;
            if (pending != null) {
                foreach (int pendingSlot in pending.Keys.Select(NormalizePositionSlot).Distinct()) {
                    string stateSlotName = GetStartPosStateSlotName(areaSid, pendingSlot, fileSlot);
                    RunPostCommitCleanup(
                        () => AkronStartPosPersistence.Cancel(stateSlotName),
                        "cancel pending slot " + pendingSlot.ToString(CultureInfo.InvariantCulture));
                    if (!deferredCleanupSlots.Contains(pendingSlot)) {
                        RunPostCommitCleanup(
                            () => AkronSaveLoadService.ClearRuntimeState(stateSlotName),
                            "release pending slot " + pendingSlot.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            foreach (Action cleanup in deferredCleanup) {
                RunPostCommitCleanup(cleanup, "release replaced slot");
            }
        }

        private static void RunPostCommitCleanup(Action cleanup, string operation) {
            try {
                cleanup();
            } catch (Exception exception) {
                Logger.Log(LogLevel.Warn, nameof(AkronActions),
                    "Post-commit StartPos cleanup failed during " + operation + ": " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public void Dispose() {
            if (!committed && pending != null) {
                PendingStartPositionsByFileAndMap[pendingKey] = pending;
            }
        }
    }

    private static void EnsureStartPositionsLoaded(Level level) {
        if (level == null || AkronModule.Session == null) {
            return;
        }

        string areaSid = GetAreaSid(level);
        if (!string.Equals(AkronModule.Session.LoadedStartPositionsAreaSid, areaSid, StringComparison.Ordinal)) {
            LoadStartPositionsForLevel(level);
        }
    }

    private static bool PersistStartPos(
        int slot,
        AkronStartPos startPos,
        int fileSlot,
        AkronModuleSaveData saveData
    ) {
        if (startPos == null || !IsOriginatingSaveFileActive(fileSlot, saveData)) {
            return false;
        }

        string areaSid = NormalizeAreaSid(startPos.AreaSid);
        if (string.IsNullOrWhiteSpace(areaSid)) {
            return false;
        }

        Dictionary<string, AkronPersistedStartPosMap> maps = saveData.StartPositionsByMap;
        AkronPersistedStartPosMap previousMap = null;
        bool hadMap = maps != null && maps.TryGetValue(areaSid, out previousMap);
        maps ??= saveData.StartPositionsByMap = new Dictionary<string, AkronPersistedStartPosMap>(StringComparer.Ordinal);
        AkronPersistedStartPosMap map = GetOrCreatePersistedStartPosMap(saveData, areaSid);
        int normalizedSlot = NormalizePositionSlot(slot);
        bool hadSlot = map.Slots.TryGetValue(normalizedSlot, out AkronPersistedStartPos previousStartPos);
        map.Slots[normalizedSlot] = ToPersistedStartPos(startPos);
        if (SaveAkronStartPosData()) {
            return true;
        }

        if (hadSlot) {
            map.Slots[normalizedSlot] = previousStartPos;
        } else {
            map.Slots.Remove(normalizedSlot);
        }
        if (!hadMap) {
            maps.Remove(areaSid);
        } else if (!ReferenceEquals(map, previousMap)) {
            maps[areaSid] = previousMap;
        }
        return false;
    }

    private static void RemovePersistedStartPos(string areaSid, int slot) {
        Dictionary<string, AkronPersistedStartPosMap> maps = AkronModule.SaveData?.StartPositionsByMap;
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (maps == null || string.IsNullOrWhiteSpace(normalizedAreaSid) || !maps.TryGetValue(normalizedAreaSid, out AkronPersistedStartPosMap map)) {
            return;
        }

        int normalizedSlot = NormalizePositionSlot(slot);
        ClearStartPosRuntimeState(normalizedAreaSid, normalizedSlot);
        if (map.Slots != null) {
            map.Slots.Remove(normalizedSlot);
        }
        if (map.Slots == null || map.Slots.Count == 0) {
            maps.Remove(normalizedAreaSid);
        }
        SaveAkronStartPosData();
    }

    private static void ClearStartPosRuntimeState(string areaSid, int slot) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (string.IsNullOrWhiteSpace(normalizedAreaSid)) {
            return;
        }

        string stateSlotName = GetStartPosStateSlotName(normalizedAreaSid, slot);
        AkronStartPosPersistence.Cancel(stateSlotName);
        AkronSaveLoadService.ClearRuntimeState(stateSlotName);
    }

    private static void DiscardStartPosRuntimeStateMemory(string areaSid, int slot) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (string.IsNullOrWhiteSpace(normalizedAreaSid)) {
            return;
        }

        string stateSlotName = GetStartPosStateSlotName(normalizedAreaSid, slot);
        AkronStartPosPersistence.Cancel(stateSlotName);
        AkronSaveLoadService.ClearRuntimeStateExceptPersistentSnapshot(stateSlotName);
    }

    private static AkronPersistedStartPosMap GetOrCreatePersistedStartPosMap(
        AkronModuleSaveData saveData,
        string areaSid
    ) {
        if (saveData == null) {
            return new AkronPersistedStartPosMap();
        }
        saveData.StartPositionsByMap ??= new Dictionary<string, AkronPersistedStartPosMap>();
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (!saveData.StartPositionsByMap.TryGetValue(normalizedAreaSid, out AkronPersistedStartPosMap map) || map == null) {
            map = new AkronPersistedStartPosMap();
            saveData.StartPositionsByMap[normalizedAreaSid] = map;
        }
        map.Slots ??= new Dictionary<int, AkronPersistedStartPos>();
        return map;
    }

    private static Dictionary<int, AkronPersistedStartPos> GetPersistedStartPositions(string areaSid) {
        Dictionary<string, AkronPersistedStartPosMap> maps = AkronModule.SaveData?.StartPositionsByMap;
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (maps == null ||
            string.IsNullOrWhiteSpace(normalizedAreaSid) ||
            !maps.TryGetValue(normalizedAreaSid, out AkronPersistedStartPosMap map) ||
            map?.Slots == null) {
            return new Dictionary<int, AkronPersistedStartPos>();
        }

        return map.Slots;
    }

    private static Dictionary<int, AkronStartPos> BuildRuntimeStartPositions(string areaSid, Dictionary<int, AkronPersistedStartPos> persisted) {
        Dictionary<int, AkronStartPos> startPositions = new Dictionary<int, AkronStartPos>();
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        foreach (KeyValuePair<int, AkronPersistedStartPos> pair in persisted ?? new Dictionary<int, AkronPersistedStartPos>()) {
            AkronPersistedStartPos entry = pair.Value;
            if (entry == null) {
                continue;
            }

            int slot = NormalizePositionSlot(pair.Key);
            string entryAreaSid = string.IsNullOrWhiteSpace(entry.AreaSid) ? normalizedAreaSid : NormalizeAreaSid(entry.AreaSid);
            string stateSlotName = GetStartPosStateSlotName(entryAreaSid, slot);
            if (!AkronSaveLoadService.HasRuntimeState(stateSlotName)) {
                continue;
            }

            startPositions[slot] = new AkronStartPos {
                Position = new Vector2(entry.X, entry.Y),
                Room = entry.Room ?? string.Empty,
                AreaSid = entryAreaSid,
                UsesSpawnConfig = entry.UsesSpawnConfig,
                Dashes = entry.Dashes,
                StaminaPercent = entry.StaminaPercent,
                Facing = entry.Facing,
                Idle = entry.Idle,
                Grab = entry.Grab,
                StateSlotName = stateSlotName
            };
        }

        return startPositions;
    }

    private static AkronPersistedStartPos ToPersistedStartPos(AkronStartPos startPos) {
        return new AkronPersistedStartPos {
            X = startPos.Position.X,
            Y = startPos.Position.Y,
            Room = startPos.Room ?? string.Empty,
            AreaSid = NormalizeAreaSid(startPos.AreaSid),
            UsesSpawnConfig = startPos.UsesSpawnConfig,
            Dashes = startPos.Dashes,
            StaminaPercent = startPos.StaminaPercent,
            Facing = startPos.Facing,
            Idle = startPos.Idle,
            Grab = startPos.Grab
        };
    }

    internal static bool SaveAkronStartPosData() {
        try {
            if (AkronModule.Instance == null || SaveData.Instance == null) {
                return false;
            }

            int fileSlot = SaveData.Instance.FileSlot;
            byte[] serialized = AkronModule.Instance.SerializeSaveData(fileSlot);
            if (serialized == null) {
                return false;
            }
            AkronModule.Instance.WriteSaveData(fileSlot, serialized);
            byte[] persisted = AkronModule.Instance.ReadSaveData(fileSlot);
            if (persisted == null || !persisted.SequenceEqual(serialized)) {
                Logger.Log(LogLevel.Warn, nameof(AkronActions),
                    "Failed to verify persisted StartPos metadata after writing it.");
                return false;
            }
            return true;
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronActions), "Failed to save persisted StartPos metadata: " + exception.Message);
            return false;
        }
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

    private static int WrapStartPosSlot(int slot) {
        int count = AkronModuleSettings.ClampStartPosSelectableSlotCount(AkronModule.Settings.StartPosSlotCount);
        int zeroBased = (slot - MinPositionSlot) % count;
        if (zeroBased < 0) {
            zeroBased += count;
        }
        return MinPositionSlot + zeroBased;
    }

    private static string GetStartPosStateSlotName(int slot) {
        return GetStartPosStateSlotName(GetLoadedAreaSid(), slot);
    }

    internal static string GetStartPosStateSlotName(string areaSid, int slot) {
        return GetStartPosStateSlotName(areaSid, slot, GetCurrentFileSlot());
    }

    internal static string GetStartPosStateSlotName(string areaSid, int slot, int fileSlot) {
        return StartPosStateSlotPrefix +
               "File " + fileSlot.ToString(CultureInfo.InvariantCulture) + " " +
               SanitizeStartPosKey(areaSid) + " " +
               NormalizePositionSlot(slot).ToString(CultureInfo.InvariantCulture);
    }

    private static int GetCurrentFileSlot() {
        return SaveData.Instance?.FileSlot ?? -1;
    }

    private static string BuildPendingStartPosKey(int fileSlot, string areaSid) {
        return fileSlot.ToString(CultureInfo.InvariantCulture) + "|" + NormalizeAreaSid(areaSid);
    }

    private static bool IsOriginatingSaveFileActive(int fileSlot, AkronModuleSaveData saveData) {
        return saveData != null &&
               SaveData.Instance?.FileSlot == fileSlot &&
               AkronModule.Instance != null &&
               ReferenceEquals(AkronModule.SaveData, saveData);
    }

    internal static void RefreshStartPositionsAfterSnapshotImport(string areaSid, AkronModuleSession targetSession) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (Engine.Scene is Level level && string.Equals(GetAreaSid(level), normalizedAreaSid, StringComparison.Ordinal)) {
            LoadStartPositionsForLevel(level);
        } else if (targetSession != null) {
            targetSession.LoadedStartPositionsAreaSid = normalizedAreaSid;
            targetSession.StartPositions = BuildRuntimeStartPositions(normalizedAreaSid, GetPersistedStartPositions(normalizedAreaSid));
        }
    }

    private static string GetAreaSid(Level level) {
        return NormalizeAreaSid(level?.Session?.Area.GetSID());
    }

    private static string GetLoadedAreaSid() {
        if (Engine.Scene is Level level) {
            return GetAreaSid(level);
        }

        return NormalizeAreaSid(AkronModule.Session?.LoadedStartPositionsAreaSid);
    }

    private static string NormalizeAreaSid(string areaSid) {
        return (areaSid ?? string.Empty).Trim();
    }

    private static string SanitizeStartPosKey(string value) {
        string normalized = NormalizeAreaSid(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            normalized = "unknown";
        }

        // Area SIDs are user-controlled map identifiers. Use a reversible byte
        // encoding instead of character replacement so two distinct SIDs cannot
        // collapse into the same runtime slot name or snapshot file path.
        return string.Concat(Encoding.UTF8
            .GetBytes(normalized)
            .Select(valueByte => valueByte.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static int NormalizePositionSlot(int slot) {
        return Math.Min(Math.Max(slot, MinPositionSlot), MaxPositionSlot);
    }
}
