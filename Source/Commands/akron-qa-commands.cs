using System;
using System.Collections.Generic;
using System.Globalization;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    // Command-only QA probes and telemetry. These intentionally create or report
    // controlled runtime states for repeatable live verification, not player UI.
    [Command("akron_qa_area_complete", "trigger Level.RegisterAreaComplete for Akron proof automation")]
    public static void QaAreaComplete(string _ = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        level.RegisterAreaComplete();
        Log("area-complete: registered");
    }

    [Command("akron_qa_enter_level", "enter a vanilla level for Akron live automation: [area-id] [normal|b|c]")]
    public static void QaEnterLevel(string areaIdText = "0", string modeText = "normal") {
        if (!int.TryParse(areaIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId)) {
            Log("qa-enter-level: invalid-area area=" + areaIdText);
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
        SaveData saveData = CreateQaSaveData(area);
        AreaStats stats = EnsureQaAreaStats(saveData, area);
        Session session = new Session(area, null, stats);
        saveData.StartSession(session);
        Engine.Scene = new LevelLoader(session);
        Log("qa-enter-level: area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + mode);
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

    private static SaveData CreateQaSaveData(AreaKey area) {
        // QA entry must not depend on the user's persisted save or mod-session
        // files. Celeste's debug initializer still builds the normal area-stat
        // graph that helper mods expect while keeping the slot non-persistent.
        SaveData.InitializeDebugMode(loadExisting: false);
        SaveData saveData = SaveData.Instance ?? new SaveData();
        saveData.DebugMode = true;
        saveData.DoNotSave = true;
        saveData.HasModdedSaveData = true;
        saveData.LastArea = area;
        saveData.LastArea_Safe = area;
        if (SaveData.Instance != saveData) {
            SaveData.Start(saveData, -1);
        }
        Log("qa-save-data: transient debug slot for area=" + area.ID.ToString(CultureInfo.InvariantCulture) + ";mode=" + area.Mode);
        return saveData;
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
        Log("camera-position: " + FormatVector(level.Camera.Position));
        Log("camera-target: " + FormatVector(player.CameraTarget));
        Log("player-state: " + player.StateMachine.State.ToString(CultureInfo.InvariantCulture));
        Log("player-collidable: " + player.Collidable.ToString().ToLowerInvariant());
        Log("player-dead: " + player.Dead.ToString().ToLowerInvariant());
        Log("player-stamina: " + player.Stamina.ToString("0.##", CultureInfo.InvariantCulture));
        Log("player-dashes: " + player.Dashes.ToString(CultureInfo.InvariantCulture));
        Log("player-on-ground: " + player.OnGround().ToString().ToLowerInvariant());
        Log("player-collides-solid: " + player.CollideCheck<Solid>().ToString().ToLowerInvariant());
        Log("player-collides-spikes: " + player.CollideCheck<Spikes>().ToString().ToLowerInvariant());
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
}
