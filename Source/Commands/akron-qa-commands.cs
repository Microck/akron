using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    // Command-only QA probes and telemetry. These intentionally create or report
    // controlled runtime states for repeatable live verification, not player UI.
    [Command("akron_qa_save_settings", "force a settings save for Akron live automation")]
    public static void QaSaveSettings() {
        MethodInfo saveSettings = typeof(Everest).GetMethod("_SaveSettings", BindingFlags.Static | BindingFlags.NonPublic);
        if (saveSettings != null) {
            if (saveSettings.Invoke(null, Array.Empty<object>()) is IEnumerator routine) {
                while (routine.MoveNext()) {
                }
            }
            Log("qa-save-settings: saved");
            return;
        }

        UserIO.SaveHandler(true, true);
        Log("qa-save-settings: saved-fallback");
    }

    [Command("akron_qa_area_complete", "trigger Level.RegisterAreaComplete for Akron proof automation")]
    public static void QaAreaComplete(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        level.RegisterAreaComplete();
        Log("area-complete: registered");
    }

    [Command("akron_qa_upload_pack_submit", "submit Upload Pack through the modal path for QA: [startpos|auto-kill|auto-deafen] [anonymous|discord]")]
    public static void QaUploadPackSubmit(string sectionText = "", string attributionText = "anonymous") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!TryParseQaUploadSection(sectionText, out AkronSetupSection section)) {
            Log("usage: akron_qa_upload_pack_submit [startpos|auto-kill|auto-deafen] [anonymous|discord]");
            return;
        }

        switch (NormalizeToken(attributionText)) {
            case "":
            case "anonymous":
            case "anon":
                AkronModule.Settings.CommunityPackUploadUseDiscordAttribution = false;
                break;
            case "discord":
                AkronModule.Settings.CommunityPackUploadUseDiscordAttribution = true;
                break;
            default:
                Log("usage: akron_qa_upload_pack_submit [startpos|auto-kill|auto-deafen] [anonymous|discord]");
                return;
        }

        AkronModule.Settings.CommunityPackUploadSection = section;
        AkronCommunityPackUploads.OpenUploadPrompt(level);
        Log("qa-upload-pack-submit: requested;section=" + AkronSetupPacks.FormatSection(section) +
            ";attribution=" + (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution ? "discord" : "anonymous"));
        Log("qa-upload-pack-status: " + AkronCommunityPackUploads.DescribeUploadStatus());
    }

    [Command("akron_qa_startpos_action", "invoke StartPos action paths for prompt QA: save|restore")]
    public static void QaStartPosAction(string action = "restore") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        switch (NormalizeToken(action)) {
            case "save":
            case "capture":
                AkronModule.PerformSaveState(level);
                Log("qa-startpos-action: save");
                break;
            case "restore":
            case "load":
                AkronModule.PerformLoadState(level);
                Log("qa-startpos-action: restore");
                break;
            default:
                Log("usage: akron_qa_startpos_action save|restore");
                break;
        }

        Log("prompt: " + AkronPromptMenu.DescribeState());
    }

    private static bool TryParseQaUploadSection(string sectionText, out AkronSetupSection section) {
        switch (NormalizeToken(sectionText)) {
            case "":
                section = AkronCommunityPackUploads.NormalizeUploadSection(AkronModule.Settings.CommunityPackUploadSection);
                return true;
            case "start":
            case "startpos":
                section = AkronSetupSection.StartPos;
                return true;
            case "autokill":
            case "kill":
                section = AkronSetupSection.AutoKill;
                return true;
            case "autodeafen":
            case "deafen":
                section = AkronSetupSection.AutoDeafen;
                return true;
            default:
                section = AkronSetupSection.StartPos;
                return false;
        }
    }

    [Command("akron_qa_startpos_death_candidate", "show StartPos death respawn selection for QA: [x] [y]")]
    public static void QaStartPosDeathCandidate(string xText = "", string yText = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        Vector2 reference = player?.Position ?? Vector2.Zero;
        if (!string.IsNullOrWhiteSpace(xText) || !string.IsNullOrWhiteSpace(yText)) {
            if (!float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
                Log("usage: akron_qa_startpos_death_candidate [x] [y]");
                return;
            }
            reference = new Vector2(x, y);
        }

        AkronStartPos candidate = AkronActions.GetDeathRespawnStartPos(level, reference);
        Log("qa-startpos-reference: " + FormatVector(reference));
        Log("qa-startpos-active-slot: " + AkronModule.Settings.ActiveStartPosSlot.ToString(CultureInfo.InvariantCulture));
        Log("qa-startpos-last-loaded-slot: " + (AkronModule.Session?.LastLoadedStartPosSlot ?? 0).ToString(CultureInfo.InvariantCulture));
        Log("qa-startpos-smart: " + AkronModule.Settings.SmartStartPos.ToString().ToLowerInvariant());
        Log("qa-startpos-candidate-slot: " + FindQaStartPosSlot(candidate).ToString(CultureInfo.InvariantCulture));
        Log("qa-startpos-candidate-position: " + (candidate == null ? "unset" : FormatVector(candidate.Position)));
        if (AkronModule.Session?.StartPositions != null) {
            foreach (KeyValuePair<int, AkronStartPos> pair in AkronModule.Session.StartPositions.OrderBy(pair => pair.Key)) {
                AkronStartPos startPos = pair.Value;
                bool sameRoom = string.Equals(startPos.Room, level.Session.Level, StringComparison.Ordinal);
                bool hasState = string.IsNullOrWhiteSpace(startPos.StateSlotName) ||
                                AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName);
                float distance = Vector2.DistanceSquared(startPos.Position, reference);
                Log("qa-startpos-entry: slot=" + pair.Key.ToString(CultureInfo.InvariantCulture) +
                    ";position=" + FormatVector(startPos.Position) +
                    ";room=" + startPos.Room +
                    ";same-room=" + sameRoom.ToString().ToLowerInvariant() +
                    ";has-state=" + hasState.ToString().ToLowerInvariant() +
                    ";distance-squared=" + distance.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }
    }

    [Command("akron_qa_screenshake", "set a deterministic screenshake vector for visual tuning QA: <intensity> [x] [y]")]
    public static void QaScreenshake(string intensityText = "100", string xText = "6", string yText = "0") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!int.TryParse(intensityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intensity) ||
            !float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
            Log("usage: akron_qa_screenshake <intensity> [x] [y]");
            return;
        }

        AkronModule.Settings.Screenshake = true;
        AkronModule.Settings.ScreenshakeIntensity = AkronModuleSettings.ClampScreenshakeIntensity(intensity);
        SetInstanceMember(level, "ShakeVector", new Vector2(x, y));
        Log("qa-screenshake-set: intensity=" + AkronModule.Settings.ScreenshakeIntensity.ToString(CultureInfo.InvariantCulture) +
            ";vector=" + FormatVector(new Vector2(x, y)));
        AkronRuntimeOptions.ApplyScreenshakeAfterLevelUpdate(level);
        LogQaScreenshakeState(level);
    }

    [Command("akron_qa_screenshake_state", "show screenshake override state for visual tuning QA")]
    public static void QaScreenshakeState() {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        LogQaScreenshakeState(level);
    }

    [Command("akron_qa_fast_lookout", "show Fast Lookout multiplier diagnostics for QA: [held] [vanilla-value]")]
    public static void QaFastLookout(string heldText = "true", string vanillaValueText = "800") {
        bool holdPressed = ParseQaBool(heldText, defaultValue: true);
        if (!float.TryParse(vanillaValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float vanillaValue)) {
            Log("usage: akron_qa_fast_lookout [held] [vanilla-value]");
            return;
        }

        float result = AkronModule.ApplyFastLookoutMultiplierForQa(vanillaValue, holdPressed);
        Log("qa-fast-lookout-enabled: " + AkronModule.Settings.FastLookout.ToString().ToLowerInvariant());
        Log("qa-fast-lookout-hold-forced: " + holdPressed.ToString().ToLowerInvariant());
        Log("qa-fast-lookout-live-hold: " + (AkronModule.Settings.FastLookoutHold?.Check ?? false).ToString().ToLowerInvariant());
        Log("qa-fast-lookout-patched-constants: " + AkronModule.FastLookoutPatchedConstantCount.ToString(CultureInfo.InvariantCulture));
        Log("qa-fast-lookout-multiplier: " + AkronModuleSettings.ClampFastLookoutMultiplier(AkronModule.Settings.FastLookoutMultiplier).ToString(CultureInfo.InvariantCulture));
        Log("qa-fast-lookout-vanilla-value: " + vanillaValue.ToString("0.###", CultureInfo.InvariantCulture));
        Log("qa-fast-lookout-result: " + result.ToString("0.###", CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_dash_redirect", "exercise Dash Redirect guard for QA: <directions> <original-aim> <redirected-aim>")]
    public static void QaDashRedirect(string directions = "down", string originalAimText = "down", string redirectedAimText = "right") {
        if (!TryParseQaAim(originalAimText, out Vector2 originalAim) ||
            !TryParseQaAim(redirectedAimText, out Vector2 redirectedAim)) {
            Log("usage: akron_qa_dash_redirect <directions> <original-aim> <redirected-aim>");
            return;
        }

        bool disabled = NormalizeToken(directions) is "off" or "false" or "disabled";
        if (disabled) {
            SetDashRedirect("disabled");
        } else {
            SetDashRedirect("on");
            SetDashRedirect(directions);
        }
        Vector2 result = AkronModule.ApplyDashRedirectGuardForQa(originalAim, redirectedAim);
        Log("qa-dash-redirect-enabled: " + AkronModule.Settings.DashRedirectEnabled.ToString().ToLowerInvariant());
        Log("qa-dash-redirect-directions: " + AkronModule.Settings.DashRedirectDirections);
        Log("qa-dash-redirect-original: " + FormatVector(originalAim));
        Log("qa-dash-redirect-redirected: " + FormatVector(redirectedAim));
        Log("qa-dash-redirect-result: " + FormatVector(result));
        Log("qa-dash-redirect-preserved-original: " + (result == originalAim.EightWayNormal()).ToString().ToLowerInvariant());
    }

    [Command("akron_qa_click_teleport", "exercise Click Teleport screen-to-world and move path for QA: <screen-x> <screen-y>")]
    public static void QaClickTeleport(string xText = "160", string yText = "90") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
            Log("usage: akron_qa_click_teleport <screen-x> <screen-y>");
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        Vector2 before = player?.Position ?? Vector2.Zero;
        Vector2 hairBefore = player?.Hair?.Nodes != null && player.Hair.Nodes.Count > 0 ? player.Hair.Nodes[0] - before : Vector2.Zero;
        Vector2 cameraBefore = level.Camera.Position;
        bool applied = AkronModule.ApplyClickTeleportForQa(level, new Vector2(x, y), out Vector2 target);
        Vector2 after = player?.Position ?? Vector2.Zero;
        Vector2 hairAfter = player?.Hair?.Nodes != null && player.Hair.Nodes.Count > 0 ? player.Hair.Nodes[0] - after : Vector2.Zero;

        Log("qa-click-teleport-applied: " + applied.ToString().ToLowerInvariant());
        Log("qa-click-teleport-screen: " + FormatVector(new Vector2(x, y)));
        Log("qa-click-teleport-target: " + FormatVector(target));
        Log("qa-click-teleport-before: " + FormatVector(before));
        Log("qa-click-teleport-after: " + FormatVector(after));
        Log("qa-click-teleport-hair-before: " + FormatVector(hairBefore));
        Log("qa-click-teleport-hair-after: " + FormatVector(hairAfter));
        Log("qa-click-teleport-camera-before: " + FormatVector(cameraBefore));
        Log("qa-click-teleport-camera-after: " + FormatVector(level.Camera.Position));
        Log("qa-click-teleport-free-camera: " + AkronRuntimeOptions.IsFreeCameraActive(level).ToString().ToLowerInvariant());
        Log("qa-click-teleport-level-zoom: " + AkronModule.IsLevelZoomActive(level).ToString().ToLowerInvariant());
    }

    [Command("akron_qa_backup", "backup QA: list|create [reason]|pin <index> <on|off>|restore <index>|retention <max-count> <keep-at-least>")]
    public static void QaBackup(string action = "list", string arg1 = "", string arg2 = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "list":
                LogQaBackups();
                return;
            case "create":
                string reason = string.IsNullOrWhiteSpace(arg1) ? "qa" : arg1.Trim();
                bool created = AkronBackupActions.CreateBackup(reason, showToast: false);
                Log("qa-backup-create: " + created.ToString().ToLowerInvariant());
                Log("qa-backup-status: " + AkronBackupActions.LastStatus);
                LogQaBackups();
                return;
            case "pin":
                if (!TryGetQaBackup(arg1, out AkronBackupEntry pinBackup)) {
                    return;
                }
                bool pin = ParseQaBool(arg2, defaultValue: true);
                AkronBackupActions.SetPinned(pinBackup, pin);
                Log("qa-backup-pin: index=" + arg1 + ";pinned=" + pin.ToString().ToLowerInvariant() + ";file=" + pinBackup.FileName);
                Log("qa-backup-status: " + AkronBackupActions.LastStatus);
                LogQaBackups();
                return;
            case "restore":
                if (!TryGetQaBackup(arg1, out AkronBackupEntry restoreBackup)) {
                    return;
                }
                AkronBackupActions.RestoreBackupForQa(restoreBackup);
                Log("qa-backup-restore: file=" + restoreBackup.FileName);
                Log("qa-backup-status: " + AkronBackupActions.LastStatus);
                LogQaBackups();
                return;
            case "retention":
                if (!int.TryParse(arg1, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxCount) ||
                    !int.TryParse(arg2, NumberStyles.Integer, CultureInfo.InvariantCulture, out int keepAtLeast)) {
                    Log("usage: akron_qa_backup retention <max-count> <keep-at-least>");
                    return;
                }
                AkronModule.Settings.BackupsMaxCount = AkronBackupActions.ClampBackupMaxCount(maxCount);
                AkronModule.Settings.BackupsKeepAtLeast = AkronBackupActions.ClampBackupKeepAtLeast(keepAtLeast);
                AkronBackupActions.ApplyRetentionForQa();
                Log("qa-backup-retention: max-count=" + AkronModule.Settings.BackupsMaxCount.ToString(CultureInfo.InvariantCulture) +
                    ";keep-at-least=" + AkronModule.Settings.BackupsKeepAtLeast.ToString(CultureInfo.InvariantCulture));
                LogQaBackups();
                return;
            default:
                Log("usage: akron_qa_backup list|create [reason]|pin <index> <on|off>|restore <index>|retention <max-count> <keep-at-least>");
                return;
        }
    }

    [Command("akron_qa_label_row_order", "label row order QA: status|move <row> <target> [before|after]")]
    public static void QaLabelRowOrder(string action = "status", string row = "", string target = "", string placement = "before") {
        AkronModuleSettings settings = AkronModule.Settings;
        settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(settings.LabelRowOrder, settings.CustomHudLabelDefinitions);
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "move":
                if (!TryResolveLabelRowKey(row, out string rowKey) ||
                    !TryResolveLabelRowKey(target, out string targetKey)) {
                    Log("qa-label-row-order: invalid-row");
                    LogQaLabelRowOrder();
                    return;
                }

                MoveQaLabelRow(rowKey, targetKey, NormalizeToken(placement) is "after" or "below");
                break;
            default:
                Log("usage: akron_qa_label_row_order status|move <row> <target> [before|after]");
                return;
        }

        LogQaLabelRowOrder();
    }

    [Command("akron_qa_label_number", "set live label number state for QA: <deaths> [no-short on|off]")]
    public static void QaLabelNumber(string deathsText = "12345", string noShortText = "off") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!int.TryParse(deathsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deaths)) {
            Log("usage: akron_qa_label_number <deaths> [no-short on|off]");
            return;
        }

        deaths = Math.Max(0, deaths);
        level.Session.Deaths = deaths;
        level.Session.DeathsInCurrentLevel = 0;
        if (TryParseBoolean(noShortText, out bool noShort)) {
            AkronModule.Settings.NoShortNumbers = noShort;
        }

        AreaStats areaStats = SaveData.Instance?.Areas_Safe != null &&
                              level.Session.Area.ID >= 0 &&
                              level.Session.Area.ID < SaveData.Instance.Areas_Safe.Count
            ? SaveData.Instance.Areas_Safe[level.Session.Area.ID]
            : null;
        int mode = (int) level.Session.Area.Mode;
        if (areaStats?.Modes != null && mode >= 0 && mode < areaStats.Modes.Length) {
            areaStats.Modes[mode].Deaths = deaths;
        }

        AkronModule.Settings.LabelSystemVisible = true;
        AkronModule.Settings.TotalAttemptsWidget = true;
        Log("qa-label-number-deaths: " + deaths.ToString(CultureInfo.InvariantCulture));
        Log("qa-label-number-total: " + AkronHudRenderer.GetCurrentMapDeathTotal(level).ToString(CultureInfo.InvariantCulture));
        Log("qa-label-number-no-short: " + AkronModule.Settings.NoShortNumbers.ToString().ToLowerInvariant());
        Log("qa-label-number-formatted: " + AkronHudRenderer.FormatHudNumber(AkronHudRenderer.GetCurrentMapDeathTotal(level) + 1));
    }

    [Command("akron_qa_toast_label", "show a live Akron toast label for QA: [message] [duration-seconds]")]
    public static void QaToastLabel(string message = "QA_TOAST", string durationText = "8") {
        if (!float.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)) {
            duration = 8f;
        }

        AkronModule.Settings.LabelSystemVisible = true;
        AkronModule.Settings.ToastLabels = true;
        Engine.Scene?.Add(new AkronToast(string.IsNullOrWhiteSpace(message) ? "QA_TOAST" : message, durationSeconds: duration));
        Log("qa-toast-label: shown");
        Log("qa-toast-label-message: " + (string.IsNullOrWhiteSpace(message) ? "QA_TOAST" : message));
        Log("qa-toast-label-duration: " + Math.Max(0.1f, duration).ToString("0.###", CultureInfo.InvariantCulture));
        Log("qa-toast-labels-enabled: " + AkronModule.Settings.ToastLabels.ToString().ToLowerInvariant());
        Log("qa-labels-visible: " + AkronModule.Settings.LabelSystemVisible.ToString().ToLowerInvariant());
    }

    [Command("akron_qa_enter_debug_map", "enter Celeste's debug map scene from the active level for QA")]
    public static void QaEnterDebugMap(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        AkronModule.PerformOpenDebugMap(level);
        Log("qa-enter-debug-map: requested");
    }

    [Command("akron_qa_cutscene_state", "prepare a controlled active cutscene state for Skip Cutscene QA")]
    public static void QaCutsceneState(string action = "active") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (NormalizeToken(action) == "clear" || NormalizeToken(action) == "off") {
            level.InCutscene = false;
            level.SkippingCutscene = false;
            SetQaCutsceneSkipCallback(level, null);
            Log("qa-cutscene-state: clear");
            return;
        }

        level.InCutscene = true;
        level.SkippingCutscene = false;
        SetQaCutsceneSkipCallback(level, skippedLevel => {
            level.InCutscene = false;
            level.SkippingCutscene = false;
        });
        Log("qa-cutscene-state: active");
    }

    [Command("akron_qa_enter_level", "enter a vanilla level for Akron live automation: [area-id] [normal|b|c] [save-slot]")]
    public static void QaEnterLevel(string areaIdText = "0", string modeText = "normal", string slotText = "") {
        if (!int.TryParse(areaIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId)) {
            Log("qa-enter-level: invalid-area area=" + areaIdText);
            return;
        }

        if (!TryParseQaSaveSlot(slotText, "qa-enter-level", out int? saveSlot)) {
            return;
        }

        AreaMode mode = ParseAreaMode(modeText);
        AreaData data = GetQaAreaData(areaId, "qa-enter-level");
        if (data == null) {
            return;
        }

        if (!data.HasMode(mode)) {
            Log("qa-enter-level: missing-mode area=" + data.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode);
            return;
        }

        AreaKey area = data.ToKey(mode);
        SaveData saveData = CreateQaSaveData(area, saveSlot);
        AreaStats stats = EnsureQaAreaStats(saveData, area);
        Session session = new Session(area, null, stats);
        saveData.StartSession(session);
        Engine.Scene = new LevelLoader(session);
        Log("qa-enter-level: area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode +
            ";slot=" + (saveSlot.HasValue ? saveSlot.Value.ToString(CultureInfo.InvariantCulture) : "transient"));
    }

    [Command("akron_qa_reenter_level", "enter a vanilla level with the current SaveData: [area-id] [normal|b|c]")]
    public static void QaReenterLevel(string areaIdText = "0", string modeText = "normal") {
        if (SaveData.Instance == null) {
            Log("qa-reenter-level: missing-save-data");
            return;
        }

        if (!int.TryParse(areaIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId)) {
            Log("qa-reenter-level: invalid-area area=" + areaIdText);
            return;
        }

        AreaMode mode = ParseAreaMode(modeText);
        AreaData data = GetQaAreaData(areaId, "qa-reenter-level");
        if (data == null) {
            return;
        }

        if (!data.HasMode(mode)) {
            Log("qa-reenter-level: missing-mode area=" + data.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode);
            return;
        }

        AreaKey area = data.ToKey(mode);
        AreaStats stats = EnsureQaAreaStats(SaveData.Instance, area);
        Session session = new Session(area, null, stats);
        SaveData.Instance.StartSession(session);
        Engine.Scene = new LevelLoader(session);
        Log("qa-reenter-level: area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode +
            ";slot=" + SaveData.Instance.FileSlot.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_enter_map", "enter a loaded map for Akron live automation: [sid] [normal|b|c]")]
    public static void QaEnterMap(string sid = "", string modeText = "normal") {
        if (string.IsNullOrWhiteSpace(sid)) {
            Log("qa-enter-map: missing sid");
            return;
        }

        AreaData data = AreaData.Get(sid.Trim());
        if (data == null) {
            Log("qa-enter-map: not-found sid=" + sid);
            return;
        }

        AreaMode mode = ParseAreaMode(modeText);
        if (!data.HasMode(mode)) {
            Log("qa-enter-map: missing-mode sid=" + data.SID + ";mode=" + mode);
            return;
        }

        AreaKey area = data.ToKey(mode);
        SaveData saveData = CreateQaSaveData(area);
        AreaStats stats = EnsureQaAreaStats(saveData, area);
        Session session = new Session(area, null, stats);
        saveData.StartSession(session);
        Engine.Scene = new LevelLoader(session);
        Log("qa-enter-map: sid=" + data.SID + ";area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode);
    }

    [Command("akron_qa_warp_room", "warp to a room in the current loaded map for Akron live automation: [room-name]")]
    public static void QaWarpRoom(string roomName = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (string.IsNullOrWhiteSpace(roomName)) {
            Log("qa-warp-room: missing room");
            return;
        }

        LevelData room = level.Session.MapData.Get(roomName.Trim());
        if (room == null) {
            Log("qa-warp-room: not-found room=" + roomName);
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene != level) {
                return;
            }

            Vector2 probe = new Vector2(room.Bounds.Left, room.Bounds.Bottom);
            level.Session.Level = room.Name;
            level.Session.RespawnPoint = level.Session.GetSpawnPoint(probe);
            level.StartPosition = null;
            level.Tracker.GetEntitiesCopy<Player>().ForEach(player => player.RemoveSelf());
            level.UnloadLevel();
            level.Completed = false;
            level.InCutscene = false;
            level.SkippingCutscene = false;
            level.LoadLevel(Player.IntroTypes.Respawn);
            level.Entities.UpdateLists();
            AkronLevelRenderState.RelinkRendererCameras(level);
        };
        Log("qa-warp-room: room=" + room.Name);
    }

    [Command("akron_qa_inspector_pin_world", "pin the entity inspector at a world coordinate for Akron live automation: x y")]
    public static void QaInspectorPinWorld(string x = "", string y = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) ||
            !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY)) {
            Log("usage: akron_qa_inspector_pin_world <x> <y>");
            return;
        }

        if (!AkronModule.Settings.EntityInspector) {
            SetPolicyToggle(AkronFeatureKind.EntityInspector, () => AkronModule.Settings.EntityInspector, value => AkronModule.Settings.EntityInspector = value);
        }

        string outcome = AkronEntityInspector.PinInspectorAtWorldPointForQa(level, new Vector2(parsedX, parsedY));
        Log("qa-inspector-pin-world: " + outcome);
    }

    [Command("akron_qa_inspector_cursor_state", "show Entity Inspector cursor activation state for Akron live automation")]
    public static void QaInspectorCursorState(string _ = "") {
        Log("qa-inspector-cursor-setting: " + AkronModule.Settings.EntityInspector.ToString().ToLowerInvariant());
        Log("qa-inspector-cursor-binding: " + AkronModuleSettings.DescribeBinding(AkronModuleSettings.ResolveEntityInspectorCursorHoldBinding(AkronModule.Settings)));
        Log("qa-inspector-cursor-left-alt: " + Keyboard.GetState().IsKeyDown(Keys.LeftAlt).ToString().ToLowerInvariant());
        Log("qa-inspector-cursor-hold: " + AkronModule.IsEntityInspectorCursorHoldActive().ToString().ToLowerInvariant());
        Log("qa-inspector-cursor-visible: " + AkronModule.ShouldShowEntityInspectorCursor().ToString().ToLowerInvariant());
        Log("qa-inspector-engine-mouse-visible: " + Engine.Instance.IsMouseVisible.ToString().ToLowerInvariant());
    }

    [Command("akron_qa_inspector_stack_state", "show Entity Inspector hover and pinned stacks for Akron live automation")]
    public static void QaInspectorStackState(string _ = "") {
        Log("qa-inspector-stack-state: " + AkronEntityInspector.DescribeInspectorStacksForQa());
    }

    [Command("akron_qa_inspector_controls", "exercise Entity Inspector copy/properties/close controls for Akron live automation")]
    public static void QaInspectorControls(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Log("qa-inspector-controls: " + AkronEntityInspector.ExerciseInspectorPinControlsForQa(level));
    }

    [Command("akron_qa_inspector_scan_targets", "scan visible Entity Inspector targets for Akron live automation: [step-pixels] [limit] [max-per-type]")]
    public static void QaInspectorScanTargets(string stepPixels = "40", string limit = "80", string maxPerType = "8") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!int.TryParse(stepPixels, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedStepPixels) ||
            !int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLimit) ||
            !int.TryParse(maxPerType, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMaxPerType)) {
            Log("usage: akron_qa_inspector_scan_targets [step-pixels] [limit] [max-per-type]");
            return;
        }

        foreach (string line in AkronEntityInspector.ScanInspectorTargetsForQa(level, parsedStepPixels, parsedLimit, parsedMaxPerType)) {
            Log("qa-inspector-scan-target: " + line);
        }
    }

    [Command("akron_qa_inspector_probe_screen", "show Entity Inspector hit-test diagnostics at screen coordinates: [x] [y]")]
    public static void QaInspectorProbeScreen(string x = "", string y = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Vector2 screenPoint;
        if (string.IsNullOrWhiteSpace(x) && string.IsNullOrWhiteSpace(y)) {
            MouseState mouse = Mouse.GetState();
            screenPoint = new Vector2(mouse.X, mouse.Y);
        } else if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) &&
                   float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY)) {
            screenPoint = new Vector2(parsedX, parsedY);
        } else {
            Log("usage: akron_qa_inspector_probe_screen [x y]");
            return;
        }

        Log("qa-inspector-probe-screen: " + AkronEntityInspector.DiagnoseInspectorPinScreenPointForQa(level, screenPoint));
    }

    [Command("akron_qa_refill_clarity_probe", "spawn a custom refill-like QA probe and enable Refill Clarity: [x] [y] [one-use|reusable]")]
    public static void QaRefillClarityProbe(string x = "", string y = "", string mode = "one-use") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Vector2 position = ResolveQaWorldPosition(level, x, y);
        bool oneUse = NormalizeToken(mode) != "reusable" && NormalizeToken(mode) != "false";
        AkronQaCustomRefillProbe probe = new AkronQaCustomRefillProbe(position, oneUse);
        level.Add(probe);
        level.Entities.UpdateLists();
        EnsureRefillClarityEnabled();

        Log("qa-refill-clarity-probe: type=" + probe.GetType().FullName +
            ";one-use=" + probe.oneUse.ToString().ToLowerInvariant() +
            ";position=" + FormatVector(probe.Position) +
            ";enabled=" + AkronModule.Settings.RefillClarity.ToString().ToLowerInvariant() +
            ";eligible=" + AkronHudRenderer.ShouldRenderRefillClarityOutline(probe).ToString().ToLowerInvariant());
        if (AkronHudRenderer.TryGetRefillClarityBounds(probe, out Rectangle bounds)) {
            Log("qa-refill-clarity-probe-bounds: " + FormatRectangle(bounds));
        }
    }

    [Command("akron_qa_refill_clarity_dash_crystal", "spawn a real dash crystal and enable Refill Clarity: [x] [y] [one-use|reusable] [one-dash|two-dash]")]
    public static void QaRefillClarityDashCrystal(string x = "", string y = "", string mode = "one-use", string dashMode = "one-dash") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Vector2 position = ResolveQaWorldPosition(level, x, y);
        bool oneUse = NormalizeToken(mode) != "reusable" && NormalizeToken(mode) != "false";
        bool twoDashes = NormalizeToken(dashMode) == "two-dash" || NormalizeToken(dashMode) == "twodash" || NormalizeToken(dashMode) == "two";
        EnsureRefillClarityEnabled();
        Refill refill = new Refill(position, twoDashes, oneUse);
        level.Add(refill);
        level.Entities.UpdateLists();

        Log("qa-refill-clarity-dash-crystal: type=" + refill.GetType().FullName +
            ";one-use=" + refill.oneUse.ToString().ToLowerInvariant() +
            ";two-dashes=" + twoDashes.ToString().ToLowerInvariant() +
            ";position=" + FormatVector(refill.Position) +
            ";enabled=" + AkronModule.Settings.RefillClarity.ToString().ToLowerInvariant() +
            ";eligible=" + AkronHudRenderer.ShouldRenderRefillClarityOutline(refill).ToString().ToLowerInvariant());
        if (AkronHudRenderer.TryGetRefillClarityBounds(refill, out Rectangle bounds)) {
            Log("qa-refill-clarity-dash-crystal-bounds: " + FormatRectangle(bounds));
        }
    }

    [Command("akron_qa_refill_clarity_style", "set Refill Clarity style for live verification: [rrggbb] [opacity]")]
    public static void QaRefillClarityStyle(string color = "FF2929", string opacity = "100") {
        if (!TryParseQaRgb(color, out int parsedColor)) {
            Log("usage: akron_qa_refill_clarity_style <rrggbb> [opacity]");
            return;
        }

        if (!int.TryParse(opacity, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOpacity)) {
            Log("usage: akron_qa_refill_clarity_style <rrggbb> [opacity]");
            return;
        }

        EnsureRefillClarityEnabled();
        AkronModule.Settings.RefillClarityColor = AkronModuleSettings.ClampRgb(parsedColor);
        AkronModule.Settings.RefillClarityOpacity = AkronModuleSettings.ClampOpacity(parsedOpacity);
        Log("qa-refill-clarity-style: color=" + AkronModule.Settings.RefillClarityColor.ToString("X6", CultureInfo.InvariantCulture) +
            ";opacity=" + AkronModule.Settings.RefillClarityOpacity.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_refill_clarity_state", "report visible one-use entities eligible for Refill Clarity")]
    public static void QaRefillClarityState(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        int count = 0;
        foreach (Entity entity in level.Entities.Where(entity => AkronHudRenderer.TryGetRefillClarityBounds(entity, out Rectangle _))) {
            AkronHudRenderer.TryGetRefillClarityBounds(entity, out Rectangle bounds);
            Log("qa-refill-clarity-target: type=" + (entity.GetType().FullName ?? entity.GetType().Name) +
                ";position=" + FormatVector(entity.Position) +
                ";bounds=" + FormatRectangle(bounds));
            count++;
        }

        Log("qa-refill-clarity: enabled=" + AkronModule.Settings.RefillClarity.ToString().ToLowerInvariant() +
            ";count=" + count.ToString(CultureInfo.InvariantCulture));
    }

    private static Vector2 ResolveQaWorldPosition(Level level, string x, string y) {
        if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) &&
            float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY)) {
            return new Vector2(parsedX, parsedY);
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player != null) {
            return player.Center + new Vector2(24f, -8f);
        }

        return level.Camera.Position + new Vector2(160f, 90f);
    }

    private static string FormatRectangle(Rectangle rectangle) {
        return rectangle.X.ToString(CultureInfo.InvariantCulture) + "," +
               rectangle.Y.ToString(CultureInfo.InvariantCulture) + " " +
               rectangle.Width.ToString(CultureInfo.InvariantCulture) + "x" +
               rectangle.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static void EnsureRefillClarityEnabled() {
        if (!AkronModule.Settings.RefillClarity) {
            SetPolicyToggle(AkronFeatureKind.RefillClarity, () => AkronModule.Settings.RefillClarity, value => AkronModule.Settings.RefillClarity = value);
        }
    }

    private static bool TryParseQaRgb(string value, out int rgb) {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal)) {
            normalized = normalized.Substring(1);
        } else if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(2);
        }

        return normalized.Length <= 6 &&
               int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    private static bool TryParseQaAim(string value, out Vector2 aim) {
        switch (NormalizeToken(value)) {
            case "up":
                aim = -Vector2.UnitY;
                return true;
            case "down":
                aim = Vector2.UnitY;
                return true;
            case "left":
                aim = -Vector2.UnitX;
                return true;
            case "right":
                aim = Vector2.UnitX;
                return true;
            case "upleft":
                aim = new Vector2(-1f, -1f);
                return true;
            case "upright":
                aim = new Vector2(1f, -1f);
                return true;
            case "downleft":
                aim = new Vector2(-1f, 1f);
                return true;
            case "downright":
                aim = new Vector2(1f, 1f);
                return true;
            default:
                aim = Vector2.Zero;
                return false;
        }
    }

    private static AreaData GetQaAreaData(int areaId, string commandName) {
        if (AreaData.Areas == null || AreaData.Areas.Count == 0) {
            Log(commandName + ": area-data-not-ready");
            return null;
        }

        if (areaId < 0 || areaId >= AreaData.Areas.Count) {
            Log(commandName + ": area-not-found area=" + areaId.ToString(CultureInfo.InvariantCulture));
            return null;
        }

        AreaData data = AreaData.Get(areaId);
        if (data == null || string.IsNullOrWhiteSpace(data.SID)) {
            Log(commandName + ": area-data-not-ready area=" + areaId.ToString(CultureInfo.InvariantCulture));
            return null;
        }

        return data;
    }

    private static SaveData CreateQaSaveData(AreaKey area, int? fileSlot = null) {
        // QA entry must not depend on the user's persisted save or mod-session
        // files. Celeste's debug initializer still builds the normal area-stat
        // graph that helper mods expect. Most tests keep the slot non-persistent;
        // backup restore QA may request a numbered throwaway slot so Celeste can
        // exercise its normal live reload path after extracting a backup.
        SaveData.InitializeDebugMode(loadExisting: false);
        SaveData saveData = SaveData.Instance ?? new SaveData();
        saveData.DebugMode = true;
        saveData.DoNotSave = !fileSlot.HasValue;
        saveData.HasModdedSaveData = true;
        saveData.LastArea = area;
        saveData.LastArea_Safe = area;

        int targetSlot = fileSlot ?? -1;
        if (SaveData.Instance != saveData || saveData.FileSlot != targetSlot) {
            SaveData.Start(saveData, targetSlot);
        }
        if (fileSlot.HasValue) {
            UserIO.SaveHandler(true, false);
        }
        Log("qa-save-data: " + (fileSlot.HasValue ? "persistent debug slot=" + fileSlot.Value.ToString(CultureInfo.InvariantCulture) : "transient debug slot") +
            " for area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + area.Mode);
        return saveData;
    }

    private static bool TryParseQaSaveSlot(string slotText, string commandName, out int? saveSlot) {
        saveSlot = null;
        if (string.IsNullOrWhiteSpace(slotText)) {
            return true;
        }

        if (!int.TryParse(slotText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < 0 ||
            parsed > 99) {
            Log(commandName + ": invalid-slot slot=" + slotText);
            return false;
        }

        saveSlot = parsed;
        return true;
    }

    private static AreaStats EnsureQaAreaStats(SaveData saveData, AreaKey area) {
        while (saveData.Areas.Count <= area.ID) {
            saveData.Areas.Add(new AreaStats(saveData.Areas.Count));
        }

        AreaStats stats = saveData.Areas[area.ID] ?? new AreaStats(area.ID);
        saveData.Areas[area.ID] = stats;
        saveData.UnlockedAreas = Math.Max(saveData.UnlockedAreas, area.ID + 1);
        return stats;
    }

    [Command("akron_qa_list_maps", "list loaded map SIDs for Akron live automation")]
    public static void QaListMaps(string filter = "") {
        string normalizedFilter = filter?.Trim() ?? string.Empty;
        int count = 0;
        foreach (AreaData data in AreaData.Areas ?? new List<AreaData>()) {
            if (data == null || string.IsNullOrWhiteSpace(data.SID)) {
                continue;
            }

            if (normalizedFilter.Length > 0 &&
                data.SID.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                (data.Name ?? string.Empty).IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            Log("qa-map: sid=" + data.SID + ";id=" + data.ID.ToString(CultureInfo.InvariantCulture) + ";name=" + (data.Name ?? string.Empty));
            count++;
        }

        Log("qa-list-maps: count=" + count.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_find_map_entities", "list loaded map entity data by name filter: [filter] [limit]")]
    public static void QaFindMapEntities(string filter = "", string limitText = "80") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        string normalizedFilter = filter?.Trim() ?? string.Empty;
        if (!int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) || limit <= 0) {
            limit = 80;
        }

        int matched = 0;
        foreach (LevelData room in level.Session.MapData.Levels.Where(room => room != null && !room.Dummy)) {
            foreach (EntityData entity in room.Entities ?? new List<EntityData>()) {
                if (normalizedFilter.Length > 0 &&
                    entity.Name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    room.Name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                Vector2 worldPosition = new Vector2(room.Bounds.Left, room.Bounds.Top) + entity.Position;
                Log("qa-map-entity: room=" + room.Name +
                    ";name=" + entity.Name +
                    ";id=" + entity.ID.ToString(CultureInfo.InvariantCulture) +
                    ";local=" + FormatVector(entity.Position) +
                    ";world=" + FormatVector(worldPosition));
                matched++;
                if (matched >= limit) {
                    Log("qa-find-map-entities: matched-at-least=" + matched.ToString(CultureInfo.InvariantCulture) + ";truncated=true");
                    return;
                }
            }
        }

        Log("qa-find-map-entities: matched=" + matched.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_invincibility_hazard", "invoke loaded hazard contact for QA: rising-lava|sandwich-lava|crush")]
    public static void QaInvincibilityHazard(string hazard = "rising-lava") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            Log("player: missing");
            return;
        }

        string normalized = NormalizeToken(hazard);
        Log("qa-invincibility-hazard: " + normalized);
        Log("qa-invincibility-hazard-before: " + DescribeQaPlayerHazardState(player));

        switch (normalized) {
            case "risinglava":
            case "rising":
            case "lava":
                RisingLava risingLava = level.Entities.OfType<RisingLava>().FirstOrDefault();
                if (risingLava == null) {
                    Log("qa-invincibility-hazard-missing: rising-lava");
                    return;
                }

                Log("qa-invincibility-hazard-entity: " + DescribeQaEntity(risingLava));
                SetInstanceMember(risingLava, "delay", 0f);
                InvokePrivateHazardOnPlayer(risingLava, player);
                Log("qa-invincibility-hazard-lava-y: " + risingLava.Y.ToString("0.###", CultureInfo.InvariantCulture));
                Log("qa-invincibility-hazard-lava-delay: " + FormatQaMember(risingLava, "delay"));
                break;
            case "sandwichlava":
            case "sandwich":
                SandwichLava sandwichLava = level.Entities.OfType<SandwichLava>().FirstOrDefault();
                if (sandwichLava == null) {
                    Log("qa-invincibility-hazard-missing: sandwich-lava");
                    return;
                }

                Log("qa-invincibility-hazard-entity: " + DescribeQaEntity(sandwichLava));
                sandwichLava.Waiting = false;
                SetInstanceMember(sandwichLava, "delay", 0f);
                InvokePrivateHazardOnPlayer(sandwichLava, player);
                Log("qa-invincibility-hazard-lava-y: " + sandwichLava.Y.ToString("0.###", CultureInfo.InvariantCulture));
                Log("qa-invincibility-hazard-lava-delay: " + FormatQaMember(sandwichLava, "delay"));
                break;
            case "crush":
            case "crushblock":
                Solid crushBlock = level.Entities.OfType<Solid>().FirstOrDefault(entity => entity.GetType().Name.IndexOf("CrushBlock", StringComparison.OrdinalIgnoreCase) >= 0);
                if (crushBlock == null) {
                    Log("qa-invincibility-hazard-missing: crush-block");
                    return;
                }

                Log("qa-invincibility-hazard-entity: " + DescribeQaEntity(crushBlock));
                CollisionData collisionData = new CollisionData {
                    Direction = Vector2.UnitX,
                    Moved = Vector2.Zero,
                    TargetPosition = player.Position,
                    Hit = crushBlock,
                    Pusher = crushBlock
                };
                player.OnSquish(collisionData);
                break;
            default:
                Log("usage: akron_qa_invincibility_hazard rising-lava|sandwich-lava|crush");
                return;
        }

        Log("qa-invincibility-hazard-after: " + DescribeQaPlayerHazardState(player));
    }

    [Command("akron_player_state", "show detailed player telemetry for Akron QA")]
    public static void PlayerState(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            Log("player: missing");
            return;
        }

        Log("frame: " + Engine.FrameCounter.ToString(CultureInfo.InvariantCulture));
        Log("level-time-active: " + level.TimeActive.ToString("0.000", CultureInfo.InvariantCulture));
        Log("level-raw-time-active: " + level.RawTimeActive.ToString("0.000", CultureInfo.InvariantCulture));
        Log("player-position: " + FormatVector(player.Position));
        Log("player-speed: " + FormatVector(player.Speed));
        Log("player-depth: " + player.Depth.ToString(CultureInfo.InvariantCulture));
        Log("player-visible: " + player.Visible.ToString().ToLowerInvariant());
        if (player.Hair?.Nodes != null && player.Hair.Nodes.Count > 0) {
            Log("player-hair-root: " + FormatVector(player.Hair.Nodes[0]));
            Log("player-hair-offset: " + FormatVector(player.Hair.Nodes[0] - player.Position));
        } else {
            Log("player-hair-root: unavailable");
            Log("player-hair-offset: unavailable");
        }
        Log("camera-position: " + FormatVector(level.Camera.Position));
        Log("camera-target: " + FormatVector(player.CameraTarget));
        Log("player-state: " + player.StateMachine.State.ToString(CultureInfo.InvariantCulture));
        Log("player-collidable: " + player.Collidable.ToString().ToLowerInvariant());
        Log("player-collider: " + FormatCollider(player.Collider));
        Log("player-dead: " + player.Dead.ToString().ToLowerInvariant());
        Log("player-stamina: " + player.Stamina.ToString("0.##", CultureInfo.InvariantCulture));
        Log("player-dashes: " + player.Dashes.ToString(CultureInfo.InvariantCulture));
        Log("player-inventory-dashes: " + player.Inventory.Dashes.ToString(CultureInfo.InvariantCulture));
        Log("player-inventory-dream-dash: " + player.Inventory.DreamDash.ToString().ToLowerInvariant());
        Log("session-inventory-dashes: " + level.Session.Inventory.Dashes.ToString(CultureInfo.InvariantCulture));
        Log("session-inventory-dream-dash: " + level.Session.Inventory.DreamDash.ToString().ToLowerInvariant());
        Log("air-jumps: " + AkronModule.Settings.JumpHack.ToString().ToLowerInvariant());
        Log("air-jumps-infinite: " + AkronModule.Settings.JumpHackInfinite.ToString().ToLowerInvariant());
        Log("air-jumps-extra: " + AkronModule.Settings.JumpHackExtraJumps.ToString(CultureInfo.InvariantCulture));
        Log("air-jumps-dash-verticals: " + AkronModule.Settings.JumpHackAllowVerticalDashJumps.ToString().ToLowerInvariant());
        Log("set-inventory-restore: " + (AkronModule.Session?.SetInventoryRestoreSnapshot != null).ToString().ToLowerInvariant());
        Log("level-core-mode: " + level.CoreMode);
        int gliderCount = 0;
        Glider firstGlider = null;
        int theoCount = 0;
        TheoCrystal firstTheo = null;
        foreach (Entity entity in level.Entities) {
            if (entity is Glider glider) {
                firstGlider ??= glider;
                gliderCount++;
            } else if (entity is TheoCrystal theo) {
                firstTheo ??= theo;
                theoCount++;
            }
        }

        Log("glider-count: " + gliderCount.ToString(CultureInfo.InvariantCulture));
        if (firstGlider != null) {
            Log("first-glider-position: " + FormatVector(firstGlider.Position));
        }
        Log("theo-count: " + theoCount.ToString(CultureInfo.InvariantCulture));
        if (firstTheo != null) {
            Log("first-theo-position: " + FormatVector(firstTheo.Position));
        }
        Log("player-on-ground: " + player.OnGround().ToString().ToLowerInvariant());
        Log("player-collides-solid: " + player.CollideCheck<Solid>().ToString().ToLowerInvariant());
        Log("player-collides-spikes: " + player.CollideCheck<Spikes>().ToString().ToLowerInvariant());
    }

    [Command("akron_qa_area_stats", "show raw SaveData AreaModeStats for the active level")]
    public static void QaAreaStats(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        LogAreaStats(level.Session.Area, level.Completed);
    }

    [Command("akron_qa_save_area_stats", "show raw SaveData AreaModeStats by area and mode: [area-id] [normal|b|c]")]
    public static void QaSaveAreaStats(string areaIdText = "0", string modeText = "normal") {
        if (!int.TryParse(areaIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId)) {
            Log("qa-save-area-stats: invalid-area area=" + areaIdText);
            return;
        }

        AreaMode mode = ParseAreaMode(modeText);
        AreaData data = GetQaAreaData(areaId, "qa-save-area-stats");
        if (data == null) {
            return;
        }

        LogAreaStats(data.ToKey(mode), levelCompleted: null);
    }

    [Command("akron_qa_unlock_state", "show raw SaveData unlock fields for unlock-system QA")]
    public static void QaUnlockState() {
        SaveData saveData = SaveData.Instance;
        Log("qa-unlock-slot: " + (saveData?.FileSlot ?? -1).ToString(CultureInfo.InvariantCulture));
        if (saveData == null) {
            Log("qa-unlock-state: missing-save-data");
            return;
        }

        int cassetteCount = 0;
        int cassetteEligibleCount = 0;
        for (int areaId = 0; areaId <= saveData.MaxArea && areaId < saveData.Areas.Count; areaId++) {
            AreaData areaData = AreaData.Get(areaId);
            if (areaData != null && !areaData.Interlude && areaData.HasMode(AreaMode.BSide)) {
                cassetteEligibleCount++;
                if (saveData.Areas[areaId].Cassette) {
                    cassetteCount++;
                }
            }
        }

        Log("qa-unlock-cheat-mode: " + saveData.CheatMode.ToString().ToLowerInvariant());
        Log("qa-unlock-areas: " + saveData.UnlockedAreas.ToString(CultureInfo.InvariantCulture) +
            "/" + saveData.MaxArea.ToString(CultureInfo.InvariantCulture));
        Log("qa-unlock-revealed-chapter9: " + saveData.RevealedChapter9.ToString().ToLowerInvariant());
        Log("qa-unlock-cassettes: " + cassetteCount.ToString(CultureInfo.InvariantCulture) +
            "/" + cassetteEligibleCount.ToString(CultureInfo.InvariantCulture));
        Log("qa-unlock-pico8-main-menu: " + (Settings.Instance?.Pico8OnMainMenu ?? false).ToString().ToLowerInvariant());
        Log("qa-unlock-variants-unlocked: " + (Settings.Instance?.VariantsUnlocked ?? false).ToString().ToLowerInvariant());
    }

    private static void LogAreaStats(AreaKey area, bool? levelCompleted) {
        AreaModeStats modeStats = null;
        if (SaveData.Instance?.Areas != null &&
            area.ID >= 0 &&
            area.ID < SaveData.Instance.Areas.Count) {
            AreaStats areaStats = SaveData.Instance.Areas[area.ID];
            int mode = (int) area.Mode;
            if (areaStats?.Modes != null && mode >= 0 && mode < areaStats.Modes.Length) {
                modeStats = areaStats.Modes[mode];
            }
        }

        Log("qa-area-stats-slot: " + (SaveData.Instance?.FileSlot ?? -1).ToString(CultureInfo.InvariantCulture));
        Log("qa-area-stats-area: " + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + area.Mode + ";sid=" + area.GetSID());
        Log("qa-area-stats-level-completed: " + (levelCompleted.HasValue ? levelCompleted.Value.ToString().ToLowerInvariant() : "unavailable"));
        if (modeStats == null) {
            Log("qa-area-stats: missing");
            return;
        }

        Log("qa-area-stats-completed: " + FormatQaStatMember(modeStats, "Completed"));
        Log("qa-area-stats-single-run-completed: " + FormatQaStatMember(modeStats, "SingleRunCompleted"));
        Log("qa-area-stats-deaths: " + modeStats.Deaths.ToString(CultureInfo.InvariantCulture));
        Log("qa-area-stats-jumps: " + FormatQaStatMember(modeStats, "Jumps"));
        Log("qa-area-stats-best-time: " + modeStats.BestTime.ToString(CultureInfo.InvariantCulture));
        Log("qa-area-stats-best-full-clear-time: " + modeStats.BestFullClearTime.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryResolveLabelRowKey(string value, out string key) {
        key = null;
        string normalizedValue = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(normalizedValue)) {
            return false;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        List<string> order = AkronModuleSettings.NormalizeLabelRowOrder(settings.LabelRowOrder, settings.CustomHudLabelDefinitions);
        if (normalizedValue is "customactive" or "activecustom") {
            AkronCustomHudLabel active = AkronCustomHudLabels.GetActiveLabel();
            if (!string.IsNullOrWhiteSpace(active?.Id)) {
                key = AkronModuleSettings.BuildCustomLabelRowKey(active.Id);
                return order.Contains(key, StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (string candidate in order) {
            if (NormalizeToken(candidate) == normalizedValue) {
                key = candidate;
                return true;
            }

            if (!candidate.StartsWith(AkronModuleSettings.CustomLabelRowPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string id = candidate.Substring(AkronModuleSettings.CustomLabelRowPrefix.Length);
            AkronCustomHudLabel label = settings.CustomHudLabelDefinitions?
                .FirstOrDefault(label => string.Equals(label?.Id, id, StringComparison.OrdinalIgnoreCase));
            if (NormalizeToken(id) == normalizedValue ||
                NormalizeToken(label?.Name ?? string.Empty) == normalizedValue ||
                NormalizeToken("custom" + (label?.Name ?? string.Empty)) == normalizedValue) {
                key = candidate;
                return true;
            }
        }

        return false;
    }

    private static void MoveQaLabelRow(string rowKey, string targetKey, bool afterTarget) {
        AkronModuleSettings settings = AkronModule.Settings;
        List<string> order = AkronModuleSettings.NormalizeLabelRowOrder(settings.LabelRowOrder, settings.CustomHudLabelDefinitions);
        int from = order.FindIndex(key => string.Equals(key, rowKey, StringComparison.OrdinalIgnoreCase));
        int to = order.FindIndex(key => string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || to < 0 || from == to) {
            return;
        }

        string moved = order[from];
        order.RemoveAt(from);
        if (from < to) {
            to--;
        }

        order.Insert(afterTarget ? Math.Min(order.Count, to + 1) : to, moved);
        settings.LabelRowOrder = order;
    }

    private static void LogQaLabelRowOrder() {
        AkronModuleSettings settings = AkronModule.Settings;
        List<string> order = AkronModuleSettings.NormalizeLabelRowOrder(settings.LabelRowOrder, settings.CustomHudLabelDefinitions);
        settings.LabelRowOrder = order;
        Log("qa-label-row-order: " + string.Join(" > ", order.Select(DescribeQaLabelRowKey)));
    }

    private static string DescribeQaLabelRowKey(string key) {
        if (key == null ||
            !key.StartsWith(AkronModuleSettings.CustomLabelRowPrefix, StringComparison.OrdinalIgnoreCase)) {
            return key ?? string.Empty;
        }

        string id = key.Substring(AkronModuleSettings.CustomLabelRowPrefix.Length);
        AkronCustomHudLabel label = AkronModule.Settings.CustomHudLabelDefinitions?
            .FirstOrDefault(label => string.Equals(label?.Id, id, StringComparison.OrdinalIgnoreCase));
        return "Custom:" + (string.IsNullOrWhiteSpace(label?.Name) ? id : label.Name);
    }

    private static string FormatQaStatMember(object target, string name) {
        if (target == null) {
            return "missing";
        }

        Type type = target.GetType();
        object value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ??
                       type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
        return value == null ? "missing" : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static void SetQaCutsceneSkipCallback(Level level, Action<Level> callback) {
        FieldInfo field = typeof(Level).GetField("onCutsceneSkip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(level, callback);
    }

    private static string FormatCollider(Collider collider) {
        if (collider == null) {
            return "none";
        }

        Rectangle bounds = collider.Bounds;
        return collider.GetType().Name +
               ";local=" + FormatVector(collider.Position) +
               ";size=" + collider.Width.ToString("0.###", CultureInfo.InvariantCulture) + "x" + collider.Height.ToString("0.###", CultureInfo.InvariantCulture) +
               ";bounds=" + bounds.X.ToString(CultureInfo.InvariantCulture) + "," +
               bounds.Y.ToString(CultureInfo.InvariantCulture) + "," +
               bounds.Width.ToString(CultureInfo.InvariantCulture) + "," +
               bounds.Height.ToString(CultureInfo.InvariantCulture);
    }

    [Command("akron_qa_sound_sources", "show SoundSource playback telemetry for Akron QA: status|start-player-loop")]
    public static void QaSoundSources(string action = "status") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (NormalizeToken(action) == "startplayerloop") {
            Player player = level.Tracker.GetEntity<Player>();
            if (player == null) {
                Log("sound-source-start-player-loop: missing");
                return;
            }

            SoundSource soundSource = new SoundSource(SFX.ui_game_general_text_loop);
            player.Add(soundSource);
            soundSource.Play(SFX.ui_game_general_text_loop);
            Log("sound-source-start-player-loop: requested");
        }

        int total = 0;
        int playing = 0;
        int instancePlaying = 0;
        int playingWithoutInstance = 0;
        foreach (Entity entity in AkronEntityListInternals.GetAll(level.Entities)) {
            foreach (SoundSource soundSource in entity.Components.GetAll<SoundSource>()) {
                total++;
                if (soundSource.Playing) {
                    playing++;
                    if (soundSource.InstancePlaying) {
                        instancePlaying++;
                    } else {
                        playingWithoutInstance++;
                    }
                }
            }
        }

        Log("sound-source-total: " + total.ToString(CultureInfo.InvariantCulture));
        Log("sound-source-playing: " + playing.ToString(CultureInfo.InvariantCulture));
        Log("sound-source-instance-playing: " + instancePlaying.ToString(CultureInfo.InvariantCulture));
        Log("sound-source-playing-without-instance: " + playingWithoutInstance.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_audio_state", "show deterministic audio QA state for speed, pitch, splitter, and per-SFX volume")]
    public static void QaAudioState(string eventName = "") {
        Log("qa-audio-splitter: " + AkronModule.Settings.AudioSplitter.ToString().ToLowerInvariant());
        Log("qa-audio-splitter-status: " + AkronAudioSplitter.Status());
        Log("qa-audio-runtime-music-volume: " + (Audio.MusicVolume * 10f).ToString("0.###", CultureInfo.InvariantCulture));
        Log("qa-audio-runtime-sfx-volume: " + (Audio.SfxVolume * 10f).ToString("0.###", CultureInfo.InvariantCulture));
        Log("qa-audio-speed: " + AkronRuntimeOptions.DescribeAudioSpeed());
        Log("qa-audio-pitch-shift: " + AkronRuntimeOptions.DescribePitchShift());
        Log("qa-audio-music-pitch: " + DescribeQaEventPitch(Audio.CurrentMusicEventInstance));
        Log("qa-audio-ambience-pitch: " + DescribeQaEventPitch(Audio.CurrentAmbienceEventInstance));

        if (!string.IsNullOrWhiteSpace(eventName)) {
            Log("qa-sfx-volume:" + eventName + ": " + AkronEarAid.VolumeForEventNameForTesting(eventName).ToString("0.###", CultureInfo.InvariantCulture));
            return;
        }

        LogQaSfxVolume("death", "event:/char/madeline/death");
        LogQaSfxVolume("spring", "event:/game/general/spring");
        LogQaSfxVolume("fireball", "event:/game/04_cliffside/fireball");
        LogQaSfxVolume("ridge-wind", "event:/env/amb/04_cliffside/ridge_wind");
        LogQaSfxVolume("dialogue", "event:/ui/game/textbox_madeline");
    }

    private static void LogQaSfxVolume(string key, string eventName) {
        bool enabled = AkronEarAid.OverrideEnabled(key);
        int configured = AkronEarAid.VolumeFor(key);
        float effective = AkronEarAid.VolumeForEventNameForTesting(eventName);
        Log("qa-sfx-volume:" + key + ": enabled=" + enabled.ToString().ToLowerInvariant() +
            ";configured=" + configured.ToString(CultureInfo.InvariantCulture) +
            ";event=" + eventName +
            ";effective=" + effective.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string DescribeQaEventPitch(FMOD.Studio.EventInstance instance) {
        if (instance == null) {
            return "missing";
        }

        try {
            FMOD.RESULT result = instance.getPitch(out float pitch, out float finalPitch);
            return result + ";pitch=" + pitch.ToString("0.###", CultureInfo.InvariantCulture) +
                   ";final=" + finalPitch.ToString("0.###", CultureInfo.InvariantCulture);
        } catch (Exception ex) {
            return "error:" + ex.GetType().Name + ":" + ex.Message;
        }
    }

    [Command("akron_input_state", "show live Celeste input values for Akron QA")]
    public static void InputState(string _ = "") {
        Log("input-move-x: " + Input.MoveX.Value.ToString(CultureInfo.InvariantCulture));
        Log("input-move-y: " + Input.MoveY.Value.ToString(CultureInfo.InvariantCulture));
        Log("input-aim: " + FormatVector(Input.Aim.Value));
        Log("input-jump: " + Input.Jump.Check.ToString().ToLowerInvariant());
        Log("input-dash: " + Input.Dash.Check.ToString().ToLowerInvariant());
        Log("input-grab: " + Input.Grab.Check.ToString().ToLowerInvariant());
        Log("input-crouch-dash: " + Input.CrouchDash.Check.ToString().ToLowerInvariant());
    }

    [Command("akron_qa_cursor_zoom_frame", "apply one Cursor Zoom frame for live QA: [percent] [screen-x] [screen-y]")]
    public static void QaCursorZoomFrame(string percentText = "50", string screenXText = "640", string screenYText = "360") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        if (!int.TryParse(percentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent) ||
            !float.TryParse(screenXText, NumberStyles.Float, CultureInfo.InvariantCulture, out float screenX) ||
            !float.TryParse(screenYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float screenY)) {
            Log("usage: akron_qa_cursor_zoom_frame <percent> <screen-x> <screen-y>");
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.CursorZoom)) {
            Log("qa-cursor-zoom-frame: blocked");
            return;
        }

        AkronModule.Settings.CursorZoom = true;
        AkronModule.Settings.CursorZoomAllowZoomOut = percent < 100 || AkronModule.Settings.CursorZoomAllowZoomOut;
        AkronModule.Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(percent, AkronModule.Settings.CursorZoomAllowZoomOut);
        AkronModule.ApplyCursorZoomFrame(level, new Vector2(screenX, screenY));
        Log("qa-cursor-zoom-frame: " + AkronModule.DescribeCursorZoom(level));
    }

    [Command("akron_qa_cursor_tools_state", "show Cursor Tools effective state with a simulated held binding")]
    public static void QaCursorToolsState(string clickAction = "") {
        if (string.Equals(NormalizeToken(clickAction), "inspector", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.CursorToolsClickAction = AkronCursorToolsClickAction.InspectorPin;
        } else if (string.Equals(NormalizeToken(clickAction), "teleport", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeToken(clickAction), "clickteleport", StringComparison.OrdinalIgnoreCase)) {
            AkronModule.Settings.CursorToolsClickAction = AkronCursorToolsClickAction.ClickTeleport;
        }

        bool simulatedHold = AkronModule.ShouldUseCursorToolsHold(AkronModule.Settings.CursorTools, true, false);
        AkronCursorToolsClickAction normalizedClickAction = AkronModuleSettings.NormalizeCursorToolsClickAction(AkronModule.Settings.CursorToolsClickAction);
        Log("qa-cursor-tools-enabled: " + AkronModule.Settings.CursorTools.ToString().ToLowerInvariant());
        Log("qa-cursor-tools-simulated-hold: " + simulatedHold.ToString().ToLowerInvariant());
        Log("qa-cursor-tools-click-action: " + normalizedClickAction);
        Log("qa-cursor-tools-click-teleport-effective: " + AkronModule.IsClickTeleportCursorActive(false, false, simulatedHold, normalizedClickAction == AkronCursorToolsClickAction.ClickTeleport).ToString().ToLowerInvariant());
        Log("qa-cursor-tools-inspector-effective: " + (simulatedHold && normalizedClickAction == AkronCursorToolsClickAction.InspectorPin).ToString().ToLowerInvariant());
        Log("qa-cursor-tools-cursor-zoom-effective: " + AkronModule.IsCursorToolEffectiveEnabled(false, simulatedHold, AkronModule.Settings.CursorToolsCursorZoom).ToString().ToLowerInvariant());
        Log("qa-cursor-tools-free-camera-effective: " + AkronModule.IsCursorToolEffectiveEnabled(false, simulatedHold, AkronModule.Settings.CursorToolsFreeCamera).ToString().ToLowerInvariant());
        Log("qa-cursor-tools-freeze-gameplay-effective: " + (simulatedHold && AkronModule.Settings.CursorToolsFreeCamera && AkronModule.Settings.CursorToolsFreezeGameplay).ToString().ToLowerInvariant());
    }

    [Command("akron_qa_air_jump_policy", "show Air Jumps policy decisions: jumpGrace state dashX dashY allowVerticals")]
    public static void QaAirJumpPolicy(
        string jumpGraceTimerText = "0",
        string playerStateText = "0",
        string dashXText = "0",
        string dashYText = "0",
        string allowVerticalsText = "false") {
        if (!float.TryParse(jumpGraceTimerText, NumberStyles.Float, CultureInfo.InvariantCulture, out float jumpGraceTimer) ||
            !int.TryParse(playerStateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerState) ||
            !float.TryParse(dashXText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dashX) ||
            !float.TryParse(dashYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dashY) ||
            !TryParseBoolean(allowVerticalsText, out bool allowVerticals)) {
            Log("usage: akron_qa_air_jump_policy <jumpGrace> <state> <dashX> <dashY> <allowVerticals>");
            return;
        }

        Vector2 dashDirection = new Vector2(dashX, dashY);
        Log("air-jump-preserve-vanilla: " + AkronModule.ShouldPreserveVanillaJumpForAirJump(jumpGraceTimer).ToString().ToLowerInvariant());
        Log("air-jump-skip-dash-direction: " + AkronModule.ShouldSkipAirJumpForDashDirection(playerState, dashDirection, allowVerticals).ToString().ToLowerInvariant());
        Log("air-jump-use-super-jump: " + AkronModule.ShouldUseSuperJumpForAirJump(playerState, dashDirection).ToString().ToLowerInvariant());
    }

    [Command("akron_qa_pause", "pause or unpause the active Celeste level for live QA: pause|unpause|toggle|status")]
    public static void QaPause(string action = "status") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "pause":
            case "on":
                if (!level.Paused) {
                    level.Pause();
                }
                break;
            case "unpause":
            case "off":
                if (level.Paused) {
                    level.Unpause();
                }
                break;
            case "toggle":
                if (level.Paused) {
                    level.Unpause();
                } else {
                    level.Pause();
                }
                break;
            default:
                Log("usage: akron_qa_pause pause|unpause|toggle|status");
                return;
        }

        Log("qa-pause-paused: " + level.Paused.ToString().ToLowerInvariant());
    }

    [Command("akron_qa_pause_event", "invoke Akron pause telemetry hooks for live QA: pause|unpause")]
    public static void QaPauseEvent(string action = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        switch (NormalizeToken(action)) {
            case "pause":
                AkronModule.NotifyPauseForQa(level);
                Log("qa-pause-event: pause");
                break;
            case "unpause":
                AkronModule.NotifyUnpauseForQa(level);
                Log("qa-pause-event: unpause");
                break;
            default:
                Log("usage: akron_qa_pause_event pause|unpause");
                return;
        }

        Log("qa-pause-paused: " + level.Paused.ToString().ToLowerInvariant());
    }

    [Command("akron_qa_pause_state", "show compact pause telemetry for live QA")]
    public static void QaPauseState(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        AkronModuleSession session = AkronModule.Session;
        Log("qa-pause-paused: " + level.Paused.ToString().ToLowerInvariant());
        Log("qa-pause-tracker: " + AkronModule.Settings.PauseTracker.ToString().ToLowerInvariant());
        Log("qa-pause-tracker-count: " + session.PauseTrackerPauseCount.ToString(CultureInfo.InvariantCulture));
        Log("qa-pause-tracker-rapid-count: " + session.PauseTrackerRapidPauseCount.ToString(CultureInfo.InvariantCulture));
        Log("qa-pause-tracker-paused-seconds: " + session.PauseTrackerPausedSeconds.ToString("0.000", CultureInfo.InvariantCulture));
        Log("qa-freeze-timer-paused: " + AkronModule.Settings.FreezeTimerWhilePaused.ToString().ToLowerInvariant());
        Log("qa-pause-countdown: " + AkronModule.Settings.PauseCountdown.ToString().ToLowerInvariant());
        Log("qa-pause-countdown-seconds: " + AkronModule.Settings.PauseCountdownSeconds.ToString("0.0", CultureInfo.InvariantCulture));
        Log("qa-pause-countdown-remaining: " + AkronModule.PauseCountdownRemaining.ToString("0.000", CultureInfo.InvariantCulture));
        Log("qa-level-time-active: " + level.TimeActive.ToString("0.000", CultureInfo.InvariantCulture));
        Log("qa-level-raw-time-active: " + level.RawTimeActive.ToString("0.000", CultureInfo.InvariantCulture));
        if (player != null) {
            Log("qa-player-position: " + FormatVector(player.Position));
            Log("qa-player-speed: " + FormatVector(player.Speed));
        }
    }

    [Command("akron_qa_sleep", "block the level update thread for live QA frame-spike checks: [milliseconds]")]
    public static void QaSleep(string millisecondsText = "300") {
        if (!int.TryParse(millisecondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds)) {
            Log("usage: akron_qa_sleep <milliseconds>");
            return;
        }

        milliseconds = Calc.Clamp(milliseconds, 0, 5000);
        Thread.Sleep(milliseconds);
        Log("qa-sleep-ms: " + milliseconds.ToString(CultureInfo.InvariantCulture));
    }

    [Command("akron_qa_death_visual_state", "show compact death visual telemetry for live QA")]
    public static void QaDeathVisualState(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        foreach (string line in AkronModule.DescribeDeathVisualStateForQa(level)) {
            Log(line);
        }
        Log(AkronModule.DescribeDeathWipeStateForQa(level));
    }

    [Command("akron_qa_freeze_frame", "trigger a Celeste freeze frame and report Engine.FreezeTimer: [seconds]")]
    public static void QaFreezeFrame(string secondsText = "0.2") {
        if (!float.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)) {
            Log("usage: akron_qa_freeze_frame <seconds>");
            return;
        }

        seconds = Calc.Clamp(seconds, 0f, 2f);
        Engine.FreezeTimer = 0f;
        Log("qa-freeze-before: " + Engine.FreezeTimer.ToString("0.000", CultureInfo.InvariantCulture));
        global::Celeste.Celeste.Freeze(seconds);
        Log("qa-freeze-requested: " + seconds.ToString("0.000", CultureInfo.InvariantCulture));
        Log("qa-freeze-after: " + Engine.FreezeTimer.ToString("0.000", CultureInfo.InvariantCulture));
    }

#if DEBUG
    [Command("akron_qa_stress", "debug-only Akron UI stress mode: on [seed]|off|status|poison-render-state")]
    public static void QaStress(string action = "status", string seedText = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                Log(AkronModule.StressStatus());
                return;
            case "on":
            case "start":
                int seed = 0xA0001;
                if (!string.IsNullOrWhiteSpace(seedText) &&
                    !int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) {
                    Log("qa-stress: invalid seed=" + seedText);
                    return;
                }

                AkronModule.StartStressMode(seed);
                Log(AkronModule.StressStatus());
                return;
            case "off":
            case "stop":
                AkronModule.StopStressMode();
                Log(AkronModule.StressStatus());
                return;
            case "poisonrenderstate":
            case "poison-render-state":
                AkronImGuiRenderer.PoisonNextFrameForTest();
                Log("qa-stress: poison-render-state armed");
                return;
            default:
                Log("usage: akron_qa_stress on [seed]|off|status|poison-render-state");
                return;
        }
    }
#endif

    [Command("akron_qa_probe", "prepare repeatable Akron QA states: low-stamina|zero-dash|start-dash|right-speed|nearest-spike|force-death")]
    public static void QaProbe(string action = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            Log("player: missing");
            return;
        }

        switch (NormalizeToken(action)) {
            case "lowstamina":
                // This intentionally creates the drain state that Infinite Stamina
                // should repair on the next normal Level.Update.
                player.Stamina = 1f;
                Log("qa-probe: low-stamina");
                break;
            case "zerodash":
                // This intentionally creates the depleted dash state that Infinite
                // Dash should repair on the next normal Level.Update.
                player.Dashes = 0;
                Log("qa-probe: zero-dash");
                break;
            case "startdash":
                // Physical key injection is unreliable on the remote Xorg/Steam
                // stack, so this invokes Celeste's own dash entrypoint directly.
                // It still exercises the same Player.StartDash resource transition
                // that a valid dash input reaches after Celeste's input layer.
                player.Dashes = Math.Max(1, player.Dashes);
                Log("qa-probe-before-dash-count: " + player.Dashes.ToString(CultureInfo.InvariantCulture));
                int dashState = player.StartDash();
                Log("qa-probe: start-dash");
                Log("qa-probe-dash-state: " + dashState.ToString(CultureInfo.InvariantCulture));
                break;
            case "rightspeed":
                // This creates deterministic horizontal motion so freeze/pause
                // tests can compare player position instead of relying on timers.
                player.Speed = new Vector2(180f, 0f);
                Log("qa-probe: right-speed");
                break;
            case "nearestspike":
                if (!TryMovePlayerToNearestSpikes(level, player)) {
                    Log("qa-probe: no spikes found");
                    return;
                }
                Log("qa-probe: nearest-spike");
                break;
            case "forcedeath":
                // This exercises the same Player.Die hook used by spike hazards,
                // without depending on a specific room's spike geometry.
                player.Die(Vector2.UnitY, evenIfInvincible: false, registerDeathInStats: false);
                Log("qa-probe: force-death");
                break;
            case "forcedeathstats":
                player.Die(Vector2.UnitY, evenIfInvincible: false, registerDeathInStats: true);
                Log("qa-probe: force-death-stats");
                break;
            case "visualnoise":
                Glitch.Value = 1f;
                Distort.Anxiety = 1f;
                Distort.GameRate = 0.5f;
                level.Particles.Emit(Player.P_Split, 16, player.Center, Vector2.One * 6f);
                TrailManager.Add(player, Player.NormalHairColor, 1f);
                Log("qa-probe: visual-noise");
                LogVisualNoiseSettings();
                break;
            default:
                Log("usage: akron_qa_probe <low-stamina|zero-dash|start-dash|right-speed|nearest-spike|force-death|force-death-stats|visual-noise>");
                return;
        }

        LogPlayerSummary(player);
    }

    private static bool TryGetQaBackup(string indexText, out AkronBackupEntry backup) {
        backup = null;
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) {
            Log("qa-backup: invalid-index index=" + indexText);
            return false;
        }

        IReadOnlyList<AkronBackupEntry> backups = AkronBackupActions.ListBackups();
        if (index < 0 || index >= backups.Count) {
            Log("qa-backup: index-out-of-range index=" + index.ToString(CultureInfo.InvariantCulture) +
                ";count=" + backups.Count.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        backup = backups[index];
        return true;
    }

    private static bool ParseQaBool(string value, bool defaultValue) {
        switch (NormalizeToken(value)) {
            case "on":
            case "true":
            case "yes":
            case "1":
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                return false;
            default:
                return defaultValue;
        }
    }

    private static int FindQaStartPosSlot(AkronStartPos candidate) {
        if (candidate == null || AkronModule.Session?.StartPositions == null) {
            return 0;
        }

        foreach (KeyValuePair<int, AkronStartPos> pair in AkronModule.Session.StartPositions) {
            if (ReferenceEquals(pair.Value, candidate)) {
                return pair.Key;
            }
        }

        return 0;
    }

    private static void LogQaScreenshakeState(Level level) {
        bool disabled = GetInstanceMember(level, "DisableScreenShake") is bool disabledValue && disabledValue;
        Vector2 vector = GetInstanceMember(level, "ShakeVector") is Vector2 shakeVector ? shakeVector : Vector2.Zero;
        Log("qa-screenshake-enabled: " + AkronModule.Settings.Screenshake.ToString().ToLowerInvariant());
        Log("qa-screenshake-intensity: " + AkronModule.Settings.ScreenshakeIntensity.ToString(CultureInfo.InvariantCulture));
        Log("qa-screenshake-disabled: " + disabled.ToString().ToLowerInvariant());
        Log("qa-screenshake-vector: " + FormatVector(vector));
    }

    private static void InvokePrivateHazardOnPlayer(Entity hazard, Player player) {
        MethodInfo onPlayer = hazard.GetType().GetMethod("OnPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        if (onPlayer == null) {
            Log("qa-invincibility-hazard-on-player: missing");
            return;
        }

        onPlayer.Invoke(hazard, new object[] { player });
        Log("qa-invincibility-hazard-on-player: invoked");
    }

    private static string DescribeQaEntity(Entity entity) {
        return entity.GetType().Name + ";position=" + FormatVector(entity.Position);
    }

    private static string DescribeQaPlayerHazardState(Player player) {
        return "position=" + FormatVector(player.Position) +
               ";speed=" + FormatVector(player.Speed) +
               ";dashes=" + player.Dashes.ToString(CultureInfo.InvariantCulture) +
               ";dead=" + player.Dead.ToString().ToLowerInvariant() +
               ";collidable=" + player.Collidable.ToString().ToLowerInvariant();
    }

    private static string FormatQaMember(object target, string name) {
        object value = GetInstanceMember(target, name);
        return value switch {
            null => "unset",
            float floatValue => floatValue.ToString("0.###", CultureInfo.InvariantCulture),
            Vector2 vectorValue => FormatVector(vectorValue),
            _ => value.ToString()
        };
    }

    private static object GetInstanceMember(object target, string name) {
        if (target == null) {
            return null;
        }

        Type type = target.GetType();
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ??
               type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    }

    private static void SetInstanceMember(object target, string name, object value) {
        if (target == null) {
            return;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true) {
            property.SetValue(target, value);
            return;
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }

    private static void LogQaBackups() {
        IReadOnlyList<AkronBackupEntry> backups = AkronBackupActions.ListBackups();
        Log("qa-backup-count: " + backups.Count.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < backups.Count; index++) {
            AkronBackupEntry backup = backups[index];
            Log("qa-backup-entry: index=" + index.ToString(CultureInfo.InvariantCulture) +
                ";file=" + backup.FileName +
                ";reason=" + backup.Reason +
                ";slot=" + backup.SaveSlot +
                ";pinned=" + backup.Pinned.ToString().ToLowerInvariant() +
                ";size=" + backup.SizeBytes.ToString(CultureInfo.InvariantCulture) +
                ";path=" + Path.GetFileName(backup.Path));
        }
    }

    private sealed class AkronQaCustomRefillProbe : Entity {
        public readonly bool oneUse;

        public AkronQaCustomRefillProbe(Vector2 position, bool oneUse) : base(position) {
            this.oneUse = oneUse;
            Collider = new Hitbox(12f, 12f, -6f, -6f);
            Visible = true;
            Collidable = true;
            Active = true;
        }
    }
}
