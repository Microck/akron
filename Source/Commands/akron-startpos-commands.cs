using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_save_time_deaths", "show or set whether StartPos restores preserve time/deaths: on|off|status")]
    public static void SaveTimeDeaths(string action = "status") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                break;
            case "on":
            case "true":
                AkronModule.Settings.SaveTimeAndDeaths = true;
                break;
            case "off":
            case "false":
                AkronModule.Settings.SaveTimeAndDeaths = false;
                break;
            default:
                Log("unknown save-time-deaths action: " + action);
                return;
        }

        Log("save-time-and-deaths: " + AkronModule.Settings.SaveTimeAndDeaths.ToString().ToLowerInvariant());
    }

    [Command("akron_slot", "set or show Akron savestate slot 1-9")]
    public static void Slot(string slot = "") {
        if (string.IsNullOrWhiteSpace(slot)) {
            Log("slot: " + AkronModule.Settings.ActiveSavestateSlot);
            return;
        }

        if (!TryParseSlot(slot, out int parsedSlot)) {
            Log("invalid slot: " + slot);
            return;
        }

        AkronModule.SetActiveSavestateSlot(parsedSlot);
        Log("slot set: " + AkronModule.Settings.ActiveSavestateSlot);
    }

    [Command("akron_save", "capture Akron StartPos state in the current or specified slot")]
    public static void Save(string slot = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!ApplyOptionalSlot(slot)) {
            return;
        }

        AkronSaveLoadResult result = AkronSaveLoadService.Save(level, AkronModule.Settings.ActiveSavestateSlot);
        Log(AkronModule.DescribeSavestateResult("Save", result, AkronModule.Settings.ActiveSavestateSlot));
    }

    [Command("akron_load", "restore Akron StartPos state from the current or specified slot")]
    public static void Load(string slot = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!ApplyOptionalSlot(slot)) {
            return;
        }

        AkronSaveLoadResult result = AkronSaveLoadService.Load(level, AkronModule.Settings.ActiveSavestateSlot);
        Log(AkronModule.DescribeSavestateResult("Load", result, AkronModule.Settings.ActiveSavestateSlot));
    }

    [Command("akron_startpos", "Akron StartPos: status|slot <n>|set|load|clear|prev|next|place <on|off>|dashes <n>|stamina <n>|slots <n>|facing <current|left|right>|idle <on|off>|grab <on|off>|label <on|off>|respawn <on|off>|smart <on|off>")]
    public static void StartPos(string action = "status", string value = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        string normalizedAction = NormalizeToken(action);
        if ((normalizedAction == "set" || normalizedAction == "load" || normalizedAction == "clear") &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actionSlot)) {
            AkronActions.SetStartPosSlot(actionSlot);
        }

        switch (normalizedAction) {
            case "":
            case "status":
                break;
            case "slot":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) {
                    Log("usage: akron_startpos slot <positive number>");
                    return;
                }
                AkronActions.SetStartPosSlot(slot);
                break;
            case "set":
                AkronActions.SetStartPos(level);
                break;
            case "load":
                AkronActions.LoadStartPos(level);
                break;
            case "clear":
                AkronActions.ClearActiveStartPos();
                break;
            case "prev":
            case "previous":
                AkronActions.ShiftStartPos(level, -1);
                break;
            case "next":
                AkronActions.ShiftStartPos(level, 1);
                break;
            case "place":
                if (!TryParseBoolean(value, out bool mouse)) {
                    Log("usage: akron_startpos place <on|off>");
                    return;
                }
                AkronModule.Settings.StartPosMousePlacement = mouse;
                break;
            case "dashes":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dashes)) {
                    Log("usage: akron_startpos dashes <-1..5>");
                    return;
                }
                AkronModule.Settings.StartPosConfiguredDashes = AkronModuleSettings.ClampStartPosDashes(dashes);
                AkronActions.ApplyStartPosConfiguration(AkronActions.GetActiveStartPos());
                break;
            case "stamina":
            case "staminapercent":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int staminaPercent)) {
                    Log("usage: akron_startpos stamina <-1..100>");
                    return;
                }
                AkronModule.Settings.StartPosConfiguredStaminaPercent = AkronModuleSettings.ClampStartPosStaminaPercent(staminaPercent);
                AkronActions.ApplyStartPosConfiguration(AkronActions.GetActiveStartPos());
                break;
            case "slots":
            case "slotcount":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotCount)) {
                    Log("usage: akron_startpos slots <1..99>");
                    return;
                }
                AkronModule.Settings.StartPosSlotCount = AkronModuleSettings.ClampStartPosSlotCount(slotCount);
                break;
            case "facing":
                if (!TryParseStartPosFacing(value, out AkronStartPosFacing facing)) {
                    Log("usage: akron_startpos facing <current|left|right>");
                    return;
                }
                AkronModule.Settings.StartPosConfiguredFacing = facing;
                AkronActions.ApplyStartPosConfiguration(AkronActions.GetActiveStartPos());
                break;
            case "idle":
                if (!TryParseBoolean(value, out bool idle)) {
                    Log("usage: akron_startpos idle <on|off>");
                    return;
                }
                AkronModule.Settings.StartPosConfiguredIdle = idle;
                AkronActions.ApplyStartPosConfiguration(AkronActions.GetActiveStartPos());
                break;
            case "grab":
                if (!TryParseBoolean(value, out bool grab)) {
                    Log("usage: akron_startpos grab <on|off>");
                    return;
                }
                AkronModule.Settings.StartPosConfiguredGrab = grab;
                AkronActions.ApplyStartPosConfiguration(AkronActions.GetActiveStartPos());
                break;
            case "label":
                if (!TryParseBoolean(value, out bool label)) {
                    Log("usage: akron_startpos label <on|off>");
                    return;
                }
                AkronModule.Settings.StartPosShowLabel = label;
                break;
            case "respawn":
                if (!ApplyStartPosRespawnAction(value)) {
                    return;
                }
                break;
            case "smart":
            case "smartstartpos":
                if (!TryParseBoolean(value, out bool smart)) {
                    Log("usage: akron_startpos smart <on|off>");
                    return;
                }
                AkronModule.Settings.SmartStartPos = smart;
                break;
            default:
                Log("unknown startpos action: " + action);
                return;
        }

        LogStartPosStatus(level);
    }

    private static bool ApplyOptionalSlot(string slot) {
        if (string.IsNullOrWhiteSpace(slot)) {
            return true;
        }

        if (!TryParseSlot(slot, out int parsedSlot)) {
            Log("invalid slot: " + slot);
            return false;
        }

        AkronModule.SetActiveSavestateSlot(parsedSlot);
        return true;
    }

    private static bool TryParseSlot(string slot, out int parsedSlot) {
        if (int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSlot)) {
            if (parsedSlot >= 1 && parsedSlot <= 9) {
                return true;
            }
        }

        parsedSlot = AkronModule.Settings.ActiveSavestateSlot;
        return false;
    }

    private static bool TryParseStartPosFacing(string value, out AkronStartPosFacing facing) {
        switch (NormalizeToken(value)) {
            case "":
            case "current":
            case "native":
                facing = AkronStartPosFacing.Current;
                return true;
            case "left":
                facing = AkronStartPosFacing.Left;
                return true;
            case "right":
                facing = AkronStartPosFacing.Right;
                return true;
            default:
                facing = AkronStartPosFacing.Current;
                return false;
        }
    }

    private static bool ApplyStartPosRespawnAction(string action) {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                return true;
            case "on":
            case "true":
            case "yes":
            case "1":
                AkronModule.Settings.RespawnAtStartPos = true;
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                AkronModule.Settings.RespawnAtStartPos = false;
                return true;
            case "toggle":
                AkronModule.Settings.RespawnAtStartPos = !AkronModule.Settings.RespawnAtStartPos;
                return true;
            default:
                Log("usage: akron_startpos respawn <on|off|toggle|status>");
                return false;
        }
    }

    private static void LogStartPosStatus(Level level) {
        AkronStartPos startPos = AkronActions.GetActiveStartPos();
        Log("startpos-index: " + (level == null ? "0/0" : AkronActions.DescribeStartPosIndex(level)));
        Log("startpos-slot: " + AkronModule.Settings.ActiveStartPosSlot.ToString(CultureInfo.InvariantCulture));
        Log("startpos-count: " + (level == null ? "0" : AkronActions.GetStartPositions(level).Count.ToString(CultureInfo.InvariantCulture)));
        Log("startpos-set: " + (startPos != null).ToString().ToLowerInvariant());
        Log("startpos-position: " + (startPos == null ? "unset" : FormatVector(startPos.Position)));
        Log("startpos-room: " + (startPos?.Room ?? "unset"));
        Log("startpos-area: " + (startPos?.AreaSid ?? "unset"));
        Log("startpos-state-slot: " + (string.IsNullOrWhiteSpace(startPos?.StateSlotName) ? "none" : startPos.StateSlotName));
        Log("startpos-state-snapshot: " + (startPos != null && AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName)).ToString().ToLowerInvariant());
        Log("startpos-snapshot-path: " + (string.IsNullOrWhiteSpace(startPos?.SnapshotPath) ? "none" : startPos.SnapshotPath));
        Log("startpos-snapshot-error: " + (string.IsNullOrWhiteSpace(startPos?.SnapshotLoadError) ? "none" : startPos.SnapshotLoadError));
        Log("startpos-last-loaded-slot: " + (AkronModule.Session?.LastLoadedStartPosSlot ?? 0).ToString(CultureInfo.InvariantCulture));
        Log("startpos-dashes: " + (startPos?.Dashes ?? AkronModule.Settings.StartPosConfiguredDashes).ToString(CultureInfo.InvariantCulture));
        Log("startpos-stamina: " + (startPos?.StaminaPercent ?? AkronModule.Settings.StartPosConfiguredStaminaPercent).ToString(CultureInfo.InvariantCulture));
        Log("startpos-facing: " + (startPos?.Facing ?? AkronModule.Settings.StartPosConfiguredFacing));
        Log("startpos-idle: " + (startPos?.Idle ?? AkronModule.Settings.StartPosConfiguredIdle).ToString().ToLowerInvariant());
        Log("startpos-grab: " + (startPos?.Grab ?? AkronModule.Settings.StartPosConfiguredGrab).ToString().ToLowerInvariant());
        Log("startpos-slot-count: " + AkronModule.Settings.StartPosSlotCount.ToString(CultureInfo.InvariantCulture));
        Log("startpos-place: " + AkronModule.Settings.StartPosMousePlacement.ToString().ToLowerInvariant());
        Log("startpos-label: " + AkronModule.Settings.StartPosShowLabel.ToString().ToLowerInvariant());
        Log("startpos-label-opacity: " + AkronModule.Settings.StartPosLabelStyle.Opacity.ToString(CultureInfo.InvariantCulture));
        Log("startpos-preview-opacity: " + AkronModule.Settings.StartPosPreviewOpacity.ToString(CultureInfo.InvariantCulture));
        Log("startpos-respawn: " + AkronModule.Settings.RespawnAtStartPos.ToString().ToLowerInvariant());
        Log("smart-startpos: " + AkronModule.Settings.SmartStartPos.ToString().ToLowerInvariant());
    }
}
