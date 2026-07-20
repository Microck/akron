using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static List<OverlayEntry> BuildGlobalEntries(bool motionSmoothingLoaded) {
        List<OverlayEntry> entries = new List<OverlayEntry>();
        if (motionSmoothingLoaded) {
            entries.Add(InlineNumericToggle("FPS Bypass", AkronFeatureKind.FpsBypass, () => AkronModule.Settings.FpsBypass, value => AkronModule.Settings.FpsBypass = value, () => AkronModule.Settings.FpsBypassTarget, value => AkronModule.Settings.FpsBypassTarget = AkronModuleSettings.ClampFpsTarget((int) Math.Round(value)), 60, 480, "%.0f", "FPS", true));
            entries.Add(InlineNumericToggle("TPS Bypass", AkronFeatureKind.TpsBypass, () => AkronModule.Settings.TpsBypass, value => AkronModule.Settings.TpsBypass = value, () => AkronModule.Settings.TpsBypassTarget, value => AkronModule.Settings.TpsBypassTarget = AkronModuleSettings.ClampTpsTarget((int) Math.Round(value)), 30, 480, "%.0f", "TPS", true, "physics bypass"));
        }

        entries.Add(NumericRow("Timescale", () => AkronModule.Session?.TimescaleMultiplier ?? 1f, SetTimescaleMultiplier, 0.1f, 2f, "%.1f", "x", false, "speed", "slowmo"));
        entries.Add(Toggle("Safe Mode", () => AkronModule.Settings.SafeMode, value => AkronModule.Settings.SafeMode = value, "safe", "freeze attempts", "indicator"));
        entries.Add(PolicyToggle("Submission Mode", AkronFeatureKind.SubmissionMode, () => AkronModule.Settings.SubmissionMode, value => {
            AkronModule.Settings.SubmissionMode = value;
            if (value) {
                AkronModule.Settings.ProofModeOverlay = true;
                AkronModule.Settings.ProofRecorderGuard = true;
                AkronModule.Settings.EndScreenHelper = true;
                AkronModule.Settings.PauseTracker = true;
                AkronModule.Settings.MapVersionStamp = true;
            }
        }, "proof", "submission", "goldberry", "hardlist"));
        entries.Add(PolicyToggle("Freeze Attempts", AkronFeatureKind.SafeModeStats, () => AkronModule.Settings.SafeModeFreezeAttempts, value => AkronModule.Settings.SafeModeFreezeAttempts = value));
        entries.Add(Toggle("Pause Buffering", () => AkronModule.Settings.AllowPauseBuffering, value => AkronModule.Settings.AllowPauseBuffering = value));
        entries.Add(PolicyToggle("Control Display", AkronFeatureKind.ShowTaps, () => AkronModule.Settings.ShowTaps, value => AkronModule.Settings.ShowTaps = value));
        entries.Add(Toggle("Autosave", () => AkronModule.Settings.Autosave, value => AkronModule.Settings.Autosave = value, "save", "room load", "respawn"));
        entries.Add(NumericToggle("Transition Speed", AkronFeatureKind.TransitionSpeed, () => AkronModule.Settings.TransitionSpeedMultiplier != 1f, value => AkronModule.Settings.TransitionSpeedMultiplier = value ? 0.5f : 1f, () => AkronModule.Settings.TransitionSpeedMultiplier, value => AkronModule.Settings.TransitionSpeedMultiplier = AkronModuleSettings.ClampTransitionSpeedMultiplier(value), 0.1f, 3f, "%.2f", "x", false));
        return entries;
    }

    private static List<OverlayEntry> BuildEntriesForTab(string tab, Level level) {
        switch (tab) {
            case "Global":
                return BuildGlobalEntries(AkronInterop.MotionSmoothingLoaded);
            case "Level":
                return new List<OverlayEntry> {
                    PolicyToggle("Auto Kill", AkronFeatureKind.AutoKill, () => AkronModule.Settings.AutoKill, value => {
                        AkronModule.Settings.AutoKill = value;
                        if (value && !AkronModule.Settings.AutoKillTimer && !AkronModule.Settings.AutoKillArea) {
                            AkronModule.Settings.AutoKillTimer = true;
                        }
                    }),
                    PolicyToggle("Auto Deafen", AkronFeatureKind.AutoDeafen, () => AkronModule.Settings.AutoDeafen, value => {
                        AkronModule.Settings.AutoDeafen = value;
                        if (!value) {
                            AkronActions.RestoreAutoDeafen();
                        }
                    }),
                    Toggle("Confirm Restart", () => AkronModule.Settings.ConfirmRestart, value => AkronModule.Settings.ConfirmRestart = value),
                    Toggle("Confirm Full Reset", () => AkronModule.Settings.ConfirmFullReset, value => AkronModule.Settings.ConfirmFullReset = value),
                    Action("Freeze Gameplay", AkronFeatureKind.Freeze, () => AkronModule.Session != null, () => AkronModule.Session == null ? "Unavailable" : AkronModule.Session.FreezeGameplay ? "On" : "Off", AkronActions.ToggleFreeze, () => AkronModule.Session?.FreezeGameplay == true),
                    Action("Core Mode", () => level != null, () => AkronActions.DescribeCoreMode(level), () => AkronActions.ToggleCoreMode(level), () => AkronModule.Settings.CoreModeOverrideEnabled, "core", "hot", "cold", "cycle"),
                    NumericToggle("Respawn Time", AkronFeatureKind.RespawnTime, () => AkronModule.Settings.RespawnTimeModifier, value => AkronModule.Settings.RespawnTimeModifier = value, () => AkronModule.Settings.RespawnTimeSeconds, value => AkronModule.Settings.RespawnTimeSeconds = AkronModuleSettings.ClampRespawnTimeSeconds(value), 0.1f, 10f, "%.2f", "s", false),
                    NumericToggle("Pause Timer", AkronFeatureKind.PauseCountdown, () => AkronModule.Settings.PauseCountdown, value => AkronModule.Settings.PauseCountdown = value, () => AkronModule.Settings.PauseCountdownSeconds, value => AkronModule.Settings.PauseCountdownSeconds = AkronModuleSettings.ClampPauseCountdownSeconds(value), 0.1f, 15f, "%.2f", "s", false),
                    PolicyToggle("Pause Tracker", AkronFeatureKind.PauseTracker, () => AkronModule.Settings.PauseTracker, value => AkronModule.Settings.PauseTracker = value, "pause count", "proof", "pause abuse"),
                    PolicyToggle("Lag Pauser", AkronFeatureKind.LagPauser, () => AkronModule.Settings.LagPauser, value => AkronModule.Settings.LagPauser = value, "lag", "threshold", "proof", "pause"),
                    PolicyToggle("Freeze Timer While Paused", AkronFeatureKind.PauseTimerFreeze, () => AkronModule.Settings.FreezeTimerWhilePaused, value => AkronModule.Settings.FreezeTimerWhilePaused = value),
                    PolicyToggle("Hide Pause Menu", AkronFeatureKind.PauseMenuVisibility, () => AkronModule.Settings.HidePauseMenu, value => AkronModule.Settings.HidePauseMenu = value),
                    PolicyToggle("Light Level", AkronFeatureKind.VisualTuning, () => AkronModule.Settings.LightLevel, value => AkronModule.Settings.LightLevel = value),
                    PolicyToggle("Bloom Level", AkronFeatureKind.VisualTuning, () => AkronModule.Settings.BloomLevel, value => AkronModule.Settings.BloomLevel = value),
                    PolicyToggle("Screen Tint", AkronFeatureKind.VisualTuning, () => AkronModule.Settings.ScreenTint, value => AkronModule.Settings.ScreenTint = value),
                    PolicyToggle("Screenshake", AkronFeatureKind.Screenshake, () => AkronModule.Settings.Screenshake, value => AkronModule.Settings.Screenshake = value),
                    PolicyToggle("Refill Clarity", AkronFeatureKind.RefillClarity, () => AkronModule.Settings.RefillClarity, value => AkronModule.Settings.RefillClarity = value),
                    new OverlayEntry(
                        "Deload Spinners",
                        () => level != null && !AkronDeloadSimulator.IsUsed(level),
                        () => AkronDeloadSimulator.Describe(level),
                        () => AkronActions.DeloadSpinners(level, AkronModule.Settings.DeloadSpinnerDelaySeconds),
                        BuildSearchTerms("Deload Spinners", new[] { "deload", "spinner", "precision", "simulation" }),
                        false,
                        OverlayEntryControl.Action,
                        AkronFeatureKind.DeloadSimulation,
                        forceOptionsPopup: true),
                    PolicyToggle("Skip Intro", AkronFeatureKind.LevelEnterSkip, () => AkronModule.Settings.SkipIntro, value => AkronModule.Settings.SkipIntro = value),
                    PolicyToggle("Skip Postcards", AkronFeatureKind.LevelEnterSkip, () => AkronModule.Settings.SkipPostcards, value => AkronModule.Settings.SkipPostcards = value),
                    Toggle("Reduced Visual Noise", () => AkronModule.Settings.ReducedVisualNoise, value => AkronModule.Settings.ReducedVisualNoise = value),
                    Toggle("No Particles", () => AkronModule.Settings.NoParticles, value => AkronModule.Settings.SetNoParticles(value)),
                    Toggle("No Glitch", () => AkronModule.Settings.NoGlitch, value => AkronModule.Settings.SetNoGlitch(value)),
                    Toggle("No Anxiety", () => AkronModule.Settings.NoAnxiety, value => AkronModule.Settings.SetNoAnxiety(value)),
                    Toggle("No Distortion", () => AkronModule.Settings.NoDistortion, value => AkronModule.Settings.SetNoDistortion(value)),
                    Toggle("Hide Snow", () => AkronModule.Settings.HideSnow, value => AkronModule.Settings.HideSnow = value),
                    Toggle("Hide Wind Snow", () => AkronModule.Settings.HideWindSnow, value => AkronModule.Settings.HideWindSnow = value),
                    Toggle("Hide Waterfalls", () => AkronModule.Settings.HideWaterfalls, value => AkronModule.Settings.HideWaterfalls = value),
                    Toggle("Hide Tentacles", () => AkronModule.Settings.HideTentacles, value => AkronModule.Settings.HideTentacles = value),
                    Toggle("Hide Heat Distortion", () => AkronModule.Settings.HideHeatDistortion, value => AkronModule.Settings.HideHeatDistortion = value),
                    PolicyToggle("No Death Wipe", AkronFeatureKind.DeathVisuals, () => AkronModule.Settings.NoDeathWipe, value => AkronModule.Settings.NoDeathWipe = value),
                    PolicyToggle("No Freeze Frames", AkronFeatureKind.FreezeFrames, () => AkronModule.Settings.NoFreezeFrames, value => AkronModule.Settings.NoFreezeFrames = value),
                    PolicyToggle("Show Hitboxes", AkronFeatureKind.HitboxViewer, () => AkronModule.Settings.HitboxViewer, value => AkronModule.Settings.HitboxViewer = value),
                    Toggle("Fix Hitbox Pixels", () => AkronModule.Settings.FixHitboxPixels, value => AkronModule.Settings.FixHitboxPixels = value),
                    PolicyToggle("Show Hitbox Trail", AkronFeatureKind.HitboxViewer, () => AkronModule.Settings.HitboxViewer && AkronModule.Settings.ShowHitboxTrail, value => {
                        AkronModule.Settings.ShowHitboxTrail = value;
                        if (value) {
                            AkronModule.Settings.HitboxViewer = true;
                        }
                    }),
                    Toggle("Show Hitboxes On Death", () => AkronModule.Settings.HitboxShowLastDeath, value => AkronModule.Settings.HitboxShowLastDeath = value),
                    PolicyToggle("Show Triggers", AkronFeatureKind.TriggerViewer, () => AkronModule.Settings.ShowTriggers, value => AkronModule.Settings.ShowTriggers = value)
                };
            case "StartPos":
                return new List<OverlayEntry> {
                    StartPosRow(level),
                    PlaceStartPosRow(),
                    Toggle("Smart StartPos", () => AkronModule.Settings.SmartStartPos, value => AkronModule.Settings.SmartStartPos = value, "smart", "nearest", "respawn"),
                    StartPosSwitcherRow(level),
                    SelectorDropdown("StartPos Slot", () => true, () => "Slot " + AkronModule.Settings.ActiveStartPosSlot, () => AkronActions.ShiftStartPosSlot(1), BuildStartPosSlotChoices, "slot", "selected", "dropdown"),
                    Toggle("Respawn at StartPos", () => AkronModule.Settings.RespawnAtStartPos, value => AkronModule.Settings.RespawnAtStartPos = value, "respawn", "death", "practice")
                };
            case "Backups":
                return new List<OverlayEntry> {
                    Toggle("Enabled", AkronFeatureKind.Backups, () => AkronModule.Settings.BackupsEnabled, value => AkronModule.Settings.BackupsEnabled = value, "backup", "save", "zip"),
                    Action("Create Now", AkronFeatureKind.Backups, () => AkronModule.Settings.BackupsEnabled, () => AkronModule.Settings.BackupsEnabled ? "Now" : "Disabled", () => AkronBackupActions.CreateBackup("manual"), "manual", "zip", "save"),
                    Action("Restore", AkronFeatureKind.Backups, () => AkronBackupActions.ListBackups().Count > 0, AkronBackupActions.DescribeBackupSummary, () => ApplyOptionsPopupDelta("Restore", 1), "restore", "browser", "save"),
                    Action("Last Result", AkronFeatureKind.Backups, () => true, () => AkronBackupActions.DescribeLastBackup(), () => ApplyOptionsPopupDelta("Last Result", 1), "status", "last backup", "errors"),
                    Action("Triggers", AkronFeatureKind.Backups, () => true, DescribeBackupTriggers, () => ApplyOptionsPopupDelta("Triggers", 1), "startup", "shutdown", "save", "chapter", "interval"),
                    Action("Retention", AkronFeatureKind.Backups, () => true, DescribeBackupRetention, () => ApplyOptionsPopupDelta("Retention", 1), "delete", "age", "count", "size", "keep")
                };
            case "Bypass":
                return new List<OverlayEntry> {
                    Action("Instant Complete", () => level != null, () => level != null ? "Cheat" : "No level", () => { if (level != null) AkronActions.InstantComplete(level); }, "complete", "finish"),
                    Action("Uncomplete Level", () => level != null && SaveData.Instance != null, () => level != null ? "Cheat" : "No level", () => { if (level != null) AkronActions.UncompleteCurrentLevel(level); }),
                    Action("Unlock A-Sides", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockASides(), "unlock", "levels"),
                    Action("Unlock B-Sides", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockBSides(), "unlock", "cassettes"),
                    Action("Unlock C-Sides", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockCSides(), "unlock", "levels"),
                    Action("Unlock All Levels", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockAllLevels(), "unlock", "levels"),
                    Action("Unlock Golden Berries", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockGoldenBerries(), "unlock", "golden", "berries"),
                    Action("Unlock Paths", () => SaveData.Instance != null, AkronActions.DescribeUnlockState, () => AkronActions.UnlockPaths(), "unlock", "paths", "gates"),
                    Action("Obtain Room Berries", () => level != null && SaveData.Instance != null, AkronActions.DescribeBerryObtainOptions, () => { if (level != null) AkronActions.ObtainRoomBerries(level); }, "obtain", "berries", "room"),
                    Action("Obtain Chapter Berries", () => level != null && SaveData.Instance != null, AkronActions.DescribeBerryObtainOptions, () => { if (level != null) AkronActions.ObtainChapterBerries(level); }, "obtain", "berries", "chapter"),
                    Action("Berry Obtain Options", () => true, AkronActions.DescribeBerryObtainOptions, () => ApplyOptionsPopupDelta("Berry Obtain Options", 1), "berries", "include", "golden", "moon")
                };
            case "Player":
                List<OverlayEntry> player = new List<OverlayEntry> {
                    PolicyToggle("Air Jumps", AkronFeatureKind.MovementStatMutation, () => AkronModule.Settings.JumpHack, value => AkronModule.Settings.JumpHack = value),
                    Toggle("Always Show Trail", () => AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Always, value => {
                        AkronModule.Settings.TrailVisibility = value ? AkronTrailVisibility.Always : AkronTrailVisibility.Vanilla;
                        AkronModule.Settings.SetNoTrails(false);
                    }),
                    PolicyToggle("Click Teleport", AkronFeatureKind.ClickTeleport, () => AkronModule.Settings.ClickTeleport, value => AkronModule.Settings.ClickTeleport = value),
                    PolicyToggle("Custom Trail", AkronFeatureKind.CustomTrail, () => AkronModule.Settings.CustomTrail, value => AkronModule.Settings.CustomTrail = value),
                    Toggle("Dash Bar", () => AkronModule.Settings.DashBar, value => AkronModule.Settings.DashBar = value),
                    PolicyToggle("Dash Count", AkronFeatureKind.DashCountOverride, () => AkronModule.Settings.DashCountOverride, value => AkronModule.Settings.DashCountOverride = value),
                    Toggle("Dash Number", () => AkronModule.Settings.DashNumber, value => AkronModule.Settings.DashNumber = value),
                    NumericToggle("Fast Lookout", AkronFeatureKind.FastLookout, () => AkronModule.Settings.FastLookout, value => AkronModule.Settings.FastLookout = value, () => AkronModule.Settings.FastLookoutMultiplier, value => AkronModule.Settings.FastLookoutMultiplier = AkronModuleSettings.ClampFastLookoutMultiplier((int) Math.Round(value)), 1, 10, "%.0f", "x", true, "lookout", "watchtower"),
                    PolicyToggle("Frame Stepper", AkronFeatureKind.FrameAdvance, () => AkronModule.Settings.FrameStepper, value => AkronModule.Settings.FrameStepper = value),
                    Action("Golden Start", () => level != null, () => AkronActions.DescribeGoldenStartHelper(level), () => { if (level != null) AkronActions.GiveGoldenFromStart(level); }, "golden", "give_golden", "proof", "start"),
                    PolicyToggle("Golden Transparency", AkronFeatureKind.GoldenTransparency, () => AkronModule.Settings.GoldenTransparency, value => AkronModule.Settings.GoldenTransparency = value, "golden", "opacity", "berry"),
                    PolicyToggle("Grab Mode", AkronFeatureKind.GrabModeHotkey, () => AkronModule.Settings.GrabModeOverrideEnabled, SetGrabModeOverrideEnabled),
                    PolicyToggle("Ground Refills", AkronFeatureKind.GroundRefillRules, () => AkronModule.Settings.GroundRefillRules, value => AkronModule.Settings.GroundRefillRules = value),
                    PolicyToggle("Hazard Accuracy", AkronFeatureKind.HazardAccuracy, () => AkronModule.Settings.NoclipAccuracy, value => {
                        AkronModule.Settings.NoclipAccuracy = value;
                        if (!value) {
                            AkronModule.ResetNoclipAccuracy();
                        }
                    }),
                    PolicyToggle("Hide Player", AkronFeatureKind.HidePlayer, () => AkronModule.Settings.HidePlayer, value => AkronModule.Settings.HidePlayer = value),
                    PolicyToggle("Infinite Dash", AkronFeatureKind.InfiniteDash, () => AkronModule.Settings.InfiniteDash, value => AkronModule.Settings.InfiniteDash = value),
                    PolicyToggle("Infinite Stamina", AkronFeatureKind.InfiniteStamina, () => AkronModule.Settings.InfiniteStamina, value => AkronModule.Settings.InfiniteStamina = value),
                    InvincibilityToggle(),
                    PolicyToggle("Madeline Colors", AkronFeatureKind.CustomTrail, () => AkronModule.Settings.MadelineColors, value => AkronModule.Settings.MadelineColors = value),
                    PolicyToggle("Madeline Hair Length", AkronFeatureKind.MadelineHairLength, () => AkronModule.Settings.MadelineHairLength, value => AkronModule.Settings.MadelineHairLength = value),
                    PolicyToggle("Madeline Effect Sync", AkronFeatureKind.MadelineEffectSync, () => AkronModule.Settings.MadelineEffectSync, value => AkronModule.Settings.MadelineEffectSync = value),
                    PolicyToggle("Death Particles", AkronFeatureKind.DeathVisuals, () => AkronModule.Settings.CustomDeathParticles, value => AkronModule.Settings.CustomDeathParticles = value),
                    Action("Set Inventory", () => level != null && level.Tracker.GetEntity<Player>() != null, () => AkronActions.DescribeSetInventory(level), () => AkronActions.ToggleSetInventory(level), AkronActions.IsSetInventoryActive, "inventory", "dash count", "dashes", "space ruins"),
                    Action("Dream State", () => level != null && level.Tracker.GetEntity<Player>() != null, () => AkronActions.DescribeDreamState(level), () => AkronActions.ToggleDreamState(level), () => AkronActions.IsDreamStateActive(level), "dream dash", "dream blocks", "inventory"),
                    PolicyToggle("No Death Effect", AkronFeatureKind.DeathVisuals, () => AkronModule.Settings.NoDeathEffect, value => AkronModule.Settings.NoDeathEffect = value),
                    Toggle("No Ghost Trail", () => AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Hidden, value => {
                        AkronModule.Settings.TrailVisibility = value ? AkronTrailVisibility.Hidden : AkronTrailVisibility.Vanilla;
                        AkronModule.Settings.SetNoTrails(value);
                    }),
                    Toggle("No Stamina Flash", () => AkronModule.Settings.NoStaminaFlash, value => AkronModule.Settings.NoStaminaFlash = value),
                    Toggle("No Trails", () => AkronModule.Settings.NoTrails, value => AkronModule.Settings.SetNoTrails(value)),
                    PolicyToggle("No Respawn Animation", AkronFeatureKind.RespawnAnimation, () => AkronModule.Settings.NoRespawnAnimation, value => AkronModule.Settings.NoRespawnAnimation = value),
                    PolicyToggle("Noclip", AkronFeatureKind.Noclip, () => AkronModule.Settings.Noclip, value => AkronModule.Settings.Noclip = value),
                    PolicyToggle("Dash Redirect", AkronFeatureKind.InputAssistShortcut, () => AkronModule.Settings.DashRedirectEnabled, value => AkronModule.Settings.DashRedirectEnabled = value, "dash", "redirect", "input assist"),
                    PolicyToggle("Show Trajectory", AkronFeatureKind.ShowTrajectory, () => AkronModule.Settings.ShowTrajectory, value => AkronModule.Settings.ShowTrajectory = value),
                    PolicyToggle("Speed Number", AkronFeatureKind.SpeedNumber, () => AkronModule.Settings.SpeedNumber, value => AkronModule.Settings.SpeedNumber = value),
                    Toggle("Stamina Bar", () => AkronModule.Settings.StaminaBar, value => AkronModule.Settings.StaminaBar = value)
                };
                return player;
            case "Sound":
                return BuildCollapsedSoundEntries();
            case "Creator":
                return SortCreatorEntries(new List<OverlayEntry> {
                    PolicyToggle("Free Camera", AkronFeatureKind.FreeCamera, () => AkronModule.Settings.FreeCamera, value => AkronModule.Settings.FreeCamera = value),
                    PolicyToggle("Cursor Tools", AkronFeatureKind.CursorTools, () => AkronModule.Settings.CursorTools, value => AkronModule.Settings.CursorTools = value, "hold", "cursor", "tools", "click teleport", "free camera"),
                    PolicyToggle("Cursor Zoom", AkronFeatureKind.CursorZoom, () => AkronModule.Settings.CursorZoom, value => {
                        AkronModule.Settings.CursorZoom = value;
                        if (!value) {
                            AkronModule.ResetCursorZoom(level);
                        }
                    }, "mouse", "cursor", "scroll", "zoom", "magnifier"),
                    EntityInspectorRow(),
                    Action("Export Room Times", () => AkronInterop.SpeedrunToolLoaded, () => AkronInterop.RoomTimerAvailable ? "Speedrun Tool" : "Unavailable", AkronActions.ExportRoomTimes),
                    Action("Room Capture", () => level != null, AkronScreenshotScanner.Describe, () => { if (level != null) AkronScreenshotScanner.ScanRoom(level); }, "screenshot tool", "scan room", "export"),
                    Action("Map Capture", () => level != null, AkronScreenshotScanner.Describe, () => { if (level != null) AkronScreenshotScanner.ScanChapter(level); }, "screenshot tool", "scan map", "scan chapter", "export"),
                    Action("Previous Room", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeSelectedRoom(level), () => { if (level != null) AkronActions.CycleSelectedRoom(level, -1); }),
                    Action("Next Room", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeSelectedRoom(level), () => { if (level != null) AkronActions.CycleSelectedRoom(level, 1); }),
                    Action("Warp Selected Room", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeSelectedRoom(level), () => { if (level != null) AkronActions.WarpSelectedRoom(level); }),
                    Action("Previous Room In Order", () => level != null, () => "Cheat", () => { if (level != null) AkronActions.WarpRelativeRoom(level, -1); }, "warp previous room", "warp to previous in order"),
                    Action("Next Room In Order", () => level != null, () => "Cheat", () => { if (level != null) AkronActions.WarpRelativeRoom(level, 1); }, "warp next room", "warp to next in order"),
                    Action("Previous Map", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeRelativeCampaignMap(level, -1), () => { if (level != null) AkronActions.WarpRelativeCampaignMap(level, -1); }, "campaign", "chapter", "area", "previous map"),
                    Action("Next Map", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeRelativeCampaignMap(level, 1), () => { if (level != null) AkronActions.WarpRelativeCampaignMap(level, 1); }, "campaign", "chapter", "area", "next map"),
                    Action("Previous Checkpoint", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeRelativeCheckpoint(level, -1), () => { if (level != null) AkronActions.WarpRelativeCheckpoint(level, -1); }, "checkpoint", "previous checkpoint"),
                    Action("Next Checkpoint", () => level != null, () => level == null ? "Unavailable" : AkronActions.DescribeRelativeCheckpoint(level, 1), () => { if (level != null) AkronActions.WarpRelativeCheckpoint(level, 1); }, "checkpoint", "next checkpoint"),
                    Action("Open Debug Map", () => level != null, () => AkronModuleSettings.DescribeBinding(AkronModule.Settings.OpenDebugMap), () => { if (level != null) AkronModule.PerformOpenDebugMap(level); }),
                    PolicyToggle("Camera Offset", AkronFeatureKind.CameraOffset, () => AkronModule.Settings.CameraOffset, value => {
                        AkronModule.Settings.CameraOffset = value;
                        if (level != null) {
                            AkronActions.ApplyCameraOffset(level);
                        }
                    }, "camera", "offset"),
                    Action("Export Room Stats", () => level != null, () => string.IsNullOrWhiteSpace(AkronModule.Session?.LastRoomStatsExportPath) ? "TSV" : Path.GetFileName(AkronModule.Session.LastRoomStatsExportPath), () => { if (level != null) AkronPracticeStats.ExportRoomStats(level); }, "room", "stats", "export")
                });
            case "Speedrun Tool":
                return BuildSpeedrunToolEntries(level);
            case "CelesteTAS":
                return BuildCelesteTasEntries();
            case "Extended Variant Mode":
                return BuildExtendedVariantEntries();
            case "Extended Camera Dynamics":
                return BuildExtendedCameraDynamicsEntries();
            case "Labels":
                return BuildLabelEntries();
            case "Shortcuts":
                return new List<OverlayEntry> {
                    Action("Open Options", () => true, () => "Interface", AkronActions.OpenOptionsShortcut),
                    Action("Retry", () => level != null, () => level != null ? "Ready" : "Unavailable", () => { if (level != null) AkronModule.PerformRetry(level); }),
                    Action("Reload Room", () => level != null, () => AkronModuleSettings.DescribeBinding(AkronModule.Settings.ReloadRoom), () => { if (level != null) AkronModule.PerformReloadRoom(level); }),
                    Action("Reload Chapter", () => level != null, () => AkronModuleSettings.DescribeBinding(AkronModule.Settings.ReloadChapter), () => { if (level != null) AkronModule.PerformReloadChapter(level); }),
                    Action("Spawn Jelly", () => level != null && level.Tracker.GetEntity<Player>() != null, () => level != null ? "Ready" : "Unavailable", () => AkronActions.SpawnJelly(level), "jelly", "glider", "spawn"),
                    Action("Spawn Theo", () => level != null && level.Tracker.GetEntity<Player>() != null, () => level != null ? "Ready" : "Unavailable", () => AkronActions.SpawnTheo(level), "theo", "theo crystal", "spawn"),
                    Action("Neutral Drop", () => level != null, () => level != null ? "Assist" : "Unavailable", AkronActions.NeutralDrop, "neutral", "drop", "throw"),
                    Action("Backboost", () => level != null, () => level != null ? "Assist" : "Unavailable", AkronActions.Backboost, "throw", "backboost"),
                    Action("Skip Cutscene", () => level != null, () => level != null && (level.InCutscene || level.SkippingCutscene) ? "Ready" : "No cutscene", () => { if (level != null) AkronActions.SkipCutscene(level); })
                };
            case "Keybinds":
                return BuildKeybindOverviewEntries(level);
            case "Interface":
                return new List<OverlayEntry> {
                    SelectorDropdown("Theme", () => true, AkronOverlayThemes.CurrentDisplayName, () => ApplyOptionsPopupDelta("Overlay Appearance", 1), BuildThemeDropdownChoices, "overlay appearance", "style", "custom theme", ".akr"),
                    NumericRow("UI Scale", () => AkronModule.Settings.OverlayScale / 100f, value => AkronModule.Settings.OverlayScale = AkronModuleSettings.ClampOverlayScale((int) Math.Round(value * 100f)), 0.75f, 1.5f, "%.3f", string.Empty, false, "overlay appearance", "dpi"),
                    NumericRow("Opacity", () => AkronModule.Settings.OverlayOpacity, value => AkronModule.Settings.OverlayOpacity = AkronModuleSettings.ClampOverlayOpacity((int) Math.Round(value)), 55, 100, "%.0f", "%", true, "transparent lists"),
                    Toggle("Streamer Mode", () => AkronModule.Settings.StreamerMode, value => AkronModule.Settings.StreamerMode = value),
                    LoggingToggle(),
                    Toggle("Pause While Open", () => AkronModule.Settings.PauseGameplayInMenu, value => AkronModule.Settings.PauseGameplayInMenu = value),
                    Action("Export Setup", () => true, () => AkronSetupPacks.FormatSection(AkronModule.Settings.SetupPackSection), () => AkronSetupPacks.ExportCurrent(AkronModule.Settings.SetupPackExportName, AkronModule.Settings.SetupPackSection), "setup", ".akr", "export"),
                    Action("Import Setup", () => true, () => AkronSetupPacks.FormatSection(AkronModule.Settings.SetupPackSection), () => AkronSetupPacks.ImportFromFileBrowser(AkronModule.Settings.SetupPackSection), "setup", ".akr", "import"),
                    Action("Community Packs", () => true, DescribeCommunityPackBrowser, OpenCommunityPackBrowser, "discord", "community", "map", ".akr", "gamebanana"),
                    UploadPackRow(level),
                    Toggle("Search Autofocus", () => AkronModule.Settings.SearchAutofocus, value => AkronModule.Settings.SearchAutofocus = value),
                    SearchInput()
                };
            case "Internal Recorder":
                return new List<OverlayEntry> {
                    Action("Start Recording", () => level != null && !AkronInternalRecorder.IsRecording, AkronInternalRecorder.DescribeStatus, () => { if (level != null) AkronActions.StartInternalRecording(level); }, "record", "ffmpeg"),
                    Action("Stop Recording", () => AkronInternalRecorder.IsRecording, AkronInternalRecorder.DescribeWarnings, AkronActions.StopInternalRecording, "record", "ffmpeg"),
                    Action("Save Replay Buffer", () => Engine.Scene != null && AkronInternalRecorder.IsReplayBuffering, DescribeRecordingReplayBufferAction, () => { if (Engine.Scene != null) AkronActions.SaveReplayBuffer(Engine.Scene); }, "clip", "hotkey", "replay"),
                    Action("Arm Completion Clips", () => Engine.Scene != null, DescribeCompletionCapture, () => { if (Engine.Scene != null) AkronActions.ArmCompletionCapture(Engine.Scene); }, "completion", "clear", "replay", "long session"),
                    Action("Flag Completion", () => Engine.Scene != null && AkronInternalRecorder.IsReplayBuffering, () => AkronInternalRecorder.IsReplayBuffering ? "Flag" : "Off", () => { if (Engine.Scene != null) AkronActions.FlagCompletion(Engine.Scene); }, "completion", "flag", "clip", "clear"),
                    Action("Build Clear Video", () => Engine.Scene != null, DescribeCompletionVideoSource, () => { if (Engine.Scene != null) AkronActions.BuildCompletionVideo(Engine.Scene); }, "completion", "clear", "montage", "concat", "export"),
                    PolicyToggle("Proof Recorder Guard", AkronFeatureKind.ProofRecorderGuard, () => AkronModule.Settings.ProofRecorderGuard, value => AkronModule.Settings.ProofRecorderGuard = value, "proof", "recording", "guard"),
                    PolicyToggle("End Screen Helper", AkronFeatureKind.EndScreenHelper, () => AkronModule.Settings.EndScreenHelper, value => AkronModule.Settings.EndScreenHelper = value, "end screen", "clear", "proof"),
                    PolicyToggle("Map Version Stamp", AkronFeatureKind.MapVersionStamp, () => AkronModule.Settings.MapVersionStamp, value => AkronModule.Settings.MapVersionStamp = value, "map", "version", "proof"),
                    Action("Journal Snapshot / Compare", () => SaveData.Instance != null, () => string.IsNullOrWhiteSpace(AkronModule.Session?.LastJournalCompareSummary) ? "Snapshot" : AkronModule.Session.LastJournalCompareSummary, () => AkronActions.WriteJournalSnapshotCompare(level), "journal", "snapshot", "proof", "compare"),
                    NumericRow("Framerate", () => AkronModule.Settings.RecordingFramerate, value => AkronModule.Settings.RecordingFramerate = AkronModuleSettings.ClampRecordingFramerate((int) Math.Round(value)), 1, 360, "%.0f", "FPS", true, "fps"),
                    NumericRow("Endscreen Duration", () => AkronModule.Settings.RecordingEndscreenDurationSeconds, value => AkronModule.Settings.RecordingEndscreenDurationSeconds = AkronModuleSettings.ClampRecordingEndscreenDurationSeconds(value), 0, 30, "%.2f", "s", false, "clear", "post"),
                    NumericRow("Bitrate", () => AkronModule.Settings.RecordingBitrateMbps, value => AkronModule.Settings.RecordingBitrateMbps = AkronModuleSettings.ClampRecordingBitrateMbps((int) Math.Round(value)), 1, 1000, "%.0f", "mbps", true, "bitrate"),
                    NumericRow("Resolution X", () => AkronModule.Settings.RecordingResolutionX, value => AkronModule.Settings.RecordingResolutionX = AkronModuleSettings.ClampRecordingResolutionX((int) Math.Round(value)), 1, 15360, "%.0f", string.Empty, true, "width"),
                    NumericRow("Resolution Y", () => AkronModule.Settings.RecordingResolutionY, value => AkronModule.Settings.RecordingResolutionY = AkronModuleSettings.ClampRecordingResolutionY((int) Math.Round(value)), 1, 8640, "%.0f", string.Empty, true, "height"),
                    Toggle("Hide Preview", () => AkronModule.Settings.RecordingHidePreview, value => AkronModule.Settings.RecordingHidePreview = value, "preview"),
                    SelectorDropdown("Codec", () => true, () => AkronModuleSettings.FormatRecordingCodec(AkronModule.Settings.RecordingCodec), () => { }, BuildRecordingCodecChoices, "codec", "encoder", "quality", "rate control"),
                    Selector("Colorspace Args", () => true, () => string.IsNullOrWhiteSpace(AkronModule.Settings.RecordingColorspaceArgs) ? "Default" : AkronModule.Settings.RecordingColorspaceArgs, () => { }, "colorspace", "ffmpeg"),
                    Action("Replay Settings", () => true, DescribeRecordingReplaySettings, () => { }, "buffer", "clip", "hotkey", "replay", "save key"),
                    Action("Output", () => true, DescribeRecordingOutputSettings, () => { }, "folder", "path", "filename", "template", "container", "mkv", "mp4", "mov", "webm", "remux", "browser", "clips"),
                    Action("Audio", () => true, DescribeRecordingAudioTracks, () => { }, "audio", "game audio", "tracks", "full mix", "music", "sfx", "ambience", "muted", "level"),
                    Action("Clip Triggers", () => true, DescribeRecordingTriggers, () => { }, "clip", "event", "death", "berry", "golden", "checkpoint", "pre-roll", "post-roll"),
                    Action("Presets", () => true, () => AkronModuleSettings.FormatRecordingPreset(AkronModule.Settings.RecordingPreset), () => { }, "cpu", "nvidia", "amd"),
                    Action("CPU", () => true, () => string.Empty, () => AkronInternalRecorder.ApplyPreset(AkronRecordingPreset.Cpu), "preset", "encoder"),
                    Action("NVIDIA", () => true, () => string.Empty, () => AkronInternalRecorder.ApplyPreset(AkronRecordingPreset.Nvidia), "preset", "encoder", "nvenc"),
                    Action("AMD", () => true, () => string.Empty, () => AkronInternalRecorder.ApplyPreset(AkronRecordingPreset.Amd), "preset", "encoder", "amf")
                };
            default:
                return new List<OverlayEntry>();
        }
    }

    private static List<OverlayEntry> BuildDisplayEntriesForTab(string tab, Level level) {
        return BuildEntriesForTab(tab, level);
    }

    private static List<OverlayEntry> SortCreatorEntries(IEnumerable<OverlayEntry> entries) {
        return (entries ?? Enumerable.Empty<OverlayEntry>())
            .OrderBy(GetCreatorEntrySortGroup)
            .ThenBy(GetCreatorEntrySortRank)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetCreatorEntrySortGroup(OverlayEntry entry) {
        if (entry.Control != OverlayEntryControl.Action) {
            return 0;
        }

        if (string.Equals(entry.Label, "Map Capture", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Label, "Room Capture", StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        return GetCreatorEntrySortRank(entry) < int.MaxValue ? 2 : 3;
    }

    private static int GetCreatorEntrySortRank(OverlayEntry entry) {
        return entry.Label switch {
            "Warp Selected Room" => 0,
            "Previous Room" => 1,
            "Next Room" => 2,
            "Previous Room In Order" => 3,
            "Next Room In Order" => 4,
            "Previous Checkpoint" => 5,
            "Next Checkpoint" => 6,
            "Previous Map" => 7,
            "Next Map" => 8,
            "Open Debug Map" => 9,
            _ => int.MaxValue
        };
    }

    private List<OverlayEntry> BuildSoundDisplayEntries() {
        return BuildSoundEntries(group => expandedSoundGroups.Contains(group.Label), label => () => ToggleSoundGroup(label));
    }

    private static List<OverlayEntry> BuildCollapsedSoundEntries() {
        return BuildSoundEntries(_ => false, _ => () => { });
    }

    private static List<OverlayEntry> BuildSoundEntries(Func<SoundGroupSpec, bool> includeChildren, Func<string, Action> buildGroupAction) {
        List<OverlayEntry> entries = BuildSoundTopLevelEntries();
        foreach (SoundGroupSpec group in SoundGroups) {
            entries.Add(SoundGroupHeader(group, buildGroupAction(group.Label)));
            if (!includeChildren(group)) {
                continue;
            }

            entries.AddRange(BuildSoundVolumeEntries(group.SoundLabels, group.Label));
        }

        return entries;
    }

    private static List<OverlayEntry> BuildSoundTopLevelEntries() {
        return new List<OverlayEntry> {
            Toggle("Audio Splitter", () => AkronModule.Settings.AudioSplitter, value => AkronModule.Settings.AudioSplitter = value, "music device", "sfx device", "audio devices"),
            Toggle("Allow Low Volume", () => AkronModule.Settings.AllowLowVolume, AkronActions.SetAllowLowVolume, "audio", "volume", "mute"),
            NumericToggle("Audio Speed", AkronFeatureKind.AudioSpeed, () => AkronModule.Settings.AudioSpeed, value => AkronModule.Settings.AudioSpeed = value, () => AkronModule.Settings.AudioSpeedMultiplier, value => AkronModule.Settings.AudioSpeedMultiplier = AkronModuleSettings.ClampAudioMultiplier(value), 0.1f, 4f, "%.2f", "x", false),
            NumericToggle("Pitch Shift", AkronFeatureKind.PitchShift, () => AkronModule.Settings.PitchShift, value => AkronModule.Settings.PitchShift = value, () => AkronModule.Settings.PitchShiftMultiplier, value => AkronModule.Settings.PitchShiftMultiplier = AkronModuleSettings.ClampAudioMultiplier(value), 0.1f, 4f, "%.2f", "x", false)
        };
    }

    private static OverlayEntry SoundGroupHeader(SoundGroupSpec group, Action toggle) {
        return new OverlayEntry(
            group.Label,
            () => true,
            () => DescribeSoundGroupValue(group),
            toggle,
            BuildSearchTerms(group.Label, new[] { "sound group" }),
            false,
            OverlayEntryControl.GroupHeader,
            soundGroupLabel: group.Label);
    }

    private static IEnumerable<OverlayEntry> BuildSoundVolumeEntries(IEnumerable<string> labels, string groupLabel = null) {
        foreach (string soundLabel in labels) {
            AkronEarAid.SoundDefinition sound = AkronEarAid.Sounds.First(candidate => string.Equals(candidate.Label, soundLabel, StringComparison.OrdinalIgnoreCase));
            string key = sound.Key;
            string label = sound.Label;
            yield return new OverlayEntry(
                label,
                () => true,
                () => AkronEarAid.OverrideEnabled(key) ? "On" : "Off",
                () => AkronEarAid.SetOverrideEnabled(key, !AkronEarAid.OverrideEnabled(key)),
                BuildSearchTerms(label, new[] {
                    "sound volume",
                    "sfx",
                    "volume",
                    key
                }),
                true,
                OverlayEntryControl.Toggle,
                soundGroupLabel: groupLabel ?? string.Empty,
                active: () => AkronEarAid.OverrideEnabled(key));
        }
    }

    private void ToggleSoundGroup(string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return;
        }

        if (!expandedSoundGroups.Add(label)) {
            expandedSoundGroups.Remove(label);
        }

        InvalidateDisplayActionEntryCache();
    }

    private bool IsSoundGroupExpanded(string label) {
        return !string.IsNullOrWhiteSpace(label) && expandedSoundGroups.Contains(label);
    }

    private static string DescribeSoundGroupValue(SoundGroupSpec group) {
        int total = group.SoundLabels.Length;
        int enabled = 0;
        foreach (string label in group.SoundLabels) {
            AkronEarAid.SoundDefinition sound = AkronEarAid.Sounds.First(candidate => string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase));
            if (AkronEarAid.OverrideEnabled(sound.Key)) {
                enabled++;
            }
        }

        return enabled == 0 ? total + " sounds" : enabled + " on / " + total + " sounds";
    }

    private sealed class SoundGroupSpec {
        public SoundGroupSpec(string label, params string[] soundLabels) {
            Label = label;
            SoundLabels = soundLabels ?? Array.Empty<string>();
        }

        public string Label { get; }
        public string[] SoundLabels { get; }
    }

    private static readonly SoundGroupSpec[] SoundGroups = {
        new SoundGroupSpec("Player", "Death", "Respawn", "Golden Death"),
        new SoundGroupSpec("Objects", "Broken Window", "Conveyor", "Core Block", "Dream Block", "Drum Swap Block", "Kevin Block", "Move Block", "Spring", "Touch Switch Complete", "Zip Mover"),
        new SoundGroupSpec("Entities", "Fireball", "Lava Barrier", "Lightning Strike", "Oshiro Boss", "Seeker"),
        new SoundGroupSpec("Ambience", "Bird Squawk", "Lightning Ambience", "Farewell Wind", "Ridge Wind"),
        new SoundGroupSpec("UI", "Dialogue", "Heart Collect", "Item Crystal Death", "Pico-8 Flag")
    };

    private static OverlayEntry Action(string label, Func<bool> enabled, Func<string> value, Action action, params string[] tags) {
        return Action(label, null, enabled, value, action, false, null, tags);
    }

    private static OverlayEntry Action(string label, Func<bool> enabled, Func<string> value, Action action, Func<bool> active, params string[] tags) {
        return Action(label, null, enabled, value, action, true, active, tags);
    }

    private static OverlayEntry Action(string label, AkronFeatureKind featureKind, Func<bool> enabled, Func<string> value, Action action, params string[] tags) {
        return Action(label, featureKind, enabled, value, action, false, null, tags);
    }

    private static OverlayEntry Action(string label, AkronFeatureKind featureKind, Func<bool> enabled, Func<string> value, Action action, Func<bool> active, params string[] tags) {
        return Action(label, (AkronFeatureKind?) featureKind, enabled, value, action, true, active, tags);
    }

    private static OverlayEntry Action(string label, AkronFeatureKind? featureKind, Func<bool> enabled, Func<string> value, Action action, bool isToggle, Func<bool> active, params string[] tags) {
        return new OverlayEntry(label, enabled, value, () => {
            if (enabled()) {
                action();
            }
        }, BuildSearchTerms(label, tags), isToggle, OverlayEntryControl.Action, featureKind, active: active);
    }

    private static OverlayEntry UploadPackRow(Level level) {
        return new OverlayEntry(
            "Upload Pack",
            () => level != null,
            AkronCommunityPackUploads.DescribeOverlayAction,
            () => {
                if (level != null) {
                    OpenUploadPackWindow();
                }
            },
            BuildSearchTerms("Upload Pack", new[] { "community", "upload", ".akr", "discord", "anonymous" }),
            false,
            OverlayEntryControl.Action,
            AkronFeatureKind.ScreenshotTool,
            forceOptionsPopup: true);
    }

    private static OverlayEntry StartPosRow(Level level) {
        return new OverlayEntry(
            "StartPos",
            () => level != null,
            DescribeStartPosActionValue,
            () => {
                if (level != null) {
                    AkronActions.LoadStartPos(level);
                }
            },
            BuildSearchTerms("StartPos", Array.Empty<string>()),
            false,
            OverlayEntryControl.StartPosActions,
            AkronFeatureKind.StartPosTools,
            forceOptionsPopup: true,
            optionsPopupKey: "StartPos Snapshot Slot");
    }

    private static OverlayEntry PlaceStartPosRow() {
        return new OverlayEntry(
            "Place StartPos",
            CanUseStartPosPlacementEditor,
            () => AkronModule.Settings.StartPosMousePlacement ? "Editing" : Engine.Scene is Level ? "Place" : "No level",
            () => {
                if (CanUseStartPosPlacementEditor()) {
                    AkronModule.Settings.StartPosMousePlacement = true;
                }
            },
            BuildSearchTerms("Place StartPos", new[] { "place", "mouse", "preview", "ghost", "free camera", "spawn config" }),
            false,
            OverlayEntryControl.Action,
            AkronFeatureKind.StartPosTools);
    }

    private static OverlayEntry StartPosSwitcherRow(Level level) {
        return new OverlayEntry(
            "StartPos Switcher",
            () => true,
            DescribeStartPosSwitcherBindings,
            () => {
                if (level != null) {
                    AkronActions.ShiftStartPos(level, 1);
                }
            },
            BuildSearchTerms("StartPos Switcher", new[] { "previous", "next", "cycle", "keybind" }),
            false,
            OverlayEntryControl.Action,
            AkronFeatureKind.StartPosTools,
            forceOptionsPopup: true);
    }

    private static OverlayEntry Selector(string label, Func<bool> enabled, Func<string> value, Action action, params string[] tags) {
        return new OverlayEntry(label, enabled, value, () => {
            if (enabled()) {
                action();
            }
        }, BuildSearchTerms(label, tags), false, OverlayEntryControl.Selector);
    }

    private static OverlayEntry SelectorDropdown(
        string label,
        Func<bool> enabled,
        Func<string> value,
        Action fallbackAction,
        Func<IReadOnlyList<SelectorDropdownChoice>> choices,
        params string[] tags) {
        return new OverlayEntry(label, enabled, value, () => {
            if (enabled()) {
                fallbackAction();
            }
        }, BuildSearchTerms(label, tags), false, OverlayEntryControl.Selector, selectorChoices: choices);
    }

    private static OverlayEntry Toggle(string label, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return Toggle(label, null, getter, setter, tags);
    }

    private static OverlayEntry Toggle(string label, AkronFeatureKind featureKind, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return Toggle(label, (AkronFeatureKind?) featureKind, getter, setter, tags);
    }

    private static OverlayEntry Toggle(string label, AkronFeatureKind? featureKind, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return new OverlayEntry(label, () => true, () => getter() ? "On" : "Off", () => setter(!getter()), BuildSearchTerms(label, tags), true, OverlayEntryControl.Toggle, featureKind, active: getter);
    }

    private static OverlayEntry LoggingToggle() {
        return new OverlayEntry(
            "Logging",
            () => true,
            () => AkronModule.Settings.Logging ? "On" : "Off",
            () => {
                bool next = !AkronModule.Settings.Logging;
                AkronLog.FlushDiagnosticSummaries();
                AkronModule.Settings.Logging = next;
                AkronLog.LogSettingsChanged("enabled=" + next.ToString().ToLowerInvariant());
            },
            BuildSearchTerms("Logging", new[] { "logs", "debug", "diagnostics", "trace" }),
            true,
            OverlayEntryControl.Toggle,
            AkronFeatureKind.Logging,
            forceOptionsPopup: true,
            active: () => AkronModule.Settings.Logging);
    }

    private static OverlayEntry Keybind(string label, string actionTab, string actionLabel, params string[] tags) {
        string actionKey = BuildActionKey(actionTab, actionLabel);
        return KeybindAction(label, actionKey, tags);
    }

    private static OverlayEntry KeybindAction(string label, string actionKey, params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => DescribeEffectiveMenuBinding(actionKey, label),
            () => { },
            BuildSearchTerms(label, tags),
            false,
            OverlayEntryControl.Keybind,
            actionKeyOverride: actionKey);
    }

    private static OverlayEntry SearchInput() {
        return new OverlayEntry(
            "Search",
            () => true,
            () => string.Empty,
            () => { },
            BuildSearchTerms("Search", new[] { "find", "filter", "lookup" }),
            false,
            OverlayEntryControl.SearchInput);
    }

    private static OverlayEntry NumericRow(
        string label,
        Func<float> numericValue,
        Action<float> numericSetter,
        float minimum,
        float maximum,
        string format,
        string suffix,
        bool integer,
        params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => FormatDirectNumericValue(numericValue, format, suffix, integer),
            () => { },
            BuildSearchTerms(label, tags),
            false,
            OverlayEntryControl.NumericInput,
            null,
            numericValue,
            numericSetter,
            minimum,
            maximum,
            format,
            suffix,
            integer);
    }

    private static string FormatDirectNumericValue(Func<float> numericValue, string format, string suffix, bool integer) {
        float value = numericValue?.Invoke() ?? 0f;
        string formatted = integer
            ? ((int) Math.Round(value)).ToString()
            : value.ToString(ConvertImGuiFormat(format));
        return string.IsNullOrWhiteSpace(suffix) ? formatted : formatted + " " + suffix;
    }

    private static OverlayEntry PolicyToggle(string label, AkronFeatureKind featureKind, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return new OverlayEntry(label, () => true, () => getter() ? "On" : "Off", () => {
            bool next = !getter();
            if (next && !AkronModule.TryUse(featureKind)) {
                return;
            }

            setter(next);
        }, BuildSearchTerms(label, tags), true, OverlayEntryControl.Toggle, featureKind, active: getter);
    }

    private static OverlayEntry EntityInspectorRow() {
        return new OverlayEntry(
            "Entity Inspector",
            () => true,
            () => AkronModule.Settings.EntityInspector
                ? AkronEntityInspector.NormalizeInspectorPinFilter(AkronModule.Settings.InspectorPinFilter).ToString()
                : "Off",
            () => {
                bool next = !AkronModule.Settings.EntityInspector;
                if (next && !AkronModule.TryUse(AkronFeatureKind.EntityInspector)) {
                    return;
                }

                AkronModule.Settings.EntityInspector = next;
            },
            BuildSearchTerms("Entity Inspector", new[] { "inspector", "entity", "trigger", "pin", "click", "properties" }),
            true,
            OverlayEntryControl.Toggle,
            AkronFeatureKind.EntityInspector,
            forceOptionsPopup: true,
            active: () => AkronModule.Settings.EntityInspector);
    }

    private static OverlayEntry InvincibilityToggle() {
        return new OverlayEntry(
            "Invincibility",
            () => true,
            () => AkronModule.Settings.Invincibility
                ? AkronModuleSettings.NormalizeInvincibilityMode(AkronModule.Settings.InvincibilityMode).ToString()
                : "Off",
            () => {
                bool next = !AkronModule.Settings.Invincibility;
                if (next && !AkronModule.TryUse(AkronFeatureKind.Invincibility)) {
                    return;
                }

                AkronModule.Settings.Invincibility = next;
            },
            BuildSearchTerms("Invincibility", new[] { "assist", "invincible", "death", "cheat", "native", "akron" }),
            true,
            OverlayEntryControl.Toggle,
            AkronFeatureKind.Invincibility,
            active: () => AkronModule.Settings.Invincibility);
    }

    private static OverlayEntry NumericToggle(
        string label,
        AkronFeatureKind featureKind,
        Func<bool> getter,
        Action<bool> setter,
        Func<float> numericValue,
        Action<float> numericSetter,
        float minimum,
        float maximum,
        string format,
        string suffix,
        bool integer,
        params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => getter() ? "On" : "Off",
            () => {
                bool next = !getter();
                if (next && !AkronModule.TryUse(featureKind)) {
                    return;
                }

                setter(next);
            },
            BuildSearchTerms(label, tags),
            true,
            OverlayEntryControl.Toggle,
            featureKind,
            numericValue,
            numericSetter,
            minimum,
            maximum,
            format,
            suffix,
            integer,
            active: getter);
    }

    private static OverlayEntry InlineNumericToggle(
        string label,
        AkronFeatureKind featureKind,
        Func<bool> getter,
        Action<bool> setter,
        Func<float> numericValue,
        Action<float> numericSetter,
        float minimum,
        float maximum,
        string format,
        string suffix,
        bool integer,
        params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => getter() ? "On" : "Off",
            () => {
                bool next = !getter();
                if (next && !AkronModule.TryUse(featureKind)) {
                    return;
                }

                setter(next);
            },
            BuildSearchTerms(label, tags),
            true,
            OverlayEntryControl.NumericInput,
            featureKind,
            numericValue,
            numericSetter,
            minimum,
            maximum,
            format,
            suffix,
            integer,
            active: getter);
    }

    private static int CycleInt(int value, int minimum, int maximum) {
        return value > maximum ? minimum : Calc.Clamp(value, minimum, maximum);
    }

    private static string BuildSearchTerms(string label, IEnumerable<string> tags) {
        List<string> terms = new List<string>();
        if (SearchAliases.TryGetValue(label, out string[] aliases)) {
            terms.AddRange(aliases);
        }

        if (tags != null) {
            terms.AddRange(tags);
        }

        return string.Join(" ", terms.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string ToExtendedVariantEntryLabel(AkronExtendedVariantOption option) {
        return option.Label;
    }

    private static bool IsExtendedVariantEntryLabel(string label) {
        return !string.Equals(label, "Extended Variants Master", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(label, "Extended Variants Randomizer", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(label, "Reset Extended", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(label, "Reset Vanilla", StringComparison.OrdinalIgnoreCase) &&
               AkronExtendedVariants.GetOptionByLabel(label) != null;
    }

    private static bool HasExtendedVariantOptionsPopup(string label) {
        AkronExtendedVariantOption option = GetExtendedVariantOptionFromLabel(label);
        return option != null && option.CurrentValue is not bool;
    }

    private static string GetExtendedVariantNameFromLabel(string label) {
        if (!IsExtendedVariantEntryLabel(label)) {
            return string.Empty;
        }

        AkronExtendedVariantOption option = AkronExtendedVariants.GetOptionByLabel(label);
        return option?.Name ?? string.Empty;
    }

    private static AkronExtendedVariantOption GetExtendedVariantOptionFromLabel(string label) {
        string name = GetExtendedVariantNameFromLabel(label);
        return string.IsNullOrWhiteSpace(name) ? null : AkronExtendedVariants.GetOption(name);
    }

    private static void SetExtendedVariantValue(AkronExtendedVariantOption option, object value) {
        if (option == null || !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
            return;
        }

        if (AkronExtendedVariants.TrySetValue(option.Name, value, out string message)) {
            AkronExtendedVariants.RecordVariantCheatUseIfUserControlled(option.Name);
            Engine.Scene?.Add(new AkronToast(message));
        }
    }

    private static void SetExtendedVariantConfiguredValue(AkronExtendedVariantOption option, object value) {
        if (option == null || !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
            return;
        }

        if (AkronExtendedVariants.TrySetConfiguredValue(option.Name, value, out string message)) {
            AkronExtendedVariants.RecordVariantCheatUseIfUserControlled(option.Name);
            Engine.Scene?.Add(new AkronToast(message));
        }
    }

    private static void SetExtendedVariantConfiguredFromText(AkronExtendedVariantOption option, string value) {
        if (option == null || !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
            return;
        }

        if (AkronExtendedVariants.TrySetConfiguredFromText(option.Name, value, out string message)) {
            AkronExtendedVariants.RecordVariantCheatUseIfUserControlled(option.Name);
            Engine.Scene?.Add(new AkronToast(message));
        } else {
            Engine.Scene?.Add(new AkronToast(message));
        }
    }

    private static void ResetExtendedVariantConfiguredValue(AkronExtendedVariantOption option) {
        if (option == null || !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
            return;
        }

        if (AkronExtendedVariants.TryResetConfigured(option.Name, out string message)) {
            Engine.Scene?.Add(new AkronToast(message));
        }
    }

    private bool MatchesSearch(string tab, ActionEntry entry) {
        // Search must stay on stable row metadata. Dynamic values can scan the
        // active level, backup folders, or other live state; doing that across
        // every row while typing makes in-map filtering stall.
        string haystack = tab + " " + entry.Label + " " + entry.ActionKey + " " + entry.SearchTerms;
        return haystack.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string DescribeRenderedEntryValue(ActionEntry entry) {
        return entry.Value();
    }

    private static string DescribeSelectedFlag(Level level) {
        if (level == null) {
            return "No level";
        }

        List<string> flags = AkronSessionFlagView.GetEditableFlags(level, 12).ToList();
        if (flags.Count == 0) {
            return "No flags";
        }

        int index = Calc.Clamp(AkronModule.Session.EditableFlagIndex, 0, flags.Count - 1);
        return flags[index];
    }

    private static string DescribeStartPosActionValue() {
        string state = AkronActions.GetActiveStartPos() != null ? "Set" : "Unset";
        string index = Engine.Scene is Level level ? AkronActions.DescribeStartPosIndex(level) : "0/0";
        return state + " | " + index + " | Slot " + AkronModule.Settings.ActiveStartPosSlot;
    }

    private static string DescribeBackupRetention() {
        return AkronModule.Settings.BackupsMaxCount + " max | " +
               AkronModule.Settings.BackupsDeleteOlderThanDays + "d | " +
               AkronModule.Settings.BackupsMaxTotalSizeMb + " MB";
    }

    private static string DescribeBackupTriggers() {
        int enabled = 0;
        if (AkronModule.Settings.BackupsOnStartup) enabled++;
        if (AkronModule.Settings.BackupsOnShutdown) enabled++;
        if (AkronModule.Settings.BackupsOnSave) enabled++;
        if (AkronModule.Settings.BackupsOnLevelBegin) enabled++;
        if (AkronModule.Settings.BackupsEveryInterval) enabled++;

        return enabled + "/5 on | " + AkronModule.Settings.BackupsIntervalMinutes + " min";
    }

    private static string DescribeConfirmActionsValue() {
        int enabled =
            (AkronModule.Settings.ConfirmRestart || AkronModule.Settings.ConfirmRetry ? 1 : 0) +
            (AkronModule.Settings.ConfirmReloadRoom ? 1 : 0) +
            (AkronModule.Settings.ConfirmFullReset || AkronModule.Settings.ConfirmReloadChapter ? 1 : 0) +
            (AkronModule.Settings.ConfirmLoadState ? 1 : 0);
        return enabled == 0 ? "Off" : enabled + " enabled";
    }

    private static string DescribeAutoKillArea() {
        List<Rectangle> areas = AkronModule.GetAutoKillAreas();
        if (!AkronModule.Settings.AutoKillArea || areas.Count == 0) {
            return "Areas: unset";
        }

        Rectangle latest = areas[areas.Count - 1];
        int selected = AkronModule.GetSelectedAutoKillAreaIndex() + 1;
        return "Areas: " + areas.Count + " (selected #" + selected + ", latest " + latest.X + ", " + latest.Y + " / " + latest.Width + "x" + latest.Height + ")";
    }

    private static string DescribeAutoKillConditionsSummary(AkronAutoKillAreaData area) {
        int enabled =
            (area.SpeedCondition ? 1 : 0) +
            (area.HorizontalSpeedCondition ? 1 : 0) +
            (area.VerticalSpeedCondition ? 1 : 0) +
            (area.DashCountCondition ? 1 : 0) +
            (AkronModuleSettings.NormalizeAutoKillGroundCondition(area.GroundCondition) == AkronAutoKillGroundCondition.Any ? 0 : 1) +
            (AkronModuleSettings.NormalizeAutoKillAxisCondition(area.HorizontalDirection) == AkronAutoKillAxisCondition.Any ? 0 : 1) +
            (AkronModuleSettings.NormalizeAutoKillAxisCondition(area.VerticalDirection) == AkronAutoKillAxisCondition.Any ? 0 : 1) +
            (area.PlayerStateCondition ? 1 : 0) +
            (area.InvertConditions ? 1 : 0);

        return enabled == 0 ? "none" : enabled + " active";
    }

    private static string DescribeAutoDeafenArea() {
        List<Rectangle> areas = AkronModule.GetAutoDeafenAreas();
        if (!AkronModule.Settings.AutoDeafenArea || areas.Count == 0) {
            return "Areas: unset";
        }

        Rectangle latest = areas[areas.Count - 1];
        return "Areas: " + areas.Count + " (latest " + latest.X + ", " + latest.Y + " / " + latest.Width + "x" + latest.Height + ")";
    }

    private static string FormatNoclipMultiplier(int speed) {
        return "x" + (speed / 240f).ToString("0.0");
    }

    private static string FormatNoclipFloatMultiplier(int speed) {
        return "x" + (speed / 90f).ToString("0.0");
    }

    private static void SetTimescaleMultiplier(float multiplier) {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            return;
        }

        float next = Calc.Clamp((float) Math.Round(multiplier, 1), 0.1f, 2f);
        if (next != 1f && !AkronModule.TryUse(AkronFeatureKind.Timescale)) {
            return;
        }

        session.TimescaleMultiplier = next;
        session.TimescaleEnabled = next != 1f;
        if (!session.TimescaleEnabled) {
#pragma warning disable CS0618
            Engine.TimeRate = 1f;
#pragma warning restore CS0618
        }
        Engine.Scene?.Add(new AkronToast("Timescale: " + next.ToString("0.0x")));
    }

    private static void SetTimescaleEnabled(bool enabled) {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            return;
        }

        if (enabled && session.TimescaleMultiplier != 1f && !AkronModule.TryUse(AkronFeatureKind.Timescale)) {
            return;
        }

        session.TimescaleEnabled = enabled;
        if (!enabled) {
#pragma warning disable CS0618
            Engine.TimeRate = 1f;
#pragma warning restore CS0618
        }
    }

    private static void AdjustNoclipSpeed(int delta) {
        AkronModule.Settings.NoclipSpeed = AkronModuleSettings.ClampNoclipSpeed(AkronModule.Settings.NoclipSpeed + delta);
        Engine.Scene?.Add(new AkronToast("Noclip speed: " + FormatNoclipMultiplier(AkronModule.Settings.NoclipSpeed)));
    }

    private static void AdjustNoclipFloatSpeed(int delta) {
        AkronModule.Settings.NoclipFloatSpeed = AkronModuleSettings.ClampNoclipFloatSpeed(AkronModule.Settings.NoclipFloatSpeed + delta);
        Engine.Scene?.Add(new AkronToast("Noclip grab speed: " + FormatNoclipFloatMultiplier(AkronModule.Settings.NoclipFloatSpeed)));
    }

    private static void SetNoclipSpeedMultiplier(float multiplier) {
        int speed = (int) Math.Round(multiplier * 240f);
        AkronModule.Settings.NoclipSpeed = AkronModuleSettings.ClampNoclipSpeed(speed);
        Engine.Scene?.Add(new AkronToast("Noclip speed: " + FormatNoclipMultiplier(AkronModule.Settings.NoclipSpeed)));
    }

    private static void SetNoclipGrabSpeedMultiplier(float multiplier) {
        int speed = (int) Math.Round(multiplier * 90f);
        AkronModule.Settings.NoclipFloatSpeed = AkronModuleSettings.ClampNoclipFloatSpeed(speed);
        Engine.Scene?.Add(new AkronToast("Noclip grab speed: " + FormatNoclipFloatMultiplier(AkronModule.Settings.NoclipFloatSpeed)));
    }

    private static string DescribeRecordingReplaySettings() {
        if (AkronInternalRecorder.IsReplayBuffering) {
            return AkronInternalRecorder.DescribeReplayBufferStatus();
        }

        int seconds = AkronModuleSettings.ClampRecordingReplayBufferSeconds(AkronModule.Settings.RecordingReplayBufferSeconds);
        if (seconds <= 0) {
            return "Off";
        }

        string autoStart = AkronModuleSettings.FormatRecordingReplayAutoStart(AkronModule.Settings.RecordingReplayAutoStart);
        return autoStart == "Off" ? seconds + "s ready" : autoStart;
    }

    private static string DescribeRecordingReplayBufferAction() {
        return AkronInternalRecorder.IsReplayBuffering ? "Save" : "Off";
    }

    private static string DescribeCompletionCapture() {
        if (AkronModule.Settings.RecordingReplayAutoStart == AkronRecordingReplayAutoStart.InLevels &&
            AkronModule.Settings.RecordingTriggerRoomEntryToClear &&
            AkronModule.Settings.RecordingTriggerCheckpointClear) {
            return AkronInternalRecorder.DescribeReplayBufferStatus();
        }

        return "Arm";
    }

    private static string DescribeCompletionVideoSource() {
        Level level = Engine.Scene as Level;
        if (level == null) {
            return "No map";
        }

        return "Build";
    }

    private static string DescribeRecordingOutputSettings() {
        string format = AkronModuleSettings.FormatRecordingContainer(AkronModule.Settings.RecordingContainerFormat);
        return AkronModule.Settings.RecordingAutoRemux ? format + " / remux" : format;
    }

    private static string DescribeRecordingAudioTracks() {
        List<string> tracks = new List<string>();
        if (AkronModule.Settings.RecordingAudioFullMixTrack) tracks.Add("Mix");
        if (AkronModule.Settings.RecordingAudioMusicTrack) tracks.Add("Music");
        if (AkronModule.Settings.RecordingAudioSfxTrack) tracks.Add("SFX");
        if (AkronModule.Settings.RecordingAudioAmbienceTrack) tracks.Add("Amb");
        return tracks.Count == 0 ? "Off" : string.Join("/", tracks);
    }

    private static string DescribeRecordingTriggers() {
        int count = 0;
        if (AkronModule.Settings.RecordingTriggerLastDeath) count++;
        if (AkronModule.Settings.RecordingTriggerRespawnToDeath) count++;
        if (AkronModule.Settings.RecordingTriggerRoomEntryToClear) count++;
        if (AkronModule.Settings.RecordingTriggerCheckpointClear) count++;
        if (AkronModule.Settings.RecordingTriggerBerryCollect) count++;
        if (AkronModule.Settings.RecordingTriggerGoldenDeath) count++;
        return count == 0 ? "Manual" : count + " triggers";
    }

}
