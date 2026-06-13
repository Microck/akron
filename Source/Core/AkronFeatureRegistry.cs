using System;
using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public static class AkronFeatureRegistry
{
    private static readonly Dictionary<AkronFeatureKind, FeatureDefinition> Definitions = new Dictionary<AkronFeatureKind, FeatureDefinition> {
        { AkronFeatureKind.RoomLabelOverlay, new FeatureDefinition(AkronFeatureKind.RoomLabelOverlay, AkronStatus.RegularClean, "Room labels", "Passive room information only.") },
        { AkronFeatureKind.StaminaWidget, new FeatureDefinition(AkronFeatureKind.StaminaWidget, AkronStatus.Cheat, "Stamina widget", "Displays already-current player state.") },
        { AkronFeatureKind.SpeedWidget, new FeatureDefinition(AkronFeatureKind.SpeedWidget, AkronStatus.Cheat, "Speed widget", "Displays already-current player state.") },
        { AkronFeatureKind.DashWidget, new FeatureDefinition(AkronFeatureKind.DashWidget, AkronStatus.Cheat, "Dash widget", "Displays already-current player state.") },
        { AkronFeatureKind.InputViewer, new FeatureDefinition(AkronFeatureKind.InputViewer, AkronStatus.GoldberryHardlistClean, "Input viewer", "Displays local inputs without changing gameplay.") },
        { AkronFeatureKind.InputHistory, new FeatureDefinition(AkronFeatureKind.InputHistory, AkronStatus.GoldberryHardlistClean, "Input history", "Displays recent local input state without changing gameplay.") },
        { AkronFeatureKind.ResourceBars, new FeatureDefinition(AkronFeatureKind.ResourceBars, AkronStatus.Cheat, "Resource bars", "Displays already-current player stamina and dash resources.") },
        { AkronFeatureKind.RoomTimer, new FeatureDefinition(AkronFeatureKind.RoomTimer, AkronStatus.RegularClean, "Room timer", "Displays timing information for the current room.") },
        { AkronFeatureKind.DeathStats, new FeatureDefinition(AkronFeatureKind.DeathStats, AkronStatus.GoldberryHardlistClean, "Death stats", "Displays death counters without mutating play.") },
        { AkronFeatureKind.ReducedVisualNoise, new FeatureDefinition(AkronFeatureKind.ReducedVisualNoise, AkronStatus.RegularClean, "Reduced visual noise", "Accessibility-focused visual suppression.") },
        { AkronFeatureKind.VisualTuning, new FeatureDefinition(AkronFeatureKind.VisualTuning, AkronStatus.RegularClean, "Visual tuning", "Adjusts lighting, bloom, and tint presentation without changing gameplay state.") },
        { AkronFeatureKind.GrabModeHotkey, new FeatureDefinition(AkronFeatureKind.GrabModeHotkey, AkronStatus.RegularClean, "Grab mode hotkey", "Changes a player control preference without changing gameplay state.") },
        { AkronFeatureKind.ScreenshotTool, new FeatureDefinition(AkronFeatureKind.ScreenshotTool, AkronStatus.GoldberryHardlistClean, "Screenshot tool", "Captures the current view for review and sharing.") },
        { AkronFeatureKind.RetryHotkey, new FeatureDefinition(AkronFeatureKind.RetryHotkey, AkronStatus.RegularClean, "Retry hotkey", "Ends the current attempt through Celeste's retry flow.") },
        { AkronFeatureKind.RoomReload, new FeatureDefinition(AkronFeatureKind.RoomReload, AkronStatus.Cheat, "Room reload", "Restarts the current room.") },
        { AkronFeatureKind.ChapterReload, new FeatureDefinition(AkronFeatureKind.ChapterReload, AkronStatus.RegularClean, "Chapter reload", "Restarts the current chapter through Celeste's loader.") },
        { AkronFeatureKind.DebugMapLauncher, new FeatureDefinition(AkronFeatureKind.DebugMapLauncher, AkronStatus.Cheat, "Debug map launcher", "Opens Celeste's debug map scene.") },
        { AkronFeatureKind.MountainViewer, new FeatureDefinition(AkronFeatureKind.MountainViewer, AkronStatus.RegularClean, "Mountain viewer", "Moves the player into an overworld inspection utility.") },
        { AkronFeatureKind.Savestates, new FeatureDefinition(AkronFeatureKind.Savestates, AkronStatus.Cheat, "StartPos runtime snapshot", "Captures or restores the active StartPos runtime snapshot.") },
        { AkronFeatureKind.BrokeredSavestates, new FeatureDefinition(AkronFeatureKind.BrokeredSavestates, AkronStatus.Cheat, "External SRT state", "Forwards capture, restore, and clear actions to Speedrun Tool state slots.") },
        { AkronFeatureKind.TasHandoff, new FeatureDefinition(AkronFeatureKind.TasHandoff, AkronStatus.Cheat, "TAS handoff", "Delegates active control to TAS tooling.") },
        { AkronFeatureKind.SplitHelper, new FeatureDefinition(AkronFeatureKind.SplitHelper, AkronStatus.RegularClean, "Split helper", "Exports or syncs room timing with external timing tools.") },
        { AkronFeatureKind.DeloadSimulation, new FeatureDefinition(AkronFeatureKind.DeloadSimulation, AkronStatus.Cheat, "Deload simulation", "Fast-forwards spinner-related timers and visual timers.") },
        { AkronFeatureKind.RoomWarp, new FeatureDefinition(AkronFeatureKind.RoomWarp, AkronStatus.Cheat, "Room warp", "Teleports the player to a selected room.") },
        { AkronFeatureKind.HitboxViewer, new FeatureDefinition(AkronFeatureKind.HitboxViewer, AkronStatus.Cheat, "Hitbox viewer", "Draws collision and hazard hitboxes.") },
        { AkronFeatureKind.EntityInspector, new FeatureDefinition(AkronFeatureKind.EntityInspector, AkronStatus.Cheat, "Entity inspector", "Shows nearby entity type and position information.") },
        { AkronFeatureKind.FlagInspector, new FeatureDefinition(AkronFeatureKind.FlagInspector, AkronStatus.Cheat, "Flag inspector", "Shows selected session and flag state.") },
        { AkronFeatureKind.RespawnTime, new FeatureDefinition(AkronFeatureKind.RespawnTime, AkronStatus.RegularClean, "Respawn time", "Changes post-death respawn pacing without changing live gameplay.") },
        { AkronFeatureKind.FrameAdvance, new FeatureDefinition(AkronFeatureKind.FrameAdvance, AkronStatus.Cheat, "Frame advance", "Advances one frozen gameplay frame at a time.") },
        { AkronFeatureKind.Freeze, new FeatureDefinition(AkronFeatureKind.Freeze, AkronStatus.Cheat, "Freeze", "Freezes gameplay updates until toggled off or stepped.") },
        { AkronFeatureKind.Timescale, new FeatureDefinition(AkronFeatureKind.Timescale, AkronStatus.Cheat, "Timescale", "Changes gameplay update speed.") },
        { AkronFeatureKind.AutoKill, new FeatureDefinition(AkronFeatureKind.AutoKill, AkronStatus.Cheat, "Auto kill", "Kills the player after a configured map-time threshold or inside selected regions.") },
        { AkronFeatureKind.AutoDeafen, new FeatureDefinition(AkronFeatureKind.AutoDeafen, AkronStatus.RegularClean, "Auto deafen", "Presses the configured Discord deafen hotkey after a selected region trigger until death or reset.") },
        { AkronFeatureKind.TransitionSpeed, new FeatureDefinition(AkronFeatureKind.TransitionSpeed, AkronStatus.Cheat, "Transition speed", "Changes room transition timing.") },
        { AkronFeatureKind.LowVolumeBypass, new FeatureDefinition(AkronFeatureKind.LowVolumeBypass, AkronStatus.RegularClean, "Allow low volume", "Audio accessibility setting that lowers music and SFX through normal Celeste settings.") },
        { AkronFeatureKind.HudVisibility, new FeatureDefinition(AkronFeatureKind.HudVisibility, AkronStatus.RegularClean, "HUD visibility", "Hides visual HUD surfaces without changing gameplay state.") },
        { AkronFeatureKind.PauseMenuVisibility, new FeatureDefinition(AkronFeatureKind.PauseMenuVisibility, AkronStatus.RegularClean, "Hide pause menu", "Hides the pause menu surface without changing paused state.") },
        { AkronFeatureKind.PauseCountdown, new FeatureDefinition(AkronFeatureKind.PauseCountdown, AkronStatus.Cheat, "Pause countdown", "Delays resumed gameplay after unpausing and shows a countdown.") },
        { AkronFeatureKind.ShowTrajectory, new FeatureDefinition(AkronFeatureKind.ShowTrajectory, AkronStatus.Cheat, "Show trajectory", "Draws a short local movement preview.") },
        { AkronFeatureKind.FreeCamera, new FeatureDefinition(AkronFeatureKind.FreeCamera, AkronStatus.Cheat, "Free camera", "Moves the camera independently for map inspection.") },
        { AkronFeatureKind.AudioSpeed, new FeatureDefinition(AkronFeatureKind.AudioSpeed, AkronStatus.RegularClean, "Audio speed", "Changes active audio playback speed without changing simulation timing.") },
        { AkronFeatureKind.PitchShift, new FeatureDefinition(AkronFeatureKind.PitchShift, AkronStatus.RegularClean, "Pitch shift", "Changes active audio pitch as an accessibility/presentation setting.") },
        { AkronFeatureKind.FpsBypass, new FeatureDefinition(AkronFeatureKind.FpsBypass, AkronStatus.RegularClean, "FPS bypass", "Raises render cadence or enables Motion Smoothing's smoothing pipeline while keeping Celeste physics at 60 FPS.") },
        { AkronFeatureKind.TpsBypass, new FeatureDefinition(AkronFeatureKind.TpsBypass, AkronStatus.Cheat, "TPS bypass", "Changes the simulation tick cadence.") },
        { AkronFeatureKind.SafeModeStats, new FeatureDefinition(AkronFeatureKind.SafeModeStats, AkronStatus.Cheat, "Safe Mode stat guards", "Prevents selected local stat fields from being dirtied by guarded Akron sessions.") },
        { AkronFeatureKind.Screenshake, new FeatureDefinition(AkronFeatureKind.Screenshake, AkronStatus.GoldberryHardlistClean, "Screenshake", "Accessibility setting that suppresses or reduces camera shake without changing gameplay state.") },
        { AkronFeatureKind.TriggerViewer, new FeatureDefinition(AkronFeatureKind.TriggerViewer, AkronStatus.Cheat, "Show triggers", "Draws invisible trigger regions.") },
        { AkronFeatureKind.StartPosTools, new FeatureDefinition(AkronFeatureKind.StartPosTools, AkronStatus.Cheat, "StartPos tools", "Captures and restores StartPos snapshots, including smart same-room respawn selection.") },
        { AkronFeatureKind.ClickTeleport, new FeatureDefinition(AkronFeatureKind.ClickTeleport, AkronStatus.Cheat, "Click teleport", "Teleports the player to the cursor and bypasses intended traversal.") },
        { AkronFeatureKind.CustomTrail, new FeatureDefinition(AkronFeatureKind.CustomTrail, AkronStatus.RegularClean, "Custom trail", "Changes player trail presentation without moving the player or changing collision.") },
        { AkronFeatureKind.MadelineHairLength, new FeatureDefinition(AkronFeatureKind.MadelineHairLength, AkronStatus.RegularClean, "Madeline hair length", "Changes Madeline hair segment count for visual customization only.") },
        { AkronFeatureKind.MadelineEffectSync, new FeatureDefinition(AkronFeatureKind.MadelineEffectSync, AkronStatus.RegularClean, "Madeline effect sync", "Matches selected player visual effects to Madeline's active hair color.") },
        { AkronFeatureKind.HidePlayer, new FeatureDefinition(AkronFeatureKind.HidePlayer, AkronStatus.RegularClean, "Hide player", "Hides Madeline while keeping gameplay state unchanged.") },
        { AkronFeatureKind.DeathVisuals, new FeatureDefinition(AkronFeatureKind.DeathVisuals, AkronStatus.RegularClean, "Death visuals", "Suppresses post-death particles and screen-wipe presentation only.") },
        { AkronFeatureKind.RespawnAnimation, new FeatureDefinition(AkronFeatureKind.RespawnAnimation, AkronStatus.RegularClean, "Respawn animation", "Shortens post-death respawn presentation without changing live gameplay.") },
        { AkronFeatureKind.ShowTaps, new FeatureDefinition(AkronFeatureKind.ShowTaps, AkronStatus.GoldberryHardlistClean, "Control display", "Displays local input state without changing gameplay.") },
        { AkronFeatureKind.InputsPerSecondCounter, new FeatureDefinition(AkronFeatureKind.InputsPerSecondCounter, AkronStatus.RegularClean, "Inputs per second", "Displays local input press rate without changing gameplay.") },
        { AkronFeatureKind.CustomHudLabels, new FeatureDefinition(AkronFeatureKind.CustomHudLabels, AkronStatus.RegularClean, "Custom HUD labels", "Displays configured local status labels without changing gameplay.") },
        { AkronFeatureKind.InstantComplete, new FeatureDefinition(AkronFeatureKind.InstantComplete, AkronStatus.Cheat, "Instant complete", "Forces the current chapter completion flow.") },
        { AkronFeatureKind.UnlockSystem, new FeatureDefinition(AkronFeatureKind.UnlockSystem, AkronStatus.Cheat, "Unlock system", "Mutates save unlock state.") },
        { AkronFeatureKind.HazardAccuracy, new FeatureDefinition(AkronFeatureKind.HazardAccuracy, AkronStatus.Cheat, "Hazard accuracy", "Prevents deaths while tracking invalid hazard contacts.") },
        { AkronFeatureKind.Noclip, new FeatureDefinition(AkronFeatureKind.Noclip, AkronStatus.Cheat, "Noclip", "Bypasses collision and intended map traversal.") },
        { AkronFeatureKind.Invincibility, new FeatureDefinition(AkronFeatureKind.Invincibility, AkronStatus.Cheat, "Invincibility", "Bypasses death and hazard rules.") },
        { AkronFeatureKind.InfiniteStamina, new FeatureDefinition(AkronFeatureKind.InfiniteStamina, AkronStatus.Cheat, "Infinite stamina", "Mutates player resource constraints.") },
        { AkronFeatureKind.InfiniteDash, new FeatureDefinition(AkronFeatureKind.InfiniteDash, AkronStatus.Cheat, "Infinite dash", "Mutates player resource constraints.") },
        { AkronFeatureKind.DashCountOverride, new FeatureDefinition(AkronFeatureKind.DashCountOverride, AkronStatus.Cheat, "Dash count", "Changes the player's current dash resource count.") },
        { AkronFeatureKind.SpeedNumber, new FeatureDefinition(AkronFeatureKind.SpeedNumber, AkronStatus.Cheat, "Speed number", "Displays already-current player speed above Madeline without changing gameplay.") },
        { AkronFeatureKind.RefillClarity, new FeatureDefinition(AkronFeatureKind.RefillClarity, AkronStatus.RegularClean, "Refill clarity", "Highlights already-visible one-use refills without revealing hidden state or changing gameplay.") },
        { AkronFeatureKind.FreezeFrames, new FeatureDefinition(AkronFeatureKind.FreezeFrames, AkronStatus.Cheat, "Freeze frames", "Suppresses native hitstop/freeze timing and changes gameplay feel.") },
        { AkronFeatureKind.GroundRefillRules, new FeatureDefinition(AkronFeatureKind.GroundRefillRules, AkronStatus.Cheat, "Ground refill rules", "Changes dash or stamina refill behavior on the ground.") },
        { AkronFeatureKind.MovementStatMutation, new FeatureDefinition(AkronFeatureKind.MovementStatMutation, AkronStatus.Cheat, "Movement mutation", "Changes core movement rules.") },
        { AkronFeatureKind.PauseTimerFreeze, new FeatureDefinition(AkronFeatureKind.PauseTimerFreeze, AkronStatus.Cheat, "Pause timer freeze", "Stops level and journal timer accumulation while paused.") },
        { AkronFeatureKind.InputAssistShortcut, new FeatureDefinition(AkronFeatureKind.InputAssistShortcut, AkronStatus.Cheat, "Input assist shortcut", "Synthesizes or modifies player inputs.") },
        { AkronFeatureKind.ExtendedVariantMode, new FeatureDefinition(AkronFeatureKind.ExtendedVariantMode, AkronStatus.GoldberryHardlistClean, "External variant mode", "Enables Extended Variant Mode hooks; individual user-controlled variants and randomizer use are tracked separately.") },
        { AkronFeatureKind.InternalRecorder, new FeatureDefinition(AkronFeatureKind.InternalRecorder, AkronStatus.RegularClean, "Internal recorder", "Captures local game frames for review without mutating gameplay state.") },
        { AkronFeatureKind.FastLookout, new FeatureDefinition(AkronFeatureKind.FastLookout, AkronStatus.Cheat, "Fast lookout", "Speeds up vanilla lookout camera movement only while the configured hold bind is pressed.") },
        { AkronFeatureKind.LevelEnterSkip, new FeatureDefinition(AkronFeatureKind.LevelEnterSkip, AkronStatus.RegularClean, "Level intro skip", "Skips repeated postcard and B-side intro waits through an explicit hold-confirm action.") },
        { AkronFeatureKind.DeathPbLossRestart, new FeatureDefinition(AkronFeatureKind.DeathPbLossRestart, AkronStatus.RegularClean, "PB loss restart", "Offers a restart prompt based on visible death-count state.") },
        { AkronFeatureKind.CameraOffset, new FeatureDefinition(AkronFeatureKind.CameraOffset, AkronStatus.Cheat, "Camera offset", "Changes the current level camera offset for map inspection.") },
        { AkronFeatureKind.CursorZoom, new FeatureDefinition(AkronFeatureKind.CursorZoom, AkronStatus.Cheat, "Cursor zoom", "Zooms the current level around the cursor for map inspection.") },
        { AkronFeatureKind.UnsafeNativeSavestateOverride, new FeatureDefinition(AkronFeatureKind.UnsafeNativeSavestateOverride, AkronStatus.Cheat, "Unsafe StartPos restore override", "Bypasses StartPos restore risk blocking.") },
        { AkronFeatureKind.SubmissionMode, new FeatureDefinition(AkronFeatureKind.SubmissionMode, AkronStatus.GoldberryHardlistClean, "Submission mode", "Enables recording metadata defaults and related warning surfaces without changing gameplay.") },
        { AkronFeatureKind.ProofRecorderGuard, new FeatureDefinition(AkronFeatureKind.ProofRecorderGuard, AkronStatus.GoldberryHardlistClean, "Proof recorder guard", "Warns when recording or replay buffering is not armed.") },
        { AkronFeatureKind.EndScreenHelper, new FeatureDefinition(AkronFeatureKind.EndScreenHelper, AkronStatus.GoldberryHardlistClean, "End screen helper", "Keeps end-screen capture settings visible and recorded.") },
        { AkronFeatureKind.PauseTracker, new FeatureDefinition(AkronFeatureKind.PauseTracker, AkronStatus.GoldberryHardlistClean, "Pause tracker", "Records pause counts and paused duration.") },
        { AkronFeatureKind.MapVersionStamp, new FeatureDefinition(AkronFeatureKind.MapVersionStamp, AkronStatus.GoldberryHardlistClean, "Map version stamp", "Adds map and loaded-module version metadata to exports.") },
        { AkronFeatureKind.GoldenStartHelper, new FeatureDefinition(AkronFeatureKind.GoldenStartHelper, AkronStatus.GoldberryHardlistClean, "Golden start helper", "Runs Celeste's first-room golden-start helper.") },
        { AkronFeatureKind.GoldenTransparency, new FeatureDefinition(AkronFeatureKind.GoldenTransparency, AkronStatus.RegularClean, "Golden transparency", "Changes golden berry/follower presentation without changing gameplay.") },
        { AkronFeatureKind.LagPauser, new FeatureDefinition(AkronFeatureKind.LagPauser, AkronStatus.GoldberryHardlistClean, "Lag pauser", "Pauses after a detected frame-time spike.") },
        { AkronFeatureKind.Logging, new FeatureDefinition(AkronFeatureKind.Logging, AkronStatus.RegularClean, "Logging", "Records local Akron diagnostics without changing gameplay state.") },
        { AkronFeatureKind.JournalSnapshotCompare, new FeatureDefinition(AkronFeatureKind.JournalSnapshotCompare, AkronStatus.GoldberryHardlistClean, "Journal snapshot / compare", "Exports and compares save-file journal stats.") }
    };

    private static readonly Dictionary<string, AkronStatus> UiLabelClassifications = new Dictionary<string, AkronStatus>(StringComparer.OrdinalIgnoreCase) {
        { "Safe Mode", AkronStatus.RegularClean },
        { "Pause Buffering", AkronStatus.Cheat },
        { "Autosave", AkronStatus.RegularClean },
        { "Confirm Restart", AkronStatus.GoldberryHardlistClean },
        { "Confirm Full Reset", AkronStatus.GoldberryHardlistClean },
        { "Freeze Gameplay", AkronStatus.Cheat },
        { "Smart StartPos", AkronStatus.Cheat },
        { "StartPos Slot", AkronStatus.Cheat },
        { "Respawn at StartPos", AkronStatus.Cheat },
        { "Instant Complete", AkronStatus.Cheat },
        { "Uncomplete Level", AkronStatus.Cheat },
        { "Unlock A-Sides", AkronStatus.Cheat },
        { "Unlock B-Sides", AkronStatus.Cheat },
        { "Unlock C-Sides", AkronStatus.Cheat },
        { "Unlock All Levels", AkronStatus.Cheat },
        { "Unlock Golden Berries", AkronStatus.Cheat },
        { "Unlock Paths", AkronStatus.Cheat },
        { "Obtain Room Berries", AkronStatus.Cheat },
        { "Obtain Chapter Berries", AkronStatus.Cheat },
        { "Berry Obtain Options", AkronStatus.RegularClean },
        { "Always Show Trail", AkronStatus.RegularClean },
        { "Madeline Colors", AkronStatus.RegularClean },
        { "Madeline Hair Length", AkronStatus.RegularClean },
        { "Madeline Effect Sync", AkronStatus.RegularClean },
        { "No Ghost Trail", AkronStatus.RegularClean },
        { "Reduced Visual Noise", AkronStatus.RegularClean },
        { "No Stamina Flash", AkronStatus.RegularClean },
        { "No Particles", AkronStatus.RegularClean },
        { "No Trails", AkronStatus.RegularClean },
        { "No Glitch", AkronStatus.RegularClean },
        { "No Anxiety", AkronStatus.RegularClean },
        { "No Distortion", AkronStatus.RegularClean },
        { "Hide Snow", AkronStatus.RegularClean },
        { "Hide Wind Snow", AkronStatus.RegularClean },
        { "Hide Waterfalls", AkronStatus.RegularClean },
        { "Hide Tentacles", AkronStatus.RegularClean },
        { "Hide Heat Distortion", AkronStatus.RegularClean },
        { "Stamina Bar", AkronStatus.Cheat },
        { "Dash Bar", AkronStatus.Cheat },
        { "Dash Number", AkronStatus.Cheat },
        { "Dash Stats", AkronStatus.RegularClean },
        { "Jump Stats", AkronStatus.RegularClean },
        { "Audio Splitter", AkronStatus.RegularClean },
        { "Allow Low Volume", AkronStatus.RegularClean },
        { "Export Room Times", AkronStatus.RegularClean },
        { "SRT Status", AkronStatus.RegularClean },
        { "SRT Slot", AkronStatus.RegularClean },
        { "SRT Capture State", AkronStatus.Cheat },
        { "SRT Restore State", AkronStatus.Cheat },
        { "SRT Clear State", AkronStatus.Cheat },
        { "SRT Room Time", AkronStatus.RegularClean },
        { "TAS Status", AkronStatus.RegularClean },
        { "Configured TAS File", AkronStatus.RegularClean },
        { "Play Configured TAS", AkronStatus.Cheat },
        { "ECD Status", AkronStatus.RegularClean },
        { "ECD Zoom Out", AkronStatus.Cheat },
        { "ECD Restore Zooming", AkronStatus.RegularClean },
        { "Previous Room", AkronStatus.RegularClean },
        { "Next Room", AkronStatus.RegularClean },
        { "Warp Selected Room", AkronStatus.Cheat },
        { "Warp To Previous In Order", AkronStatus.Cheat },
        { "Warp To Next In Order", AkronStatus.Cheat },
        { "Open Debug Map", AkronStatus.Cheat },
        { "Export Room Stats", AkronStatus.RegularClean },
        { "Death Stats", AkronStatus.GoldberryHardlistClean },
        { "Room", AkronStatus.RegularClean },
        { "Status", AkronStatus.RegularClean },
        { "Toasts", AkronStatus.RegularClean },
        { "Cheat Indicator", AkronStatus.RegularClean },
        { "Streamer Mode", AkronStatus.RegularClean },
        { "Input History", AkronStatus.GoldberryHardlistClean },
        { "Room Stat Tracker", AkronStatus.RegularClean },
        { "Room Timer", AkronStatus.RegularClean },
        { "Attempts", AkronStatus.GoldberryHardlistClean },
        { "No Short Numbers", AkronStatus.GoldberryHardlistClean },
        { "Visible", AkronStatus.RegularClean },
        { "Open Options", AkronStatus.GoldberryHardlistClean },
        { "Retry", AkronStatus.RegularClean },
        { "Reload Room", AkronStatus.Cheat },
        { "Reload Chapter", AkronStatus.RegularClean },
        { "Neutral Drop", AkronStatus.Cheat },
        { "Backboost", AkronStatus.Cheat },
        { "Fix Hitbox Pixels", AkronStatus.RegularClean },
        { "Show Hitboxes On Death", AkronStatus.RegularClean },
        { "Skip Cutscene", AkronStatus.RegularClean },
        { "Theme", AkronStatus.RegularClean },
        { "UI Scale", AkronStatus.RegularClean },
        { "Opacity", AkronStatus.RegularClean },
        { "Pause While Open", AkronStatus.RegularClean },
        { "Export Setup", AkronStatus.RegularClean },
        { "Import Setup", AkronStatus.RegularClean },
        { "Community Packs", AkronStatus.RegularClean },
        { "Room Capture", AkronStatus.GoldberryHardlistClean },
        { "Map Capture", AkronStatus.GoldberryHardlistClean },
        { "Search", AkronStatus.RegularClean },
        { "Search Autofocus", AkronStatus.RegularClean },
        { "Logging", AkronStatus.RegularClean },
        { "Extended Variants Master", AkronStatus.GoldberryHardlistClean },
        { "Extended Variants Randomizer", AkronStatus.Cheat },
        { "Reset Extended", AkronStatus.RegularClean },
        { "Reset Vanilla", AkronStatus.RegularClean },
        { "Start Recording", AkronStatus.GoldberryHardlistClean },
        { "Stop Recording", AkronStatus.GoldberryHardlistClean },
        { "Arm Completion Clips", AkronStatus.GoldberryHardlistClean },
        { "Flag Completion", AkronStatus.GoldberryHardlistClean },
        { "Build Clear Video", AkronStatus.GoldberryHardlistClean },
        { "Save Replay Buffer", AkronStatus.GoldberryHardlistClean },
        { "Framerate", AkronStatus.GoldberryHardlistClean },
        { "Endscreen Duration", AkronStatus.GoldberryHardlistClean },
        { "Bitrate", AkronStatus.GoldberryHardlistClean },
        { "Resolution X", AkronStatus.GoldberryHardlistClean },
        { "Resolution Y", AkronStatus.GoldberryHardlistClean },
        { "Hide Preview", AkronStatus.GoldberryHardlistClean },
        { "Codec", AkronStatus.GoldberryHardlistClean },
        { "Colorspace Args", AkronStatus.GoldberryHardlistClean },
        { "Output Folder", AkronStatus.GoldberryHardlistClean },
        { "Filename Template", AkronStatus.GoldberryHardlistClean },
        { "Replay Settings", AkronStatus.GoldberryHardlistClean },
        { "Output", AkronStatus.GoldberryHardlistClean },
        { "Audio", AkronStatus.GoldberryHardlistClean },
        { "Clip Triggers", AkronStatus.GoldberryHardlistClean },
        { "Presets", AkronStatus.GoldberryHardlistClean },
        { "CPU", AkronStatus.GoldberryHardlistClean },
        { "NVIDIA", AkronStatus.GoldberryHardlistClean },
        { "AMD", AkronStatus.GoldberryHardlistClean },
        { "Submission Mode", AkronStatus.GoldberryHardlistClean },
        { "Proof Recorder Guard", AkronStatus.GoldberryHardlistClean },
        { "End Screen Helper", AkronStatus.GoldberryHardlistClean },
        { "Pause Tracker", AkronStatus.GoldberryHardlistClean },
        { "Map Version Stamp", AkronStatus.GoldberryHardlistClean },
        { "Golden Start", AkronStatus.GoldberryHardlistClean },
        { "Golden Transparency", AkronStatus.RegularClean },
        { "Lag Pauser", AkronStatus.GoldberryHardlistClean },
        { "Journal Snapshot / Compare", AkronStatus.GoldberryHardlistClean }
    };

    private static readonly AkronStatus[] ClassificationByKind = BuildClassificationByKind();

    private static readonly Dictionary<string, AkronStatus> UiSuboptionClassifications = new Dictionary<string, AkronStatus>(StringComparer.OrdinalIgnoreCase) {
        { BuildUiSuboptionKey("Safe Mode", "Freeze deaths"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Safe Mode", "Freeze jumps"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Safe Mode", "Freeze best run"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Death Stats", "PB loss prompt"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Input History", "Input history"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Input History", "Rows"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Input History", "Pin on death"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Input History", "Show on death"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Room Stat Tracker", "Freeze mode"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Free Camera", "Freeze gameplay"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("FPS Bypass", "Target FPS"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Method"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Smooth Camera"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Objects"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Objects: Extrapolate"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Objects: Interpolate"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("FPS Bypass", "TAS mode"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("FPS Bypass", "Subpixel Madeline"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Smooth background"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Smooth foreground"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Hide edge gaps"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("FPS Bypass", "Nasty mode"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("TPS Bypass", "Target TPS"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Room Capture", "Freeze timers"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Map Capture", "Freeze timers"), AkronStatus.Cheat }
    };

    public static FeatureDefinition Get(AkronFeatureKind kind)
    {
        return Definitions[kind];
    }

    public static AkronStatus Classify(AkronFeatureKind kind)
    {
        int index = (int)kind;
        return index >= 0 && index < ClassificationByKind.Length
            ? ClassificationByKind[index]
            : Get(kind).Classification;
    }

    public static bool TryClassifyUiLabel(string label, out AkronStatus status)
    {
        return UiLabelClassifications.TryGetValue(label ?? string.Empty, out status);
    }

    public static bool TryClassifyUiSuboption(string parentLabel, string suboptionLabel, out AkronStatus status)
    {
        return UiSuboptionClassifications.TryGetValue(BuildUiSuboptionKey(parentLabel, suboptionLabel), out status);
    }

    private static string BuildUiSuboptionKey(string parentLabel, string suboptionLabel)
    {
        return (parentLabel ?? string.Empty).Trim() + "\n" + (suboptionLabel ?? string.Empty).Trim();
    }

    private static AkronStatus[] BuildClassificationByKind()
    {
        Array values = Enum.GetValues(typeof(AkronFeatureKind));
        int max = 0;
        foreach (AkronFeatureKind kind in values)
        {
            max = Math.Max(max, (int)kind);
        }

        AkronStatus[] classifications = new AkronStatus[max + 1];
        foreach (AkronFeatureKind kind in values)
        {
            classifications[(int)kind] = Definitions[kind].Classification;
        }

        return classifications;
    }
}
