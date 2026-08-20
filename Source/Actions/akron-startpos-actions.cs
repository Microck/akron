using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    // A toast is a few lines on screen. Reconstruction failures carry whole graph
    // paths, so the toast shows the head of the reason and the log keeps all of it.
    internal const int MaxStartPosFailureToastLength = 180;

    // Level.Update reads this generation before processing pre-update actions.
    // A successful Set or Load increments it so the exact StartPos frame can
    // render before Celeste advances the room simulation.
    internal static ulong StartPosFrameGeneration { get; private set; }
    private static bool startPosCaptureInProgress;
    private static readonly Dictionary<string, Dictionary<int, AkronStartPos>> PendingStartPositionsByFileAndMap =
        new Dictionary<string, Dictionary<int, AkronStartPos>>(StringComparer.Ordinal);

    // GetStartPositions is called several times per rendered frame by the HUD label, the
    // label obstruction pass and the overlay rows. Rebuilding it costs a map-wide room
    // order dictionary, a LINQ sort, and a HasRuntimeState probe per slot, so the result
    // is cached. The cache is only reused while every input is provably unchanged:
    //   - the same AkronModuleSession instance (a new session means a new run)
    //   - the same area SID (the room order and the area filter both derive from it)
    //   - the catalog revision (any add, remove, or wholesale replacement of
    //     Session.StartPositions)
    //   - the runtime-slot revision (warm state appearing or being dropped)
    //   - the snapshot-existence revision (a disk snapshot written or deleted)
    // Room changes need no key of their own: nothing in the built list depends on the
    // current room, and a room load reloads the catalog, which bumps the revisions.
    private static long startPosCatalogRevision;
    private static AkronModuleSession cachedStartPosListSession;
    private static string cachedStartPosListAreaSid;
    private static long cachedStartPosListCatalogRevision = -1;
    private static long cachedStartPosListRuntimeRevision = -1;
    private static long cachedStartPosListSnapshotRevision = -1;
    private static IReadOnlyList<AkronStartPosEntry> cachedStartPosList;
    // Save file and map the prewarmed snapshot documents were read for.
    private static string prewarmedSnapshotScope;

    // Exposed so the cache-invalidation tests can observe that a catalog mutation was
    // seen, and so the counter reads the same way as the two revisions it is compared
    // against in GetStartPositions.
    internal static long StartPosCatalogRevision => startPosCatalogRevision;

    internal static void MarkStartPosCatalogChanged() {
        startPosCatalogRevision++;
        cachedStartPosList = null;
    }

    // A Set has to be atomic with respect to whatever the slot already held. Two of the
    // four pieces of a committed StartPos are already transactional: the disk snapshot
    // goes through PreparedSnapshotInstall, which moves the previous file aside and puts
    // it back on rollback, and the persisted metadata is only written once the snapshot is
    // in place. The other two - the warm clone with its fresh-room baseline, and the
    // in-memory catalog entry - are replaced the instant Set runs, long before the restart
    // copy exists. This record parks them so a Set whose restart copy never lands leaves
    // the slot exactly as it found it.
    //
    // Keyed by state slot name rather than by persistence generation on purpose: what is
    // parked is the last state that actually committed, so a second Set arriving while the
    // first is still writing inherits the same record instead of parking its own
    // uncommitted capture on top of it.
    private sealed class StartPosRollback {
        public string ParkedRuntimeStateName;
        public string ParkedFreshBaselineKey;
        public AkronStartPos PreviousEntry;
        public AkronStartPos PublishedEntry;
        public bool HadCommittedState;
    }

    private static readonly Dictionary<string, StartPosRollback> StartPosRollbacks =
        new Dictionary<string, StartPosRollback>(StringComparer.Ordinal);

    private static StartPosRollback BeginStartPosRollback(int slot, string stateSlotName, out bool ownsRollback) {
        if (StartPosRollbacks.TryGetValue(stateSlotName, out StartPosRollback existing)) {
            // An earlier Set on this slot is still writing. Its record already holds the
            // last committed state, which is what any failure has to restore, and that
            // earlier Set keeps ownership of it: a capture that fails before publishing
            // anything has changed nothing, so it must leave the record alone.
            ownsRollback = false;
            return existing;
        }

        ownsRollback = true;
        AkronModuleSession session = AkronModule.TryGetSession();
        AkronStartPos previousEntry = null;
        if (session?.StartPositions != null) {
            session.StartPositions.TryGetValue(NormalizePositionSlot(slot), out previousEntry);
        }

        StartPosRollback rollback = new StartPosRollback {
            PreviousEntry = previousEntry,
            ParkedRuntimeStateName = AkronSaveLoadService.ParkRuntimeState(stateSlotName),
            ParkedFreshBaselineKey = AkronStartPosPersistence.ParkRuntimeFreshBaseline(stateSlotName)
        };
        // Occupancy is decided conservatively and only once, here: any one of these means
        // the slot held something a failure must not destroy. Guessing "empty" would send
        // the failure down the discard path, which deletes the snapshot and the metadata.
        rollback.HadCommittedState = previousEntry != null ||
                                     rollback.ParkedRuntimeStateName != null ||
                                     AkronStartPosReconstruction.HasSnapshot(stateSlotName);
        StartPosRollbacks[stateSlotName] = rollback;
        return rollback;
    }

    // The new state is the committed state now, so the previous clone and baseline are
    // released. This is the release StoreRuntimeSlot used to perform at Set time, moved to
    // the point where the replacement is actually durable.
    private static void ReleaseStartPosRollback(string stateSlotName) {
        if (string.IsNullOrWhiteSpace(stateSlotName) ||
            !StartPosRollbacks.Remove(stateSlotName, out StartPosRollback rollback)) {
            return;
        }

        AkronSaveLoadService.DiscardParkedRuntimeState(rollback.ParkedRuntimeStateName);
        AkronStartPosPersistence.DiscardParkedRuntimeFreshBaseline(rollback.ParkedFreshBaselineKey);
    }

    // Puts the slot back the way it was before the Set that owns this record. A null reason
    // means the caller already reported the failure itself.
    //
    // previousSnapshotLost is only read when a reason is being reported: it says the move
    // that would have put the slot's snapshot file back failed, so nothing restored below
    // can be counted on to survive this session - everything it puts back is memory.
    private static void RestoreStartPosRollback(
        int fileSlot,
        int slot,
        AkronStartPos startPos,
        string stateSlotName,
        string reason,
        bool previousSnapshotLost = false
    ) {
        if (string.IsNullOrWhiteSpace(stateSlotName) ||
            !StartPosRollbacks.Remove(stateSlotName, out StartPosRollback rollback)) {
            return;
        }

        int normalizedSlot = NormalizePositionSlot(slot);
        AkronSaveLoadService.RestoreParkedRuntimeState(rollback.ParkedRuntimeStateName, stateSlotName);
        AkronStartPosPersistence.RestoreParkedRuntimeFreshBaseline(rollback.ParkedFreshBaselineKey, stateSlotName);

        AkronModuleSession session = AkronModule.TryGetSession();
        if (rollback.PublishedEntry != null &&
            session?.StartPositions != null &&
            session.StartPositions.TryGetValue(normalizedSlot, out AkronStartPos current) &&
            ReferenceEquals(current, rollback.PublishedEntry)) {
            // Only this Set's own entry is taken back out. Anything else sitting in the
            // slot belongs to a later action - a clear, a setup-pack import - that already
            // owns the slot and must not be overwritten with older metadata.
            if (rollback.PreviousEntry != null) {
                session.StartPositions[normalizedSlot] = rollback.PreviousEntry;
            } else {
                session.StartPositions.Remove(normalizedSlot);
            }
            MarkStartPosCatalogChanged();
        }
        // The pending marker is what stops a Load from pairing the new metadata with the
        // previous snapshot, so it is cleared last, once the previous state is back.
        RemovePendingStartPos(fileSlot, slot, startPos);

        if (reason == null) {
            return;
        }
        string message = DescribeFailedStartPosReplacement(normalizedSlot, reason, previousSnapshotLost);
        AkronLog.Warn(nameof(AkronActions), message);
        Engine.Scene?.Add(new AkronToast(message));
    }

    // What a failed Set over an occupied slot leaves on screen.
    //
    // The second sentence is the one the player acts on, so it has to describe the slot
    // they are left with rather than the outcome this path was written for. The previous
    // position, its warm clone and its metadata are back either way; what the lost case
    // cannot promise is the saved room state behind them, which is the only part that
    // survives leaving the map. It says so plainly rather than hedging: a re-set the
    // player did not need costs one Set, and a slot they trusted costs the run.
    //
    // Split out from the toast because the toast needs a scene: the completion path that
    // reaches it cannot run outside the game, and both sentences can be read back here.
    internal static string DescribeFailedStartPosReplacement(int slot, string reason, bool previousSnapshotLost) {
        string slotText = slot.ToString(CultureInfo.InvariantCulture);
        return "StartPos " + slotText + " was not replaced because " + reason +
               (previousSnapshotLost
                   ? ". The previous StartPos " + slotText + " could not be put back either, so it works " +
                     "until you leave this map and then has to be set again."
                   : ". The previous StartPos " + slotText + " was kept.");
    }

    // Cancel invalidates the generation whose completion would otherwise run the rollback,
    // so the parked previous state has to be released at the same moment. Every cancel in
    // this file goes through here; releasing separately would leave a record that a later
    // Set on the same slot could restore over state the player already replaced or cleared.
    private static void CancelStartPosPersistence(string stateSlotName) {
        AkronStartPosPersistence.Cancel(stateSlotName);
        ReleaseStartPosRollback(stateSlotName);
    }

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
        // Asked before the clone rather than after it, because a clone this Set cannot
        // afford must not be made at all. This only fires on a map whose slots are heavy
        // enough to fill the whole warm budget while every one of them is still being
        // written to disk, which is the one state where nothing can be dropped to make
        // room. Letting the player through here is how the game runs out of memory.
        if (AkronSaveLoadService.WarmStartPosBudgetIsBlocked()) {
            Engine.Scene?.Add(new AkronToast(
                "StartPos slots on this map are using all " +
                (AkronSaveLoadService.MaxWarmStartPosBytes / (1024L * 1024L)).ToString(CultureInfo.InvariantCulture) +
                " MB Akron keeps them in. Pause for a moment to let the restart copies finish, or clear a slot."));
            completion?.Invoke(false);
            return;
        }
        startPosCaptureInProgress = true;
        // A Set freezes the game thread for a full clone. Nothing speculative should be
        // reading a snapshot file or allocating a document graph across that window.
        AkronStartPosPersistence.CancelPrewarm();

        int slot = AkronModule.Settings.ActiveStartPosSlot;
        int fileSlot = GetCurrentFileSlot();
        string areaSid = GetAreaSid(level);
        string stateSlotName = GetStartPosStateSlotName(areaSid, slot, fileSlot);
        AkronSaveLoadResult saveResult = AkronSaveLoadResult.Failed;
        Stopwatch captureTimer = Stopwatch.StartNew();
        StartPosPlayerSnapshot playerSnapshot = null;
        Vector2? originalRespawnPoint = level.Session.RespawnPoint;
        Vector2 clampedPosition = ClampToRoom(level, position);

        // Park whatever this slot already holds before the capture overwrites it. Every
        // exit below either commits the replacement or restores what was parked.
        StartPosRollback rollback = BeginStartPosRollback(slot, stateSlotName, out bool ownsRollback);

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
            // The capture threw, so nothing was published yet and the throw is the report.
            if (ownsRollback) {
                RestoreStartPosRollback(fileSlot, slot, null, stateSlotName, reason: null);
            }
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
            // Nothing was published, and the toast below already names the failure.
            if (ownsRollback) {
                RestoreStartPosRollback(fileSlot, slot, null, stateSlotName, reason: null);
            }
            Engine.Scene?.Add(new AkronToast("StartPos capture failed: " + saveResult + "."));
            completion?.Invoke(false);
            return;
        }

        captureTimer.Stop();
        AkronLog.Diagnostic(nameof(AkronActions),
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
        long persistenceGeneration;
        try {
            PublishPendingStartPos(fileSlot, slot, startPos);
            rollback.PublishedEntry = startPos;
            startPosCaptureInProgress = false;
            // No save file means the metadata half of a restart copy can never be
            // written, so the copy cannot start and the Set below is rolled back.
            persistenceGeneration = AkronModule.SaveData == null
                ? 0
                : AkronStartPosPersistence.Enqueue(fileSlot, slot, startPos, stateSlotName);
        } catch {
            // Publishing and queueing are what hand this Set to the completion that would
            // otherwise own the rollback. A throw here means no completion will ever run,
            // so the parked state has to be put back now or it stays retained and a later
            // Set on this slot would inherit a record it does not own. The throw itself is
            // the report.
            startPosCaptureInProgress = false;
            if (ownsRollback) {
                RestoreStartPosRollback(fileSlot, slot, startPos, stateSlotName, reason: null);
            }
            completion?.Invoke(false);
            throw;
        }
        if (persistenceGeneration == 0) {
            // No restart copy will ever be produced for this capture, so the slot
            // cannot survive leaving the map. Roll the Set back now instead of handing
            // back a StartPos that stops working without explanation later.
            RollBackFailedStartPos(fileSlot, slot, startPos, "its restart copy could not start");
            completion?.Invoke(false);
            return;
        }

        // Run after the Set is published, so this capture counts as pending and is never
        // the clone dropped to pay for itself.
        TrimWarmStartPosSlotsAndReport();

        Engine.Scene?.Add(new AkronToast(toast));
        completion?.Invoke(true);
        if (!useSpawnConfig) {
            StartPosFrameGeneration++;
        }
    }

    // Drops the coldest warm clones the memory budget can no longer pay for, and says so
    // when it does. A dropped clone is not a lost slot - it loads from its restart copy
    // instead of from memory - but the player is about to notice one slot taking seconds
    // where the others are instant, so the toast explains it rather than leaving them to
    // wonder which slots went slow and why.
    private static void TrimWarmStartPosSlotsAndReport() {
        int droppedWarmSlots = AkronSaveLoadService.TrimWarmStartPosSlots(out long droppedBytes);
        if (droppedWarmSlots == 0) {
            return;
        }

        Engine.Scene?.Add(new AkronToast(
            droppedWarmSlots.ToString(CultureInfo.InvariantCulture) +
            (droppedWarmSlots == 1 ? " StartPos slot on this map is" : " StartPos slots on this map are") +
            " no longer held in memory (" +
            (droppedBytes / (1024d * 1024d)).ToString("F0", CultureInfo.InvariantCulture) +
            " MB). " +
            (droppedWarmSlots == 1 ? "It still loads, from disk." : "They still load, from disk.")));
    }

    internal static void CompletePersistentStartPosCapture(
        int fileSlot,
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
            // A newer Set already owns this slot and published its own pending entry.
            // That generation owns the outcome; clearing anything here would destroy
            // state that is still being written.
            return;
        }

        AkronStartPosReconstruction.PreparedSnapshotInstall installedSnapshot = null;
        bool committed = false;
        string failureReason = null;
        try {
            if (!IsOriginatingSaveFileActive(fileSlot)) {
                failureReason = "the save file it belongs to is no longer open";
                return;
            }

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
                failureReason = string.IsNullOrWhiteSpace(persistError)
                    ? "the restart copy failed"
                    : persistError;
                return;
            }

            if (!PersistStartPos(slot, startPos, fileSlot)) {
                failureReason = "its restart metadata could not be saved";
                return;
            }

            // Commit order: the snapshot file is in place, its metadata is written, and
            // only then does the install stop being reversible. Everything from here to
            // the end of the block runs on the game thread without an engine boundary, so
            // nothing can observe the slot half-committed.
            installedSnapshot.Commit();
            RemovePendingStartPos(fileSlot, slot, startPos);
            ReleaseStartPosRollback(stateSlotName);
            committed = true;
            AkronLog.Diagnostic(nameof(AkronActions),
                "Restart-safe StartPos copy finished in " +
                elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms.");
            // This slot has just become droppable, and it may be the first one that is.
            // A player who sets slot after slot without pausing leaves every clone
            // pending, so the Set-time trim has nothing it is allowed to drop and the
            // population sits over budget until something makes a slot droppable again.
            // This is that moment, so the trim runs here too rather than waiting for a
            // Set that may not come.
            TrimWarmStartPosSlotsAndReport();
        } finally {
            try {
                // Dispose rolls the staged install back, so it has to run before the
                // slot state is deleted. The rollback reports its own file-move
                // failures rather than throwing them, so nothing here throws today;
                // the nested finally is what keeps the cleanup below reachable
                // without this method having to depend on that.
                installedSnapshot?.Dispose();
            } finally {
                // Every exit except the committed one leaves this slot without the restart
                // copy this Set was making; what it keeps is whatever it had before, put
                // back above. Rolling back from the finally rather than from each exit is
                // what keeps a future exit path from reintroducing an unloadable pending
                // entry.
                if (!committed) {
                    // Read after Dispose, because Dispose is where the rollback of a
                    // staged install that did land runs, and a rollback that could not
                    // put the previous snapshot back changes what this slot is left with.
                    RollBackFailedStartPos(
                        fileSlot,
                        slot,
                        startPos,
                        failureReason ?? "the restart copy did not finish",
                        installedSnapshot?.PreviousSnapshotLost == true);
                }
            }
        }
    }

    // The single failure exit for a Set. Which of the two outcomes applies is decided by
    // what the slot held when the Set began, recorded then rather than re-derived now: by
    // this point the new capture has already replaced the in-memory state, so "was this
    // slot occupied" no longer has a truthful answer.
    //
    //  - the slot held a StartPos: it keeps its previous metadata, warm clone, fresh-room
    //    baseline and snapshot, and stays loadable. The failed Set is the no-op.
    //  - the slot was empty: it ends up empty again, reported with the reason.
    //
    // previousSnapshotLost is the one exception to the first outcome, and it defaults to
    // false because only a failure that staged a snapshot install can cause it: the
    // install rolled back and its move to put the slot's snapshot file back failed, so
    // the slot keeps everything but what it needs to load after this session.
    private static void RollBackFailedStartPos(
        int fileSlot,
        int slot,
        AkronStartPos startPos,
        string reason,
        bool previousSnapshotLost = false
    ) {
        string stateSlotName = startPos?.StateSlotName;
        if (!string.IsNullOrWhiteSpace(stateSlotName) &&
            StartPosRollbacks.TryGetValue(stateSlotName, out StartPosRollback rollback) &&
            rollback.HadCommittedState) {
            RestoreStartPosRollback(fileSlot, slot, startPos, stateSlotName, reason, previousSnapshotLost);
            return;
        }

        // Nothing to keep. Release the parked handles here so the discard below stays a
        // plain removal of a slot that never had committed state.
        ReleaseStartPosRollback(stateSlotName);
        DiscardFailedStartPos(fileSlot, slot, startPos, reason);
    }

    // A StartPos whose restart copy failed has no durable state. It keeps working from
    // its memory clone until the player leaves the map, then stops with no explanation.
    // Removing it here, while the player can still act on it, is the only honest
    // outcome: the alternative is a slot that silently lies about being saved.
    //
    // Only reached for a slot that had no committed state when the Set began, so there is
    // nothing here worth preserving. A Set over an occupied slot rolls back instead.
    private static void DiscardFailedStartPos(
        int fileSlot,
        int slot,
        AkronStartPos startPos,
        string reason
    ) {
        if (startPos == null) {
            return;
        }

        int normalizedSlot = NormalizePositionSlot(slot);
        RemovePendingStartPos(fileSlot, slot, startPos);
        CancelStartPosPersistence(startPos.StateSlotName);
        // Drops the memory clone, its fresh-room baseline and any snapshot left on this
        // slot name. Keeping a snapshot would pair it with newer position metadata, which
        // is exactly the mismatch the load path must never see.
        AkronSaveLoadService.ClearRuntimeState(startPos.StateSlotName);

        if (IsOriginatingSaveFileActive(fileSlot)) {
            RemovePersistedStartPos(NormalizeAreaSid(startPos.AreaSid), normalizedSlot);
            AkronModuleSession session = AkronModule.TryGetSession();
            if (session?.StartPositions != null &&
                session.StartPositions.TryGetValue(normalizedSlot, out AkronStartPos current) &&
                string.Equals(current?.StateSlotName, startPos.StateSlotName, StringComparison.Ordinal)) {
                session.StartPositions.Remove(normalizedSlot);
                MarkStartPosCatalogChanged();
            }
            if (session != null && session.LastLoadedStartPosSlot == normalizedSlot) {
                session.LastLoadedStartPosSlot = 0;
            }
        }

        string message = "StartPos " + normalizedSlot.ToString(CultureInfo.InvariantCulture) +
                         " was removed because " + reason + ". Set it again.";
        AkronLog.Warn(nameof(AkronActions), message);
        Engine.Scene?.Add(new AkronToast(message));
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
            MarkStartPosCatalogChanged();
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
        // Shutdown and module unload abandon every in-flight Set, so nothing will ever run
        // their rollbacks. Release the parked clones instead of leaving them retained.
        foreach (string stateSlotName in StartPosRollbacks.Keys.ToArray()) {
            ReleaseStartPosRollback(stateSlotName);
        }
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

    // Every StartPos slot set before the snapshot format was bumped disappears from the
    // slot list rather than failing to load: BuildRuntimeStartPositions keeps only slots
    // HasRuntimeState answers for, and a slot whose restart copy is under the previous
    // format's name has none. So the message a player actually gets after an update is
    // this one, not the one on the load path, and "No StartPos saved in slot 3" would
    // send them looking for a slot they know they set.
    //
    // The catalog is what says the slot was set, not the leftover file. Both answer
    // today, but only the catalog keeps answering: the superseded file is swept once
    // nothing can read it, and evidence that a player set a slot cannot live in a file
    // this build intends to delete. The catalog also survives the next bump, and every
    // one after it, for free.
    //
    // The catalog carries the reason as well as the fact, because the entry records the
    // format its state was written under. That is what separates the two sentences
    // below. A slot emptied by a format move is named as one, which is the sentence
    // worth showing: it says what happened and what to do. A slot whose state went
    // missing with the format unchanged - a file deleted by hand, a backup restored
    // over the folder, a write that never landed - gets a sentence that claims nothing
    // about the cause, because there is nothing here that knows it.
    internal static string DescribeMissingStartPos(Level level, int slot) {
        return DescribeMissingStartPos(slot, GetPersistedStartPositions(GetAreaSid(level)));
    }

    internal static string DescribeMissingStartPos(
        int slot,
        IReadOnlyDictionary<int, AkronPersistedStartPos> persisted
    ) {
        if (persisted == null ||
            !persisted.TryGetValue(NormalizePositionSlot(slot), out AkronPersistedStartPos entry)) {
            return "No StartPos saved in slot " + slot.ToString(CultureInfo.InvariantCulture) + ".";
        }

        return WasSavedByAnOlderAkron(entry)
            ? "StartPos " + slot.ToString(CultureInfo.InvariantCulture) +
              " was saved by an older Akron that built rooms differently, so it cannot be loaded. Set it again."
            : "StartPos " + slot.ToString(CultureInfo.InvariantCulture) +
              " was set, but the state behind it is missing. Set it again.";
    }

    // True when the slot's room state was written under a saved-state format older than
    // the one this build reads, which is the one cause of an emptied slot this build can
    // name with certainty: such a state sits under a file name this build never builds,
    // and would be refused at the header even if it did.
    //
    // Compared as a number rather than for equality on purpose. A slot written by a
    // newer build the player has since downgraded from is unreadable here too, and the
    // sweep deliberately leaves its file alone; calling that slot older would be wrong
    // in the one direction this message must never be wrong in.
    private static bool WasSavedByAnOlderAkron(AkronPersistedStartPos entry) {
        return entry != null &&
               ReadSnapshotFormatVersion(entry.SnapshotFormat) <
               ReadSnapshotFormatVersion(AkronReconstructionDocument.CurrentFormat);
    }

    // The trailing number in a saved-state format name: "akron-reconstruction-v9" is 9.
    // Read out of the name rather than written down so a format move needs no edit here.
    //
    // A name with no trailing number reads as 0, which is where an entry written before
    // the format was recorded lands. That is the right answer for it: the field arrived
    // in the release that moved the format, so anything without one is older.
    private static int ReadSnapshotFormatVersion(string format) {
        int end = format?.Length ?? 0;
        int start = end;
        while (start > 0 && format[start - 1] >= '0' && format[start - 1] <= '9') {
            start--;
        }

        // Bounded so a hand-edited name carrying a long run of digits cannot overflow
        // the comparison into reading as some other version.
        int digits = end - start;
        return digits > 0 && digits <= 9
            ? int.Parse(format.AsSpan(start, digits), NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
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
            Engine.Scene?.Add(new AkronToast(DescribeMissingStartPos(level, slot)));
            return;
        }
        if (!IsStartPosInArea(startPos, level.Session.Area.GetSID())) {
            Engine.Scene?.Add(new AkronToast("StartPos " + AkronModule.Settings.ActiveStartPosSlot + " belongs to " + startPos.AreaSid + "."));
            return;
        }

        AkronModule.ScheduleAfterStableEngineUpdate(() => {
            // The load runs one engine boundary after the key press. Both of these
            // used to return silently, which is indistinguishable from a dead hotkey.
            if (Engine.Scene != level) {
                Engine.Scene?.Add(new AkronToast("StartPos " + slot + " was not loaded: the scene changed."));
                return;
            }
            if (startPosCaptureInProgress) {
                Engine.Scene?.Add(new AkronToast("StartPos " + slot + " was not loaded: a capture is still finishing."));
                return;
            }

            if (!RestoreStartPos(
                level,
                startPos,
                "Loaded StartPos " + slot + ".",
                slot)) {
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
        CancelStartPosPersistence(GetStartPosStateSlotName(areaSid, clampedSlot, fileSlot));
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
        MarkStartPosCatalogChanged();
        RemovePersistedStartPos(areaSid, clampedSlot);
        if (AkronModule.Session.LastLoadedStartPosSlot == clampedSlot) {
            AkronModule.Session.LastLoadedStartPosSlot = 0;
        }
        Engine.Scene?.Add(new AkronToast("StartPos " + clampedSlot + " cleared."));
    }

    // The same hole DescribeMissingStartPos covers, one map up. A snapshot format bump
    // drops every slot on the map from the list at once, so Previous and Next find no
    // entries at all, and "No StartPos entries in this chapter." is what a player with a
    // map full of slots is told. The persisted metadata is what the bump does not touch,
    // so it is what can tell an emptied map apart from one that never had a slot.
    //
    // The format move is named only when every slot on the map was written under an
    // older format, which is what a move actually does: it takes the whole map at once.
    // One slot on the map that lost its state some other way makes the sentence false
    // for that slot, and this sentence covers them together, so a mixed map gets the one
    // that claims nothing about the cause.
    internal static string DescribeEmptyStartPosList(Level level) {
        return DescribeEmptyStartPosList(GetPersistedStartPositions(GetAreaSid(level)));
    }

    internal static string DescribeEmptyStartPosList(IReadOnlyDictionary<int, AkronPersistedStartPos> persisted) {
        if (persisted == null || persisted.Count == 0) {
            return "No StartPos entries in this chapter.";
        }

        return persisted.Values.All(WasSavedByAnOlderAkron)
            ? "This chapter's StartPos slots were saved by an older Akron that built rooms differently. Set them again."
            : "This chapter's StartPos slots were set, but the states behind them are missing. Set them again.";
    }

    public static void ShiftStartPos(Level level, int delta) {
        if (level == null || delta == 0 || !AkronModule.TryUse(AkronFeatureKind.StartPosTools)) {
            return;
        }

        IReadOnlyList<AkronStartPosEntry> entries = GetStartPositions(level);
        if (entries.Count == 0) {
            Engine.Scene?.Add(new AkronToast(DescribeEmptyStartPosList(level)));
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
        long runtimeRevision = AkronSaveLoadService.RuntimeStateRevision;
        long snapshotRevision = AkronStartPosReconstruction.SnapshotExistenceRevision;
        if (cachedStartPosList != null &&
            ReferenceEquals(cachedStartPosListSession, AkronModule.Session) &&
            string.Equals(cachedStartPosListAreaSid, areaSid, StringComparison.Ordinal) &&
            cachedStartPosListCatalogRevision == startPosCatalogRevision &&
            cachedStartPosListRuntimeRevision == runtimeRevision &&
            cachedStartPosListSnapshotRevision == snapshotRevision) {
            return cachedStartPosList;
        }

        Dictionary<string, int> roomOrder = BuildRoomOrder(level);
        List<AkronStartPosEntry> entries = AkronModule.Session.StartPositions
            .Where(pair => IsStartPosInArea(pair.Value, areaSid))
            .OrderBy(pair => RoomSortIndex(roomOrder, pair.Value.Room))
            .ThenBy(pair => pair.Value.Position.X)
            .ThenBy(pair => pair.Value.Position.Y)
            .ThenBy(pair => pair.Key)
            .Select(pair => new AkronStartPosEntry(NormalizePositionSlot(pair.Key), pair.Value))
            .ToList();

        // Callers now share one instance, so hand out a read-only view instead of the
        // list the cache holds.
        cachedStartPosList = new ReadOnlyCollection<AkronStartPosEntry>(entries);
        cachedStartPosListSession = AkronModule.Session;
        cachedStartPosListAreaSid = areaSid;
        cachedStartPosListCatalogRevision = startPosCatalogRevision;
        cachedStartPosListRuntimeRevision = runtimeRevision;
        cachedStartPosListSnapshotRevision = snapshotRevision;
        return cachedStartPosList;
    }

    public static string DescribeStartPosIndex(Level level) {
        IReadOnlyList<AkronStartPosEntry> entries = GetStartPositions(level);
        if (entries.Count == 0) {
            return "0/0";
        }

        int index = -1;
        for (int candidate = 0; candidate < entries.Count; candidate++) {
            if (entries[candidate].Slot == AkronModule.Settings.ActiveStartPosSlot) {
                index = candidate;
                break;
            }
        }
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

    // The deferred engine boundary that runs a StartPos load catches and logs every
    // exception (AkronModule.RunAfterEngineUpdateActions). Without this wrapper a
    // throwing restore is a several-second freeze followed by nothing at all, with no
    // message anywhere the player can see.
    private static bool RestoreStartPos(Level level, AkronStartPos startPos, string toast, int loadedSlot = 0, bool endPlacementForLoad = true) {
        try {
            return RestoreStartPosCore(level, startPos, toast, loadedSlot, endPlacementForLoad);
        } catch (Exception exception) {
            // A saved type that will not load is refused while the document is walked,
            // which happens outside the reconstruction graph's own handlers, so this is
            // where an uninstalled mod's refusal arrives. It carries the type it refused
            // and what the refusal is about, and the player message is built from both
            // exactly as for a returned failure.
            AkronReconstructionException refusal = exception as AkronReconstructionException;
            ReportStartPosLoadFailure(
                loadedSlot,
                exception.GetType().Name + ": " + exception.Message,
                refusal?.RefusedTypeName ?? string.Empty,
                refusal?.RefusedKind ?? AkronReconstructionRefusalKind.SavedObject);
            return false;
        }
    }

    // Restore failures reach the player through a toast, and reconstruction errors carry
    // full graph paths. Keep the whole reason in the log and a readable head in the toast.
    //
    // refusedTypeName is set only when the reconstruction graph refused a saved object,
    // and refusedKind says what that refusal was about. Between them the toast says which
    // mod the object came from, or that the room's map has changed, and what to do about
    // it - because the graph's own text, a path ten levels deep and thirty authenticity
    // flags, is written for whoever reads the log and the player cannot act on any of it.
    // The log keeps both: the full reason, then the sentence that went on screen.
    //
    // Neither parameter has a default. Both call sites hold a real kind, and a default
    // would let a future one report a map change as a missing mod without saying so.
    private static void ReportStartPosLoadFailure(
        int loadedSlot,
        string reason,
        string refusedTypeName,
        AkronReconstructionRefusalKind refusedKind
    ) {
        string slotLabel = loadedSlot > 0
            ? "StartPos " + loadedSlot.ToString(CultureInfo.InvariantCulture)
            : "StartPos";
        string message = slotLabel + " could not be loaded: " + reason;
        AkronLog.Warn(nameof(AkronActions), message);

        string refusal = AkronStartPosRefusal.Describe(slotLabel, refusedTypeName, refusedKind);
        if (refusal != null) {
            AkronLog.Warn(nameof(AkronActions), slotLabel + " load message shown: " + refusal);
        }
        // The cap covers the refusal sentence too, not only the diagnostic text. A type
        // name comes out of a snapshot file and the reader allows strings into the
        // megabytes, so a corrupt or hostile one would otherwise put all of it on screen.
        string toast = refusal ?? message;
        Engine.Scene?.Add(new AkronToast(
            toast.Length <= MaxStartPosFailureToastLength
                ? toast
                : toast.Substring(0, MaxStartPosFailureToastLength) + "... (see akron-current.log)"));
    }

    private static string DescribeRestoreFailure(AkronSaveLoadResult result) {
        string detail = AkronSaveLoadService.LastPersistentSnapshotError;
        if (!string.IsNullOrWhiteSpace(detail)) {
            return detail;
        }

        return result switch {
            AkronSaveLoadResult.NoState => "no saved state remains for this slot",
            AkronSaveLoadResult.Blocked =>
                "the game is paused, transitioning, or in a cutscene",
            AkronSaveLoadResult.SessionMismatch =>
                "the saved state belongs to a different map or save file",
            AkronSaveLoadResult.BrokerUnavailable => "the savestate broker is unavailable",
            _ => "the saved state could not be rebuilt"
        };
    }

    private static bool RestoreStartPosCore(Level level, AkronStartPos startPos, string toast, int loadedSlot, bool endPlacementForLoad) {
        ClearStartPosInputWait();
        bool restoreRespawnAtStartPos = AkronModule.Settings.RespawnAtStartPos;
        AkronModule.Settings.RespawnAtStartPos = false;
        try {
            if (!RestoreStartPosUnderPacingGate(level, startPos, toast, loadedSlot, endPlacementForLoad)) {
                return false;
            }
            // The level graph that was live a moment ago is now garbage, and every
            // death since the last load left Celeste's forced collection unpaid.
            // Settle it here, while the player is already waiting on a load they
            // asked for, instead of during the next retry. After the gate hold has
            // been released, so the prewarm worker stops at its next buffer fill
            // rather than growing the heap all the way through the collection.
            AkronEngineGarbageCollection.CollectDeferred();
        } finally {
            AkronModule.Settings.RespawnAtStartPos = restoreRespawnAtStartPos;
        }

        // Queued after the load, never inside it. Measured in game on Forsaken City
        // with four other slots placed, same snapshot files on both builds: queueing
        // inside the load's frozen window took the first load from 5222.5 +- 21.9 ms
        // (n=4) to 8220.1 +- 669.5 ms (n=4), +57%, and 69% worse with fifteen slots.
        // The same build with nothing to queue landed at 5183.3 +- 9.1 ms (n=3), so
        // every millisecond of that was the speculative reads competing with the
        // load's own parse rather than anything else the split changed. It is
        // contention, not waiting: the worker's summary line lands 0.4-0.6 s before
        // the restore line, and the runtime reports one workstation GC heap, so two
        // reflection-driven JSON parses allocate into it at once. The cost scaled
        // with bytes read in the window, 19-27 ms of first-load latency per MB.
        //
        // By the time this runs the gate is closed again, so the worker parks at its
        // first buffer fill and drains where the player is not in control: a pause,
        // the overworld or chapter select, a StartPos input wait. That converges more
        // slowly than reading inside the load did, which is the trade - the load the
        // player is waiting on runs alone.
        PrewarmOtherStartPosSnapshots(Engine.Scene as Level ?? level, loadedSlot);
        return true;
    }

    // Everything a load does while the game thread is frozen, with the snapshot pacing
    // gate held open for all of it.
    //
    // A load freezes the game thread for seconds: waiting for an outstanding restart
    // copy, reading the requested snapshot, then unloading and rebuilding the whole
    // room. The gate is held open so the one restart copy this load is blocked on can
    // run; nothing speculative is queued in here. Filling the prewarm queue inside this
    // window cost the first load 51-59% and is now done after the load returns - see
    // RestoreStartPosCore for the measurements.
    private static bool RestoreStartPosUnderPacingGate(
        Level level,
        AkronStartPos startPos,
        string toast,
        int loadedSlot,
        bool endPlacementForLoad
    ) {
        long prewarmHitsBeforeLoad = AkronStartPosReconstruction.PrewarmedSnapshotHits;
        // Abandon any speculative read before the gate opens, not after. A read left
        // over from an earlier load is parked while the player is in control, and
        // opening the gate would wake it straight into competition with the restart
        // copy this load is about to block on. A parked read checks the abandon
        // predicate on its next 25 ms wake, before it re-tests the gate, so opening
        // the gate underneath it does not release it.
        //
        // One narrow case is left, and it is bounded rather than closed: a load
        // started from the pause menu catches the worker already running, and Pace
        // has no reason to suspend, so the read only stops at the cancellation the
        // file stream polls. That is one gzip input buffer of parse - a few hundred
        // KB decompressed, single-digit milliseconds against a multi-second load.
        // Closing it completely means testing the abandon predicate on the Pace fast
        // path, which runs millions of times per capture.
        AkronStartPosPersistence.CancelPrewarm();
        using AkronStartPosPersistence.PacingGateHold gate = AkronStartPosPersistence.HoldPacingGateOpen();
        // A load that cannot come from memory has to come from the restart copy, and the
        // worker that writes it is stopped for as long as the player is in a level.
        // Refusing the slot is the worse answer: a Load is not gameplay, it is a pause
        // the player asked for and already waits seconds through. Finish that one copy
        // here. Runs before the catalog snapshot below so a completion applied during
        // the wait is not undone by the restore afterwards.
        if (!AkronSaveLoadService.WillRestoreFromRuntimeMemory(level, startPos.StateSlotName)) {
            AkronStartPosPersistence.FinishPendingRestartCopy(startPos.StateSlotName);
        }
        Dictionary<string, AkronPersistedStartPosMap> currentStartPositionsByMap =
            AkronModule.Instance == null ? null : AkronModule.SaveData?.StartPositionsByMap;
        if (endPlacementForLoad && !AkronModule.EndStartPosPlacementForLoad()) {
            AkronModule.Settings.StartPosMousePlacement = false;
        }
        Stopwatch restoreTimer = Stopwatch.StartNew();
        AkronSaveLoadResult restored;
        bool usedSnapshot;
        try {
            restored = AkronSaveLoadService.LoadRuntimeState(
                level, startPos.StateSlotName, allowDeadPlayer: true, out usedSnapshot);
        } finally {
            // Module save data is part of the gameplay rewind. The StartPos
            // catalog is not: it can contain slots created after this
            // snapshot, and those newer entries must remain loadable.
            RestoreStartPosCatalog(
                AkronModule.Instance == null ? null : AkronModule.SaveData,
                currentStartPositionsByMap);
        }
        restoreTimer.Stop();
        if (restored != AkronSaveLoadResult.Success) {
            ReportStartPosLoadFailure(
                loadedSlot,
                DescribeRestoreFailure(restored),
                AkronSaveLoadService.LastPersistentSnapshotRefusedTypeName,
                AkronSaveLoadService.LastPersistentSnapshotRefusedKind);
            return false;
        }
        ReportStartPosRestoreTiming(restoreTimer.Elapsed, usedSnapshot, prewarmHitsBeforeLoad);

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
        if (!string.IsNullOrWhiteSpace(toast)) {
            Engine.Scene?.Add(new AkronToast(toast));
        }
        return true;
    }

    // Once per load, at Diagnostic, which is the default logging level. The prewarm
    // cache had no log line and no counter at all, so two verification passes could
    // only guess at whether it had served a load by comparing wall-clock timings, and
    // a change that silently disabled it would have left every test green.
    private static void ReportStartPosRestoreTiming(
        TimeSpan elapsed,
        bool usedSnapshot,
        long prewarmHitsBeforeLoad
    ) {
        string line = "StartPos " + (usedSnapshot ? "cold" : "warm") + " restore finished in " +
                      elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms";
        if (usedSnapshot) {
            // TryLoadSnapshot is the only thing that takes from the cache and it only
            // runs on this thread, so a hit counted across the load is this load's.
            bool servedFromPrewarm =
                AkronStartPosReconstruction.PrewarmedSnapshotHits > prewarmHitsBeforeLoad;
            line += servedFromPrewarm
                ? "; the snapshot came from the prewarm cache"
                : "; the snapshot was read from disk";
        }
        AkronLog.Diagnostic(nameof(AkronActions),
            line + ". Prewarm holds " +
            AkronStartPosReconstruction.PrewarmedSnapshotCount.ToString(CultureInfo.InvariantCulture) +
            " slots, " +
            (AkronStartPosReconstruction.PrewarmedSnapshotBytes / (1024d * 1024d))
                .ToString("F1", CultureInfo.InvariantCulture) + " MB of the " +
            (AkronStartPosReconstruction.MaxPrewarmedSnapshotBytes / (1024d * 1024d))
                .ToString("F1", CultureInfo.InvariantCulture) + " MB budget.");
    }

    // A cold load spends most of its time turning the snapshot file into a document:
    // gzip plus a reflection-driven JSON parse, none of which touches the live scene.
    // That is the only part of a load that can run ahead of time, so queue every other
    // placed slot of this map for it. Every slot goes in the queue, not just the ones
    // near the loaded slot, because the queue costs nothing until the gate opens and a
    // map that never warms completely is the defect this replaced. The order puts the
    // nearest slots first, because the budget or the window can run out before the
    // queue does and the near ones are wanted soonest.
    private static void PrewarmOtherStartPosSnapshots(Level level, int loadedSlot) {
        Dictionary<int, AkronStartPos> startPositions = AkronModule.Session?.StartPositions;
        if (startPositions == null) {
            AkronStartPosPersistence.CancelPrewarm();
            return;
        }

        List<string> stateSlotNames = new List<string>();
        // Nearest slot first: after loading slot 4 the next press is far more often 3
        // or 5 than 1, and the budget can run out before the queue does.
        foreach (KeyValuePair<int, AkronStartPos> pair in startPositions
                     .Where(pair => pair.Key != loadedSlot && pair.Value != null)
                     .OrderBy(pair => Math.Abs(pair.Key - loadedSlot))
                     .ThenBy(pair => pair.Key)) {
            string stateSlotName = pair.Value.StateSlotName;
            // A slot that will still restore from memory never reads its snapshot, and a
            // slot whose restart copy is still pending is refused before the read. Neither
            // can use a prewarm. The memory check is session-aware on purpose: a slot left
            // over from before a chapter re-entry is in memory but is going to be rebuilt
            // from disk anyway, so it is exactly the one worth prewarming.
            if (AkronSaveLoadService.WillRestoreFromRuntimeMemory(level, stateSlotName) ||
                HasPendingStartPosState(stateSlotName) ||
                !AkronStartPosReconstruction.HasSnapshot(stateSlotName)) {
                continue;
            }
            stateSlotNames.Add(stateSlotName);
        }
        AkronStartPosPersistence.PrewarmSnapshots(stateSlotNames);
    }

    // Puts the StartPos catalog back after a savestate load has rewound Akron's module
    // state, and re-derives the in-session view onto whatever session object came out
    // of the restore.
    //
    // A savestate restore replaces AkronModule._SaveData and AkronModule._Session
    // wholesale - the native path at RestoreNativeSlot does it, and SpeedrunTool's
    // SupportModSessionAndSaveData does the same thing through the broker, which is
    // the path every shipped build actually takes. The StartPos catalog lives in
    // both: the persisted metadata in _SaveData.StartPositionsByMap and the
    // in-session view in _Session.StartPositions. Neither is gameplay state. Both can
    // hold slots created after the savestate was taken, and rewinding them makes
    // those slots unreachable - measured in game, a slot set after the savestate read
    // startpos-set: false with its snapshot and metadata both intact on disk, and
    // only came back at the next process start. Worse, the rewound persisted catalog
    // is what the next save file write would have persisted.
    //
    // The session view is rebuilt rather than carried across, because the session
    // object itself is replaced by the restore and the view has to land on the new
    // one. LoadStartPositionsForLevel derives it from the persisted metadata, which
    // is why the metadata is put back first.
    internal static void RestoreStartPosCatalogAfterStateLoad(
        Level level,
        Dictionary<string, AkronPersistedStartPosMap> catalogBeforeLoad
    ) {
        // Caught because the only caller runs this from a finally. A savestate load
        // that worked must not be reported as failed because the catalog rebuild
        // stumbled, and a load that threw must report its own exception rather than
        // this one. Nothing durable is at risk either way: the metadata on disk is
        // untouched and the next room load rebuilds the same view.
        try {
            if (catalogBeforeLoad != null) {
                RestoreStartPosCatalog(
                    AkronModule.Instance == null ? null : AkronModule.SaveData,
                    catalogBeforeLoad);
            }
            // The same Level instance survives both restore paths - each copies into
            // it rather than replacing it in the scene - so this is the level the
            // rebuilt catalog belongs to.
            LoadStartPositionsForLevel(level);
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronActions),
                "Could not rebuild the StartPos catalog after a savestate load: " + exception);
        }
    }

    internal static void RestoreStartPosCatalog(
        AkronModuleSaveData saveData,
        Dictionary<string, AkronPersistedStartPosMap> currentStartPositionsByMap
    ) {
        if (saveData == null) {
            return;
        }

        saveData.StartPositionsByMap = currentStartPositionsByMap ??
            new Dictionary<string, AkronPersistedStartPosMap>(StringComparer.Ordinal);
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

        // A room load is the only point where the game can notice that a player edited
        // the Saves folder from outside. Re-stat there instead of trusting the cache
        // forever; it costs one stat per slot per room load and nothing per frame.
        AkronStartPosReconstruction.ResetSnapshotExistenceCache();

        string areaSid = GetAreaSid(level);
        // Prewarmed documents belong to one map and one save file. Correctness does not
        // depend on this - every consumer re-checks the file it read from - but nothing
        // on another map will ever be asked for, so hold none of it.
        string prewarmScope = GetCurrentFileSlot().ToString(CultureInfo.InvariantCulture) + "|" + areaSid;
        if (!string.Equals(prewarmScope, prewarmedSnapshotScope, StringComparison.Ordinal)) {
            prewarmedSnapshotScope = prewarmScope;
            AkronStartPosPersistence.CancelPrewarm();
            AkronStartPosReconstruction.ResetPrewarmedSnapshots();
        }
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
                    CancelStartPosPersistence(GetStartPosStateSlotName(areaSid, pendingSlot, fileSlot));
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
                        () => CancelStartPosPersistence(stateSlotName),
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
                AkronLog.Warn(nameof(AkronActions),
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
        int fileSlot
    ) {
        if (startPos == null || !IsOriginatingSaveFileActive(fileSlot)) {
            return false;
        }

        // Whatever object currently owns the file is the one to write into. The Set
        // that queued this copy read a different instance, and holding on to that
        // one would write the StartPos into an orphan the game has already replaced.
        AkronModuleSaveData saveData = AkronModule.SaveData;
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
        CancelStartPosPersistence(stateSlotName);
        AkronSaveLoadService.ClearRuntimeState(stateSlotName);
    }

    private static void DiscardStartPosRuntimeStateMemory(string areaSid, int slot) {
        string normalizedAreaSid = NormalizeAreaSid(areaSid);
        if (string.IsNullOrWhiteSpace(normalizedAreaSid)) {
            return;
        }

        string stateSlotName = GetStartPosStateSlotName(normalizedAreaSid, slot);
        CancelStartPosPersistence(stateSlotName);
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
        // AkronModule.SaveData reads through Instance without checking it, the way
        // ReplaceAllStartPositions already has to allow for. The catalog is what says a
        // slot was ever set, so it is read from message paths that have to answer
        // without a game behind them.
        Dictionary<string, AkronPersistedStartPosMap> maps =
            AkronModule.Instance == null ? null : AkronModule.SaveData?.StartPositionsByMap;
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

    // Every persisted entry is built here, and every one of them is built at the point
    // a slot's room state is written by this build: a Set writes the snapshot right
    // after this, and an import is refused unless its pack carries the current
    // contract. So stamping the current format is a statement about the file that is
    // about to exist, not a guess about one already on disk.
    private static AkronPersistedStartPos ToPersistedStartPos(AkronStartPos startPos) {
        return new AkronPersistedStartPos {
            SnapshotFormat = AkronReconstructionDocument.CurrentFormat,
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
                AkronLog.Warn(nameof(AkronActions),
                    "Failed to verify persisted StartPos metadata after writing it.");
                return false;
            }
            return true;
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronActions), "Failed to save persisted StartPos metadata: " + exception.Message);
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

    // "The save file changed" has to mean the player is on a different save file now,
    // not that the mod save data object was replaced. Everest hands out a fresh
    // EverestModuleSaveData every time it loads a file, and an Akron savestate Load
    // installs a cloned one for every module, so object identity says nothing about
    // which file is open - it only says something was reloaded. Comparing identity
    // here made a background restart copy report "the active save file changed" and
    // destroy a StartPos the player had just set, after nothing more than loading a
    // savestate. Compare the file itself; PersistStartPos writes into whatever object
    // currently owns it.
    private static bool IsOriginatingSaveFileActive(int fileSlot) {
        return AkronModule.Instance != null &&
               AkronModule.SaveData != null &&
               SaveData.Instance?.FileSlot == fileSlot;
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
