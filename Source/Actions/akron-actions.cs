using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Globalization;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronActions {
    private const string ImplicitStartCheckpointName = "Start";

    private static bool autoDeafenActive;

    public static bool AutoDeafenActive => autoDeafenActive;

    public static void CycleGrabMode() {
        AdjustGrabMode(1);
    }

    public static void AdjustGrabMode(int delta) {
        if (delta == 0 || !AkronModule.TryUse(AkronFeatureKind.GrabModeHotkey)) {
            return;
        }

        GrabModes[] modes = { GrabModes.Hold, GrabModes.Toggle, GrabModes.Invert };
        int currentIndex = Array.IndexOf(modes, Settings.Instance.GrabMode);
        if (currentIndex < 0) {
            currentIndex = 0;
        }

        int nextIndex = (currentIndex + delta) % modes.Length;
        if (nextIndex < 0) {
            nextIndex += modes.Length;
        }

        Settings.Instance.GrabMode = modes[nextIndex];

        Engine.Scene?.Add(new AkronToast("Grab mode: " + Settings.Instance.GrabMode));
    }

    public static void ToggleFreeze() {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            Engine.Scene?.Add(new AkronToast("Freeze is unavailable outside a save session."));
            return;
        }

        bool next = !session.FreezeGameplay;

        // The action must be able to recover from a previously-enabled freeze
        // state while still respecting the current feature policy.
        if (next && !AkronModule.TryUse(AkronFeatureKind.Freeze)) {
            return;
        }

        session.FreezeGameplay = next;
        Engine.Scene?.Add(new AkronToast(session.FreezeGameplay ? "Gameplay frozen." : "Gameplay resumed."));
    }

    public static bool IsSetInventoryActive() {
        return AkronModule.Session?.SetInventoryRestoreSnapshot != null;
    }

    public static string DescribeSetInventory(Level level) {
        if (level?.Tracker.GetEntity<Player>() == null) {
            return "No player";
        }

        int dashes = AkronModuleSettings.ClampSetInventoryDashes(AkronModule.Settings.SetInventoryDashes);
        int jumps = AkronModuleSettings.ClampSetInventoryJumps(AkronModule.Settings.SetInventoryJumps);
        string summary = dashes.ToString(CultureInfo.InvariantCulture) + " dash" + (dashes == 1 ? string.Empty : "es") +
                         " / " + jumps.ToString(CultureInfo.InvariantCulture) + " jump" + (jumps == 1 ? string.Empty : "s");
        return IsSetInventoryActive() ? "On | " + summary : summary;
    }

    public static void ToggleSetInventory(Level level) {
        AkronModuleSession session = AkronModule.Session;
        if (session == null || level?.Tracker.GetEntity<Player>() == null) {
            Engine.Scene?.Add(new AkronToast("Set Inventory is unavailable outside gameplay."));
            return;
        }

        ApplySetInventory(level);
    }

    public static void ApplySetInventory(Level level) {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            Engine.Scene?.Add(new AkronToast("Set Inventory is unavailable outside gameplay."));
            return;
        }

        if (level?.Tracker.GetEntity<Player>() is not Player player) {
            Engine.Scene?.Add(new AkronToast("Set Inventory is unavailable without Madeline."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
            return;
        }

        if (AkronModule.Settings.SetInventoryRestoreOnDeath && session.SetInventoryRestoreSnapshot == null) {
            session.SetInventoryRestoreSnapshot = CaptureSetInventorySnapshot(level, player);
        } else if (!AkronModule.Settings.SetInventoryRestoreOnDeath) {
            session.SetInventoryRestoreSnapshot = null;
        }

        int dashes = AkronModuleSettings.ClampSetInventoryDashes(AkronModule.Settings.SetInventoryDashes);
        int jumps = AkronModuleSettings.ClampSetInventoryJumps(AkronModule.Settings.SetInventoryJumps);
        PlayerInventory inventory = level.Session.Inventory;
        inventory.Dashes = dashes;
        level.Session.Inventory = inventory;
        level.Session.Dashes = dashes;
        player.Dashes = dashes;
        ApplySetInventoryJumps(jumps);
        Engine.Scene?.Add(new AkronToast(
            "Inventory set: " +
            dashes.ToString(CultureInfo.InvariantCulture) +
            " dash" +
            (dashes == 1 ? string.Empty : "es") +
            ", " +
            jumps.ToString(CultureInfo.InvariantCulture) +
            " jump" +
            (jumps == 1 ? string.Empty : "s") +
            "."));
    }

    public static void ClearSetInventory() {
        if (AkronModule.Session != null) {
            AkronModule.Session.SetInventoryRestoreSnapshot = null;
        }
    }

    public static void RestoreSetInventoryOnDeath(Level level, Player player) {
        AkronModuleSession session = AkronModule.Session;
        AkronSetInventorySnapshot snapshot = session?.SetInventoryRestoreSnapshot;
        if (session == null || snapshot == null) {
            ClearSetInventory();
            return;
        }

        if (level != null) {
            PlayerInventory inventory = level.Session.Inventory;
            inventory.Dashes = AkronModuleSettings.ClampSetInventoryDashes(snapshot.SessionInventoryDashes);
            level.Session.Inventory = inventory;
            level.Session.Dashes = AkronModuleSettings.ClampSetInventoryDashes(snapshot.SessionDashes);
        }

        if (player != null) {
            player.Dashes = AkronModuleSettings.ClampSetInventoryDashes(snapshot.PlayerDashes);
        }

        AkronModule.Settings.JumpHack = snapshot.JumpHack;
        AkronModule.Settings.JumpHackInfinite = snapshot.JumpHackInfinite;
        AkronModule.Settings.JumpHackExtraJumps = AkronModuleSettings.ClampJumpHackExtraJumps(snapshot.JumpHackExtraJumps);
        AkronModule.Settings.JumpHackAllowVerticalDashJumps = snapshot.JumpHackAllowVerticalDashJumps;
        ClearSetInventory();
    }

    private static AkronSetInventorySnapshot CaptureSetInventorySnapshot(Level level, Player player) {
        return new AkronSetInventorySnapshot {
            SessionInventoryDashes = level.Session.Inventory.Dashes,
            SessionDashes = level.Session.Dashes,
            PlayerDashes = player.Dashes,
            JumpHack = AkronModule.Settings.JumpHack,
            JumpHackInfinite = AkronModule.Settings.JumpHackInfinite,
            JumpHackExtraJumps = AkronModule.Settings.JumpHackExtraJumps,
            JumpHackAllowVerticalDashJumps = AkronModule.Settings.JumpHackAllowVerticalDashJumps
        };
    }

    private static void ApplySetInventoryJumps(int jumps) {
        AkronModule.Settings.JumpHack = jumps > 0;
        AkronModule.Settings.JumpHackInfinite = false;
        if (jumps > 0) {
            AkronModule.Settings.JumpHackExtraJumps = AkronModuleSettings.ClampJumpHackExtraJumps(jumps);
        }
    }

    public static string DescribeDreamState(Level level) {
        Player player = level?.Tracker.GetEntity<Player>();
        return player == null ? "No player" : player.Inventory.DreamDash ? "On" : "Off";
    }

    public static void ToggleDreamState(Level level) {
        if (level?.Tracker.GetEntity<Player>() is not Player player) {
            Engine.Scene?.Add(new AkronToast("Dream State is unavailable without Madeline."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
            return;
        }

        PlayerInventory inventory = level.Session.Inventory;
        inventory.DreamDash = !inventory.DreamDash;
        level.Session.Inventory = inventory;
        Engine.Scene?.Add(new AkronToast("Dream State: " + (inventory.DreamDash ? "On" : "Off") + "."));
    }

    public static string DescribeCoreMode(Level level) {
        if (level == null) {
            return "No level";
        }

        string mode = FormatCoreMode(AkronModule.Settings.CoreModeOverride);
        return AkronModule.Settings.CoreModeOverrideEnabled ? "On | " + mode : "Off | " + mode;
    }

    public static void ToggleCoreMode(Level level) {
        AkronModuleSession session = AkronModule.Session;
        if (level == null || session == null) {
            Engine.Scene?.Add(new AkronToast("Core Mode is unavailable outside a level."));
            return;
        }

        if (AkronModule.Settings.CoreModeClickBehavior == AkronCoreModeClickBehavior.Cycle) {
            AkronCoreModeOverride nextMode = AkronModule.Settings.CoreModeOverride == AkronCoreModeOverride.Hot
                ? AkronCoreModeOverride.Cold
                : AkronCoreModeOverride.Hot;
            ApplyCoreModeOverride(level, session, nextMode);
            return;
        }

        if (!AkronModule.Settings.CoreModeOverrideEnabled) {
            ApplyCoreModeOverride(level, session, AkronModule.Settings.CoreModeOverride);
            return;
        }

        DisableCoreModeOverride(level, session);
    }

    public static void SetCoreModeOverrideEnabled(Level level, bool enabled) {
        AkronModuleSession session = AkronModule.Session;
        if (level == null || session == null) {
            Engine.Scene?.Add(new AkronToast("Core Mode is unavailable outside a level."));
            return;
        }

        if (enabled) {
            ApplyCoreModeOverride(level, session, AkronModule.Settings.CoreModeOverride);
            return;
        }

        DisableCoreModeOverride(level, session);
    }

    public static void ApplyCoreMode(Level level, AkronCoreModeOverride mode) {
        ApplyCoreModeOverride(level, AkronModule.Session, mode);
    }

    private static void ApplyCoreModeOverride(Level level, AkronModuleSession session, AkronCoreModeOverride mode) {
        if (level == null) {
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
            return;
        }

        if (session != null) {
            CaptureCoreModeRestoreSnapshot(level, session);
        }

        mode = AkronModuleSettings.NormalizeCoreModeOverride(mode);
        AkronModule.Settings.CoreModeOverride = mode;
        AkronModule.Settings.CoreModeOverrideEnabled = true;
        level.CoreMode = ToSessionCoreMode(mode);
        Engine.Scene?.Add(new AkronToast("Core Mode: " + FormatCoreMode(mode) + "."));
    }

    private static void CaptureCoreModeRestoreSnapshot(Level level, AkronModuleSession session) {
        if (session.CoreModeRestoreSnapshot == null) {
            session.CoreModeRestoreSnapshot = level.CoreMode;
        }
    }

    private static void DisableCoreModeOverride(Level level, AkronModuleSession session) {
        if (session.CoreModeRestoreSnapshot.HasValue) {
            level.CoreMode = session.CoreModeRestoreSnapshot.Value;
            session.CoreModeRestoreSnapshot = null;
        }

        AkronModule.Settings.CoreModeOverrideEnabled = false;
        Engine.Scene?.Add(new AkronToast("Core Mode override off."));
    }

    public static string FormatCoreMode(AkronCoreModeOverride mode) {
        return AkronModuleSettings.NormalizeCoreModeOverride(mode) == AkronCoreModeOverride.Cold ? "Cold" : "Hot";
    }

    public static string FormatCoreModeClickBehavior(AkronCoreModeClickBehavior behavior) {
        return AkronModuleSettings.NormalizeCoreModeClickBehavior(behavior) == AkronCoreModeClickBehavior.Cycle ? "Cycle" : "Toggle";
    }

    private static Session.CoreModes ToSessionCoreMode(AkronCoreModeOverride mode) {
        return AkronModuleSettings.NormalizeCoreModeOverride(mode) == AkronCoreModeOverride.Cold
            ? Session.CoreModes.Cold
            : Session.CoreModes.Hot;
    }

    public static void SpawnJelly(Level level) {
        if (level?.Tracker.GetEntity<Player>() is not Player player) {
            Engine.Scene?.Add(new AkronToast("Spawn Jelly is unavailable without Madeline."));
            return;
        }

        level.Add(new Glider(player.Position, false, false));
        Engine.Scene?.Add(new AkronToast("Spawned jelly."));
    }

    public static void SpawnTheo(Level level) {
        if (level?.Tracker.GetEntity<Player>() is not Player player) {
            Engine.Scene?.Add(new AkronToast("Spawn Theo is unavailable without Madeline."));
            return;
        }

        level.Add(new TheoCrystal(player.Position));
        Engine.Scene?.Add(new AkronToast("Spawned Theo."));
    }

    public static void AdjustTimescale(float delta) {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            Engine.Scene?.Add(new AkronToast("Timescale is unavailable outside a save session."));
            return;
        }

        float next = Calc.Clamp(session.TimescaleMultiplier + delta, 0.1f, 2f);

        // Resetting to canonical speed must stay available even when Cheat
        // speed changes are blocked, but blocked policy decisions must not allow moving away
        // from 1.0x.
        if (next != 1f && !AkronModule.TryUse(AkronFeatureKind.Timescale)) {
            return;
        }

        session.TimescaleMultiplier = next;
        if (next != 1f) {
            session.TimescaleEnabled = true;
        } else {
            session.TimescaleEnabled = false;
#pragma warning disable CS0618
            Engine.TimeRate = 1f;
#pragma warning restore CS0618
        }
        Engine.Scene?.Add(new AkronToast("Timescale: " + next.ToString("0.0x")));
    }

    public static void SetAllowLowVolume(bool enabled) {
        if (enabled && !AkronModule.TryUse(AkronFeatureKind.LowVolumeBypass)) {
            return;
        }

        AkronModule.Settings.AllowLowVolume = enabled;
        if (enabled) {
            ApplyLowVolumeBypass();
        } else {
            RestoreLowVolumeBypass();
        }
    }

    public static void SetLowVolumeMusic(float volume) {
        AkronModule.Settings.LowVolumeMusic = AkronModuleSettings.ClampLowVolumeLevel(volume);
        ApplyLowVolumeBypass();
    }

    public static void SetLowVolumeSfx(float volume) {
        AkronModule.Settings.LowVolumeSfx = AkronModuleSettings.ClampLowVolumeLevel(volume);
        ApplyLowVolumeBypass();
    }

    public static void ApplyLowVolumeBypass() {
        if (!AkronModule.Settings.AllowLowVolume) {
            return;
        }

        Audio.MusicVolume = AkronModuleSettings.ClampLowVolumeLevel(AkronModule.Settings.LowVolumeMusic) / 10f;
        Audio.SfxVolume = AkronModuleSettings.ClampLowVolumeLevel(AkronModule.Settings.LowVolumeSfx) / 10f;
    }

    public static void RestoreLowVolumeBypass() {
        if (Settings.Instance == null) {
            return;
        }

        Settings.Instance.ApplyMusicVolume();
        Settings.Instance.ApplySFXVolume();
    }

    public static bool ActivateAutoDeafen(out string error) {
        error = string.Empty;
        if (autoDeafenActive) {
            return true;
        }

        if (!AkronHotkey.TryParse(AkronModule.Settings.AutoDeafenHotkey, out AkronHotkey hotkey)) {
            error = "set the same hotkey you use for Discord Toggle Deafen first.";
            return false;
        }

        if (!hotkey.TrySend(out error)) {
            return false;
        }

        autoDeafenActive = true;
        return true;
    }

    public static void RestoreAutoDeafen() {
        if (!autoDeafenActive) {
            return;
        }

        bool restored = true;
        if (AkronHotkey.TryParse(AkronModule.Settings.AutoDeafenHotkey, out AkronHotkey hotkey)) {
            restored = hotkey.TrySend(out _);
        }

        // If injection fails during cleanup, prefer clearing Akron's internal state.
        // Keeping it stuck active would cause every later trigger to be ignored.
        autoDeafenActive = false;
        if (!restored) {
            Engine.Scene?.Add(new AkronToast("Auto Deafen restore hotkey failed."));
        }
    }

    public static bool ToggleAutoDeafenHotkeyForTest(out string error) {
        if (autoDeafenActive) {
            RestoreAutoDeafen();
            error = string.Empty;
            return true;
        }

        return ActivateAutoDeafen(out error);
    }

    public static string DescribeAutoDeafenHotkey() {
        return AkronHotkey.Describe(AkronModule.Settings.AutoDeafenHotkey);
    }

    public static bool SetAutoDeafenHotkey(string value, out string error) {
        error = string.Empty;
        if (!AkronHotkey.TryParse(value, out AkronHotkey hotkey)) {
            error = "invalid hotkey. Use examples like F7, Shift+F7, Ctrl+Shift+D, or A+B+C.";
            return false;
        }

        RestoreAutoDeafen();
        AkronModule.Settings.AutoDeafenHotkey = hotkey.ToStorageString();
        return true;
    }

    public static void InstantComplete(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.InstantComplete)) {
            return;
        }

        level.CompleteArea(false, true);
        Engine.Scene?.Add(new AkronToast("Instant complete requested."));
    }

    public static void OpenOptionsShortcut() {
        Scene scene = Engine.Scene;
        AkronOverlay overlay = AkronModule.GetOverlay(scene, ensureVisible: true);
        overlay?.OpenTab("Interface");
        Engine.Scene?.Add(new AkronToast("Opened Akron options."));
    }

    public static void ApplyCameraOffset(Level level) {
        if (level == null) {
            return;
        }

        if (!AkronModule.Settings.CameraOffset) {
            level.CameraOffset = Vector2.Zero;
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.CameraOffset)) {
            return;
        }

        level.CameraOffset = new Vector2(
            AkronModuleSettings.ClampCameraOffset(AkronModule.Settings.CameraOffsetX),
            AkronModuleSettings.ClampCameraOffset(AkronModule.Settings.CameraOffsetY));
    }

    public static void ResetCameraOffset(Level level) {
        AkronModule.Settings.CameraOffsetX = 0;
        AkronModule.Settings.CameraOffsetY = 0;
        if (level != null) {
            level.CameraOffset = Vector2.Zero;
        }
    }

    public static string DescribeCameraOffset(Level level) {
        Vector2 offset = AkronModule.Settings.CameraOffset && level != null
            ? level.CameraOffset
            : new Vector2(AkronModule.Settings.CameraOffsetX, AkronModule.Settings.CameraOffsetY);
        return (AkronModule.Settings.CameraOffset ? "on" : "off") +
               ";offset=" + offset.X.ToString("0", CultureInfo.InvariantCulture) + "," + offset.Y.ToString("0", CultureInfo.InvariantCulture);
    }

    public static void NeutralDrop() {
        Player player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
        if (!CanThrowHeldObject(player)) {
            Engine.Scene?.Add(new AkronToast("Neutral Drop needs a throwable held item."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InputAssistShortcut)) {
            return;
        }

        Input.MoveY.Value = 1;
        player.Throw();
        Engine.Scene?.Add(new AkronToast("Neutral Drop."));
    }

    public static void Backboost() {
        Player player = (Engine.Scene as Level)?.Tracker.GetEntity<Player>();
        if (!CanThrowHeldObject(player)) {
            Engine.Scene?.Add(new AkronToast("Backboost needs a throwable held item."));
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.InputAssistShortcut)) {
            return;
        }

        if (player.Facing == Facings.Left) {
            player.Facing = Facings.Right;
        } else if (player.Facing == Facings.Right) {
            player.Facing = Facings.Left;
        }

        player.Throw();
        Engine.Scene?.Add(new AkronToast("Backboost."));
    }

    private static bool CanThrowHeldObject(Player player) {
        return player?.Holding != null && player.minHoldTimer <= 0f;
    }

    public static void DeloadSpinners(Level level, float secondsBeforeDeload) {
        if (level == null) {
            Engine.Scene?.Add(new AkronToast("Open a room before simulating deload."));
            return;
        }

        float delay = AkronDeloadSimulator.ClampDelaySeconds(secondsBeforeDeload);
        if (!AkronModule.TryUse(AkronFeatureKind.DeloadSimulation)) {
            Engine.Scene?.Add(new AkronToast("Deload simulation is blocked by Akron policy."));
            return;
        }

        int steps = AkronDeloadSimulator.Simulate(level, delay);
        Engine.Scene?.Add(new AkronToast(steps > 0
            ? "Deload simulated: " + steps + " frames."
            : "Deload already simulated."));
    }

    public static void ToggleEditableFlag(Level level) {
        if (!AkronModule.TryUse(AkronFeatureKind.FlagInspector)) {
            return;
        }

        string flag = AkronModule.Settings.EditableFlagName;
        if (string.IsNullOrWhiteSpace(flag)) {
            Engine.Scene?.Add(new AkronToast("Editable flag name is empty."));
            return;
        }

        bool enabled = level.Session.GetFlag(flag);
        level.Session.SetFlag(flag, !enabled);
        Engine.Scene?.Add(new AkronToast((!enabled ? "Enabled " : "Disabled ") + flag + "."));
    }

    public static void ToggleSelectedVisibleFlag(Level level) {
        if (!AkronModule.TryUse(AkronFeatureKind.FlagInspector)) {
            return;
        }

        List<string> flags = AkronSessionFlagView.GetEditableFlags(level, 12).ToList();
        if (flags.Count == 0) {
            Engine.Scene?.Add(new AkronToast("No editable flags available."));
            return;
        }

        AkronModule.Session.EditableFlagIndex = Calc.Clamp(AkronModule.Session.EditableFlagIndex, 0, flags.Count - 1);
        string flag = flags[AkronModule.Session.EditableFlagIndex];
        bool enabled = level.Session.GetFlag(flag);
        level.Session.SetFlag(flag, !enabled);
        Engine.Scene?.Add(new AkronToast((!enabled ? "Enabled " : "Disabled ") + flag + "."));
    }

    public static void CycleSelectedFlag(Level level, int delta) {
        List<string> flags = AkronSessionFlagView.GetEditableFlags(level, 12).ToList();
        if (flags.Count == 0) {
            return;
        }

        AkronModule.Session.EditableFlagIndex = (AkronModule.Session.EditableFlagIndex + delta + flags.Count) % flags.Count;
        Engine.Scene?.Add(new AkronToast("Selected flag: " + flags[AkronModule.Session.EditableFlagIndex]));
    }

    public static void StartInternalRecording(Level level) {
        AkronInternalRecorder.Start(level);
    }

    public static void StopInternalRecording() {
        AkronInternalRecorder.Stop();
    }

    public static void StartReplayBuffer(Scene scene) {
        AkronInternalRecorder.StartReplayBuffer(scene);
    }

    public static void StopReplayBuffer() {
        AkronInternalRecorder.StopReplayBuffer();
    }

    public static void DisarmReplayBufferAutoStart() {
        AkronInternalRecorder.DisarmReplayBufferAutoStart();
    }

    public static void SaveReplayBuffer(Scene scene) {
        AkronInternalRecorder.SaveReplayBuffer(scene);
    }

    public static void ArmCompletionCapture(Scene scene) {
        AkronInternalRecorder.ArmCompletionCapture(scene);
    }

    public static void FlagCompletion(Scene scene) {
        AkronInternalRecorder.FlagCompletion(scene);
    }

    public static void BuildCompletionVideo(Scene scene) {
        AkronInternalRecorder.BuildCompletionVideo(scene);
    }

    public static string DescribeRecordingOutputFolder() {
        if (string.IsNullOrWhiteSpace(AkronModule.Settings.RecordingOutputFolder)) {
            return "Saves/AkronRecordings";
        }

        return AkronModule.Settings.FormatPathForDisplay(AkronModule.Settings.RecordingOutputFolder);
    }

    public static void LaunchTas() {
        if (!AkronModule.TryUse(AkronFeatureKind.TasHandoff)) {
            return;
        }

        if (!AkronInterop.CelesteTasLoaded) {
            Engine.Scene?.Add(new AkronToast("CelesteTAS is not loaded."));
            return;
        }

        if (string.IsNullOrWhiteSpace(AkronModule.Settings.TasFilePath) || !File.Exists(AkronModule.Settings.TasFilePath)) {
            Engine.Scene?.Add(new AkronToast("Configured TAS file was not found."));
            return;
        }

        string commandText = "playtas " + AkronModule.Settings.TasFilePath;
        Engine.Commands.commandHistory.Insert(0, commandText);
        Engine.Commands.ExecuteCommand("playtas", new[] { AkronModule.Settings.TasFilePath });
        Engine.Commands.commandHistory.RemoveAt(0);
        AkronInterop.TryShowStudioPopup("akron-playtas", "Akron", "Started TAS handoff for " + Path.GetFileName(AkronModule.Settings.TasFilePath));
        Engine.Scene?.Add(new AkronToast("Started TAS handoff."));
    }

    public static void ExportRoomTimes() {
        if (!AkronModule.TryUse(AkronFeatureKind.SplitHelper)) {
            return;
        }

        if (!AkronInterop.SpeedrunToolLoaded) {
            Engine.Scene?.Add(new AkronToast("Speedrun Tool is not loaded."));
            return;
        }

        string commandText = "srt_exportroomtimes";
        Engine.Commands.commandHistory.Insert(0, commandText);
        Engine.Commands.ExecuteCommand("srt_exportroomtimes", System.Array.Empty<string>());
        Engine.Commands.commandHistory.RemoveAt(0);
        Engine.Scene?.Add(new AkronToast("Exported room times via Speedrun Tool."));
    }

    public static void WriteProofSidecar(Level level, string eventName = "manual-proof-export") {
        if (level == null) {
            return;
        }

        string path = AkronProof.WriteSidecar(level, eventName);
        AkronProof.ShowProofPanel(level, eventName, path);
        Engine.Scene?.Add(new AkronToast("Proof sidecar: " + Path.GetFileName(path)));
    }

    public static string DescribeGoldenStartHelper(Level level) {
        return IsGoldenStartHelperSafe(level) ? "First room" : "Unsafe here";
    }

    public static bool IsGoldenStartHelperSafe(Level level) {
        if (level?.Session?.MapData?.Levels == null) {
            return false;
        }

        LevelData firstRoom = level.Session.MapData.Levels.FirstOrDefault(room => !room.Dummy);
        return firstRoom != null &&
               string.Equals(level.Session.Level, firstRoom.Name, StringComparison.OrdinalIgnoreCase) &&
               level.Entities.OfType<Strawberry>().All(strawberry => !strawberry.Golden || strawberry.Follower.Leader == null);
    }

    public static void GiveGoldenFromStart(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.GoldenStartHelper)) {
            return;
        }

        if (!IsGoldenStartHelperSafe(level)) {
            Engine.Scene?.Add(new AkronToast("Golden helper is only allowed from the first room start."));
            return;
        }

        try {
            Engine.Commands.ExecuteCommand("give_golden", Array.Empty<string>());
            AkronModule.Session.UsedGoldenStartHelper = true;
            string path = AkronProof.WriteSidecar(level, "golden-start-helper");
            Engine.Scene?.Add(new AkronToast("Golden helper used: " + Path.GetFileName(path)));
        } catch (Exception ex) {
            Engine.Scene?.Add(new AkronToast("Golden helper failed: " + ex.Message));
        }
    }

    public static void WriteJournalSnapshotCompare(Level level) {
        if (!AkronModule.TryUse(AkronFeatureKind.JournalSnapshotCompare) || SaveData.Instance == null) {
            return;
        }

        string directory = Path.Combine(Everest.PathGame, "Saves", "AkronProof");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "akron-journal-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
        string previousPath = FindLatestJournalSnapshot(directory);
        string summary = BuildJournalSnapshotSummary(level);
        File.WriteAllText(path, summary);

        string compare = string.IsNullOrWhiteSpace(previousPath)
            ? "First snapshot"
            : "Compared with " + Path.GetFileName(previousPath);
        AkronModule.Session.UsedJournalSnapshotCompare = true;
        AkronModule.Session.LastJournalSnapshotPath = path;
        AkronModule.Session.LastJournalCompareSummary = compare;
        Engine.Scene?.Add(new AkronToast("Journal snapshot: " + compare));
    }

    private static string FindLatestJournalSnapshot(string directory) {
        return Directory.EnumerateFiles(directory, "akron-journal-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string BuildJournalSnapshotSummary(Level level) {
        var snapshot = new {
            timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            mapSid = level?.Session?.Area.GetSID() ?? "unknown",
            room = level?.Session?.Level ?? "unknown",
            fileSlot = SaveData.Instance?.FileSlot ?? -1,
            totalDeaths = SaveData.Instance?.TotalDeaths ?? 0,
            fileTime = SaveData.Instance?.Time ?? 0L,
            unlockedAreas = SaveData.Instance?.UnlockedAreas ?? 0,
            maxArea = SaveData.Instance?.MaxArea ?? 0,
            cheatMode = SaveData.Instance?.CheatMode ?? false
        };

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string WriteDebugSnapshot(Level level, string tag = "manual") {
        if (level == null) {
            return string.Empty;
        }

        string path = AkronDebugSnapshot.Write(level, tag);
        AkronLog.Normal(nameof(AkronDebugSnapshot), "debug snapshot written: " + path);
        Engine.Scene?.Add(new AkronToast("Debug snapshot: " + Path.GetFileName(path)));
        return path;
    }

    public static List<string> GetAvailableRooms(Level level) {
        if (level?.Session?.MapData == null) {
            return new List<string>();
        }

        return level.Session.MapData.Levels
            .Where(levelData => !levelData.Dummy)
            .Select(levelData => levelData.Name)
            .Distinct()
            .ToList();
    }

    public static void CycleSelectedRoom(Level level, int delta) {
        List<string> rooms = GetAvailableRooms(level);
        if (rooms.Count == 0) {
            return;
        }

        AkronModule.Session.SelectedRoomIndex = (AkronModule.Session.SelectedRoomIndex + delta + rooms.Count) % rooms.Count;
        Engine.Scene?.Add(new AkronToast("Selected room: " + rooms[AkronModule.Session.SelectedRoomIndex]));
    }

    public static string DescribeSelectedRoom(Level level) {
        List<string> rooms = GetAvailableRooms(level);
        if (rooms.Count == 0) {
            return "No rooms";
        }

        AkronModule.Session.SelectedRoomIndex = Calc.Clamp(AkronModule.Session.SelectedRoomIndex, 0, rooms.Count - 1);
        return rooms[AkronModule.Session.SelectedRoomIndex];
    }

    public static void WarpSelectedRoom(Level level) {
        if (!AkronModule.TryUse(AkronFeatureKind.RoomWarp)) {
            return;
        }

        List<string> rooms = GetAvailableRooms(level);
        if (rooms.Count == 0) {
            return;
        }

        AkronModule.Session.SelectedRoomIndex = Calc.Clamp(AkronModule.Session.SelectedRoomIndex, 0, rooms.Count - 1);
        WarpToRoom(level, rooms[AkronModule.Session.SelectedRoomIndex]);
    }

    public static void WarpRelativeRoom(Level level, int delta) {
        if (!AkronModule.TryUse(AkronFeatureKind.RoomWarp)) {
            return;
        }

        List<string> rooms = GetAvailableRooms(level);
        if (rooms.Count == 0) {
            return;
        }

        int currentIndex = rooms.FindIndex(room => room == level.Session.Level);
        if (currentIndex < 0) {
            currentIndex = 0;
        }

        int nextIndex = (currentIndex + delta + rooms.Count) % rooms.Count;
        AkronModule.Session.SelectedRoomIndex = nextIndex;
        WarpToRoom(level, rooms[nextIndex]);
    }

    public static string DescribeRelativeCampaignMap(Level level, int delta) {
        List<AreaKey> areas = GetCampaignAreaOrder(level);
        int currentIndex = FindCampaignAreaIndex(areas, level?.Session?.Area ?? AreaKey.None);
        if (areas.Count == 0 || currentIndex < 0) {
            return "No campaign";
        }

        AreaKey target = areas[(currentIndex + delta + areas.Count) % areas.Count];
        AreaData data = AreaData.Get(target);
        return FormatAreaLabel(data, target);
    }

    public static void WarpRelativeCampaignMap(Level level, int delta) {
        if (!AkronModule.TryUse(AkronFeatureKind.RoomWarp)) {
            return;
        }

        List<AreaKey> areas = GetCampaignAreaOrder(level);
        int currentIndex = FindCampaignAreaIndex(areas, level?.Session?.Area ?? AreaKey.None);
        if (areas.Count == 0 || currentIndex < 0) {
            Engine.Scene?.Add(new AkronToast("No campaign map order available."));
            return;
        }

        AreaKey target = areas[(currentIndex + delta + areas.Count) % areas.Count];
        LoadArea(target, checkpoint: null);
    }

    public static string DescribeRelativeCheckpoint(Level level, int delta) {
        List<CheckpointData> checkpoints = GetCheckpointOrder(level);
        int currentIndex = FindCurrentCheckpointIndex(level, checkpoints);
        if (checkpoints.Count == 0 || currentIndex < 0) {
            return "No checkpoints";
        }

        return FormatCheckpointLabel(checkpoints[(currentIndex + delta + checkpoints.Count) % checkpoints.Count]);
    }

    public static void WarpRelativeCheckpoint(Level level, int delta) {
        if (!AkronModule.TryUse(AkronFeatureKind.RoomWarp)) {
            return;
        }

        List<CheckpointData> checkpoints = GetCheckpointOrder(level);
        int currentIndex = FindCurrentCheckpointIndex(level, checkpoints);
        if (checkpoints.Count == 0 || currentIndex < 0) {
            Engine.Scene?.Add(new AkronToast("No checkpoints available."));
            return;
        }

        CheckpointData target = checkpoints[(currentIndex + delta + checkpoints.Count) % checkpoints.Count];
        LoadArea(level.Session.Area, ResolveCheckpointLevelForLoad(level, target));
    }

    public static void SkipCutscene(Level level) {
        if (level == null) {
            return;
        }

        if (!level.InCutscene && !level.SkippingCutscene) {
            Engine.Scene?.Add(new AkronToast("No active cutscene to skip."));
            return;
        }

        level.SkippingCutscene = true;
        FieldInfo onCutsceneSkipField = typeof(Level).GetField("onCutsceneSkip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object onCutsceneSkipValue = onCutsceneSkipField?.GetValue(level);
        if (onCutsceneSkipValue is Action<Level> onCutsceneSkipWithLevel) {
            onCutsceneSkipWithLevel(level);
            onCutsceneSkipField.SetValue(level, null);
        } else if (onCutsceneSkipValue is Action onCutsceneSkip) {
            onCutsceneSkip();
            onCutsceneSkipField.SetValue(level, null);
        }
        Engine.Scene?.Add(new AkronToast("Cutscene skip requested."));
    }

    public static void OpenMountainViewer() {
        if (!AkronModule.TryUse(AkronFeatureKind.MountainViewer)) {
            return;
        }

        if (Engine.Scene == null) {
            AkronMountainViewer.Open();
            return;
        }

        Engine.Scene.OnEndOfFrame += AkronMountainViewer.Open;
    }

    public static void ToggleForceBroker(Level level) {
        AkronMapOverrides.ToggleForceBroker(level);
        Engine.Scene?.Add(new AkronToast("Force broker for this map: " + (AkronMapOverrides.ShouldForceBroker(level) ? "On" : "Off")));
    }

    public static void ToggleUnsafeNativeOverride(Level level) {
        if (level == null) {
            return;
        }

        if (AkronMapOverrides.ShouldAllowUnsafeSavestates(level)) {
            AkronMapOverrides.ToggleAllowUnsafe(level);
            Engine.Scene?.Add(new AkronToast("Unsafe StartPos restore disabled for this map."));
            return;
        }

        AkronPromptMenu.Show(
            level,
            "UNSAFE STARTPOS RESTORE",
            "This bypasses Akron's native StartPos restore risk block for the current map.\n" +
            "Use this only if you accept desync risk and Cheat escalation.",
            new AkronPromptOption("Keep Blocked", () => { }),
            new AkronPromptOption("Allow On This Map", () => {
                AkronMapOverrides.ToggleAllowUnsafe(level);
                Engine.Scene?.Add(new AkronToast("Unsafe StartPos restore enabled for this map."));
            })
        );
    }

    public static void ToggleEverestSafeBypass(Level level) {
        if (level == null) {
            return;
        }

        if (AkronMapOverrides.ShouldDisableEverestSafeBlock(level)) {
            AkronMapOverrides.ToggleEverestSafeBlock(level);
            Engine.Scene?.Add(new AkronToast("Everest-safe native block restored for this map."));
            return;
        }

        AkronPromptMenu.Show(
            level,
            "EVEREST-SAFE BYPASS",
            "This disables the conservative native StartPos restore block for this map while Everest-safe is active.\n" +
            "Proceed only if you trust the map's runtime behavior.",
            new AkronPromptOption("Keep Blocked", () => { }),
            new AkronPromptOption("Disable Block On This Map", () => {
                AkronMapOverrides.ToggleEverestSafeBlock(level);
                Engine.Scene?.Add(new AkronToast("Everest-safe native block disabled for this map."));
            })
        );
    }

    private static void WarpToRoom(Level level, string roomName) {
        LevelData levelData = level.Session.MapData.Levels.FirstOrDefault(candidate => candidate.Name == roomName);
        if (levelData == null) {
            Engine.Scene?.Add(new AkronToast("Room was not found in map data."));
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene != level) {
                return;
            }

            Vector2 probe = new Vector2(levelData.Bounds.Left, levelData.Bounds.Bottom);
            level.Session.Level = roomName;
            level.Session.RespawnPoint = level.Session.GetSpawnPoint(probe);
            level.StartPosition = null;
            level.Tracker.GetEntitiesCopy<Player>().ForEach(entity => entity.RemoveSelf());
            level.UnloadLevel();
            level.Completed = false;
            level.InCutscene = false;
            level.SkippingCutscene = false;
            level.LoadLevel(Player.IntroTypes.Respawn);
            level.Entities.UpdateLists();
            RelinkRuntimeRenderState(level);
            Engine.Scene?.Add(new AkronToast("Warped to " + roomName + "."));
        };
    }

    private static List<AreaKey> GetCampaignAreaOrder(Level level) {
        AreaKey current = level?.Session?.Area ?? AreaKey.None;
        AreaData currentData = AreaData.Get(current);
        if (currentData == null || AreaData.Areas == null) {
            return new List<AreaKey>();
        }

        string levelSet = currentData.LevelSet ?? current.LevelSet ?? string.Empty;
        List<AreaKey> areas = new List<AreaKey>();
        foreach (AreaData data in AreaData.Areas) {
            if (data == null ||
                data.Interlude ||
                string.IsNullOrWhiteSpace(data.SID) ||
                !string.Equals(data.LevelSet ?? string.Empty, levelSet, StringComparison.Ordinal)) {
                continue;
            }

            foreach (AreaMode mode in new[] { AreaMode.Normal, AreaMode.BSide, AreaMode.CSide }) {
                if (data.HasMode(mode)) {
                    areas.Add(data.ToKey(mode));
                }
            }
        }

        return areas;
    }

    private static int FindCampaignAreaIndex(List<AreaKey> areas, AreaKey current) {
        return areas.FindIndex(area => area.ID == current.ID && area.Mode == current.Mode);
    }

    private static List<CheckpointData> GetCheckpointOrder(Level level) {
        return BuildCheckpointOrder(AreaData.GetMode(level.Session.Area)?.Checkpoints, GetStartLevelName(level));
    }

    private static int FindCurrentCheckpointIndex(Level level, List<CheckpointData> checkpoints) {
        int roomIndex = checkpoints.FindIndex(checkpoint => string.Equals(checkpoint.Level, level.Session.Level, StringComparison.Ordinal));
        if (roomIndex >= 0) {
            return roomIndex;
        }

        int checkpointId = AreaData.GetCheckpointID(level.Session.Area, level.Session.Level);
        if (checkpointId >= 0) {
            CheckpointData[] modeCheckpoints = AreaData.GetMode(level.Session.Area)?.Checkpoints;
            string checkpointLevel = checkpointId < (modeCheckpoints?.Length ?? 0) ? modeCheckpoints[checkpointId]?.Level : string.Empty;
            int checkpointIndex = checkpoints.FindIndex(checkpoint => string.Equals(checkpoint.Level, checkpointLevel, StringComparison.Ordinal));
            if (checkpointIndex >= 0) {
                return checkpointIndex;
            }
        }

        return 0;
    }

    internal static List<CheckpointData> BuildCheckpointOrder(IEnumerable<CheckpointData> checkpoints, string startLevel) {
        List<CheckpointData> ordered = (checkpoints ?? Enumerable.Empty<CheckpointData>())
            .Where(checkpoint => checkpoint != null && !string.IsNullOrWhiteSpace(checkpoint.Level))
            .ToList();

        if (!string.IsNullOrWhiteSpace(startLevel) &&
            ordered.All(checkpoint => !string.Equals(checkpoint.Level, startLevel, StringComparison.Ordinal))) {
            ordered.Insert(0, CreateCheckpointData(startLevel, ImplicitStartCheckpointName));
        }

        return ordered;
    }

    private static CheckpointData CreateCheckpointData(string level, string name) {
        CheckpointData checkpoint = new CheckpointData(level, name) {
            Level = level,
            Name = name
        };
        return checkpoint;
    }

    internal static string ResolveCheckpointLevelForLoad(Level level, CheckpointData checkpoint) {
        if (checkpoint == null) {
            return null;
        }

        string startLevel = GetStartLevelName(level);
        return ResolveCheckpointLevelForLoad(checkpoint, startLevel, IsImplicitStartCheckpoint(level, checkpoint));
    }

    internal static string ResolveCheckpointLevelForLoad(CheckpointData checkpoint, string startLevel, bool implicitStartCheckpoint) {
        if (checkpoint == null) {
            return null;
        }

        return implicitStartCheckpoint &&
               string.Equals(checkpoint.Level, startLevel, StringComparison.Ordinal)
            ? null
            : checkpoint.Level;
    }

    private static bool IsImplicitStartCheckpoint(Level level, CheckpointData checkpoint) {
        if (checkpoint == null || !string.Equals(checkpoint.Name, ImplicitStartCheckpointName, StringComparison.Ordinal)) {
            return false;
        }

        CheckpointData[] checkpoints = AreaData.GetMode(level.Session.Area)?.Checkpoints;
        return checkpoints == null ||
               checkpoints.All(candidate => !string.Equals(candidate?.Level, checkpoint.Level, StringComparison.Ordinal));
    }

    private static string GetStartLevelName(Level level) {
        return level?.Session?.MapData?.Levels?
            .FirstOrDefault(room => room != null && !room.Dummy)
            ?.Name ?? string.Empty;
    }

    private static string FormatAreaLabel(AreaData data, AreaKey area) {
        string name = data == null || string.IsNullOrWhiteSpace(data.Name) ? area.GetSID() : data.Name;
        return name + " " + FormatAreaMode(area.Mode);
    }

    private static string FormatAreaMode(AreaMode mode) {
        return mode switch {
            AreaMode.BSide => "B-Side",
            AreaMode.CSide => "C-Side",
            _ => "A-Side"
        };
    }

    private static string FormatCheckpointLabel(CheckpointData checkpoint) {
        if (!string.IsNullOrWhiteSpace(checkpoint.Name)) {
            return Dialog.Clean(checkpoint.Name);
        }

        return checkpoint.Level;
    }

    private static void LoadArea(AreaKey area, string checkpoint) {
        if (SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("No save data available."));
            return;
        }

        AreaStats stats = area.ID >= 0 && area.ID < SaveData.Instance.Areas_Safe.Count
            ? SaveData.Instance.Areas_Safe[area.ID]
            : null;
        Engine.Scene?.Add(new AkronToast("Loading " + FormatAreaLabel(AreaData.Get(area), area) + "."));
        Session session = new Session(area, checkpoint, stats);
        SaveData.Instance.StartSession(session);
        Engine.Scene = new LevelLoader(session);
    }

}
