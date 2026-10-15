using System;
using System.Collections.Generic;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronActions {
    internal static void ApplyStartPosConfigurationEdit(AkronStartPos startPos,
        int? dashes = null, int? staminaPercent = null, AkronStartPosFacing? facing = null,
        bool? idle = null, bool? grab = null) {
        if (startPos == null) {
            return; // Editing an empty slot changes only the configured defaults.
        }

        int slot = FindStartPosSlot(startPos);
        int fileSlot = GetCurrentFileSlot();
        string areaSid = NormalizeAreaSid(startPos.AreaSid);
        if (slot == 0 || fileSlot < 0 || !IsOriginatingSaveFileActive(fileSlot, startPos.ProfileId) ||
            !string.Equals(areaSid, GetLoadedAreaSid(), StringComparison.Ordinal) ||
            !string.Equals(startPos.StateSlotName, GetStartPosStateSlotName(areaSid, slot, fileSlot), StringComparison.Ordinal)) {
            ReportStartPosConfigurationFailure("the slot is no longer current");
            return;
        }

        PendingStartPositionsByFileAndMap.TryGetValue(
            BuildPendingStartPosKey(fileSlot, areaSid, startPos.ProfileId), out Dictionary<int, AkronStartPos> pending);
        bool previousMetadataLost = false;
        bool applied = TryApplyStartPosConfigurationEdit(
            AkronModule.Session.StartPositions, slot, startPos, pending, GetPersistedStartPositions(areaSid),
            () => SaveAkronStartPosData(out previousMetadataLost),
            dashes, staminaPercent, facing, idle, grab);
        if (applied) {
            MarkStartPosCatalogChanged();
        } else {
            ReportStartPosConfigurationFailure(previousMetadataLost
                ? "saving failed and the previous disk metadata could not be restored"
                : "its metadata could not be saved");
        }
    }

    private static void ReportStartPosConfigurationFailure(string reason) {
        string message = "StartPos spawn edit was not applied: " + reason + ".";
        AkronLog.Warn(nameof(AkronActions), message);
        Engine.Scene?.Add(new AkronToast(message));
    }

    internal static bool TryApplyStartPosConfigurationEdit(
        Dictionary<int, AkronStartPos> runtimeSlots, int slot, AkronStartPos startPos,
        Dictionary<int, AkronStartPos> pendingSlots, Dictionary<int, AkronPersistedStartPos> persistedSlots,
        Func<bool> persist, int? dashes = null, int? staminaPercent = null,
        AkronStartPosFacing? facing = null, bool? idle = null, bool? grab = null) {
        if (startPos == null || runtimeSlots == null ||
            !runtimeSlots.TryGetValue(slot, out AkronStartPos current) || !ReferenceEquals(current, startPos)) {
            return false;
        }

        int nextDashes = dashes.HasValue ? AkronModuleSettings.ClampStartPosDashes(dashes.Value) : startPos.Dashes;
        int nextStamina = staminaPercent.HasValue ? AkronModuleSettings.ClampStartPosStaminaPercent(staminaPercent.Value) : startPos.StaminaPercent;
        AkronStartPosFacing nextFacing = facing ?? startPos.Facing;
        bool nextIdle = idle ?? startPos.Idle;
        bool nextGrab = grab ?? startPos.Grab;

        if (pendingSlots != null && pendingSlots.TryGetValue(slot, out AkronStartPos pending)) {
            if (!ReferenceEquals(pending, startPos)) {
                return false;
            }
            // Completion owns this exact pending object and will persist its final config.
            // Publishing metadata now could pair a new capture with an older disk snapshot.
        } else {
            if (persistedSlots == null || persist == null ||
                !persistedSlots.TryGetValue(slot, out AkronPersistedStartPos previous) || previous == null) {
                return false;
            }
            // This edit does not write a snapshot, so preserve its format and location exactly.
            AkronPersistedStartPos replacement = new AkronPersistedStartPos {
                X = previous.X, Y = previous.Y, Room = previous.Room, AreaSid = previous.AreaSid,
                SnapshotFormat = previous.SnapshotFormat,
                UsesSpawnConfig = true, Dashes = nextDashes, StaminaPercent = nextStamina,
                Facing = nextFacing, Idle = nextIdle, Grab = nextGrab
            };
            persistedSlots[slot] = replacement;
            bool saved = false;
            try {
                saved = persist();
            } finally {
                if (!saved) {
                    persistedSlots[slot] = previous;
                }
            }
            if (!saved) {
                return false;
            }
        }

        startPos.UsesSpawnConfig = true;
        startPos.Dashes = nextDashes;
        startPos.StaminaPercent = nextStamina;
        startPos.Facing = nextFacing;
        startPos.Idle = nextIdle;
        startPos.Grab = nextGrab;
        return true;
    }
}
