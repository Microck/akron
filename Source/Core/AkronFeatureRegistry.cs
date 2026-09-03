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
        { AkronFeatureKind.DisablePlayback, new FeatureDefinition(AkronFeatureKind.DisablePlayback, AkronStatus.RegularClean, "Disable playback", "Suppresses map-placed playback tutorial ghosts, including their visuals and audio, during gameplay.") },
        { AkronFeatureKind.VisualTuning, new FeatureDefinition(AkronFeatureKind.VisualTuning, AkronStatus.RegularClean, "Visual tuning", "Adjusts lighting, bloom, and tint presentation without changing gameplay state.") },
        { AkronFeatureKind.GrabModeHotkey, new FeatureDefinition(AkronFeatureKind.GrabModeHotkey, AkronStatus.RegularClean, "Grab mode hotkey", "Changes a player control preference without changing gameplay state.") },
        { AkronFeatureKind.ScreenshotTool, new FeatureDefinition(AkronFeatureKind.ScreenshotTool, AkronStatus.GoldberryHardlistClean, "Screenshot tool", "Captures the current view for review and sharing.") },
        { AkronFeatureKind.RetryHotkey, new FeatureDefinition(AkronFeatureKind.RetryHotkey, AkronStatus.RegularClean, "Retry hotkey", "Ends the current attempt by forcing a death and respawn.") },
        { AkronFeatureKind.RoomReload, new FeatureDefinition(AkronFeatureKind.RoomReload, AkronStatus.Cheat, "Room reload", "Restarts the current room.") },
        { AkronFeatureKind.ChapterReload, new FeatureDefinition(AkronFeatureKind.ChapterReload, AkronStatus.RegularClean, "Chapter reload", "Restarts the current chapter through Celeste's loader.") },
        { AkronFeatureKind.DebugMapLauncher, new FeatureDefinition(AkronFeatureKind.DebugMapLauncher, AkronStatus.Cheat, "Debug map launcher", "Opens Celeste's debug map scene.") },
        { AkronFeatureKind.Savestates, new FeatureDefinition(AkronFeatureKind.Savestates, AkronStatus.Cheat, "Speedrun Tool slot bindings", "Captures or restores the active Speedrun Tool slot from Akron's bindings.") },
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
        { AkronFeatureKind.CustomHudLabels, new FeatureDefinition(AkronFeatureKind.CustomHudLabels, AkronStatus.Cheat, "Custom HUD labels", "Displays configurable local status labels that can expose player resources, position, or speed.") },
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
        { AkronFeatureKind.InternalRecorder, new FeatureDefinition(AkronFeatureKind.InternalRecorder, AkronStatus.GoldberryHardlistClean, "Internal recorder", "Captures local game frames for proof review without mutating gameplay state.") },
        { AkronFeatureKind.FastLookout, new FeatureDefinition(AkronFeatureKind.FastLookout, AkronStatus.Cheat, "Fast lookout", "Speeds up vanilla lookout camera movement only while the configured hold bind is pressed.") },
        { AkronFeatureKind.LevelEnterSkip, new FeatureDefinition(AkronFeatureKind.LevelEnterSkip, AkronStatus.RegularClean, "Level intro skip", "Skips repeated postcard and B-side intro waits through an explicit hold-confirm action.") },
        { AkronFeatureKind.DeathPbLossRestart, new FeatureDefinition(AkronFeatureKind.DeathPbLossRestart, AkronStatus.RegularClean, "PB loss restart", "Offers a restart prompt based on visible death-count state.") },
        { AkronFeatureKind.CameraOffset, new FeatureDefinition(AkronFeatureKind.CameraOffset, AkronStatus.Cheat, "Camera offset", "Changes the current level camera offset for map inspection.") },
        { AkronFeatureKind.CursorTools, new FeatureDefinition(AkronFeatureKind.CursorTools, AkronStatus.Cheat, "Cursor tools", "Temporarily enables cursor-driven map inspection tools while the configured hold bind is pressed.") },
        { AkronFeatureKind.CursorZoom, new FeatureDefinition(AkronFeatureKind.CursorZoom, AkronStatus.Cheat, "Cursor zoom", "Zooms the current level around the cursor for map inspection.") },
        { AkronFeatureKind.SubmissionMode, new FeatureDefinition(AkronFeatureKind.SubmissionMode, AkronStatus.GoldberryHardlistClean, "Submission mode", "Enables recording metadata defaults and related warning surfaces without changing gameplay.") },
        { AkronFeatureKind.ProofRecorderGuard, new FeatureDefinition(AkronFeatureKind.ProofRecorderGuard, AkronStatus.GoldberryHardlistClean, "Proof recorder guard", "Warns when recording or replay buffering is not armed.") },
        { AkronFeatureKind.EndScreenHelper, new FeatureDefinition(AkronFeatureKind.EndScreenHelper, AkronStatus.GoldberryHardlistClean, "End screen helper", "Keeps end-screen capture settings visible and recorded.") },
        { AkronFeatureKind.PauseTracker, new FeatureDefinition(AkronFeatureKind.PauseTracker, AkronStatus.GoldberryHardlistClean, "Pause tracker", "Records pause counts and paused duration.") },
        { AkronFeatureKind.MapVersionStamp, new FeatureDefinition(AkronFeatureKind.MapVersionStamp, AkronStatus.GoldberryHardlistClean, "Map version stamp", "Adds map and loaded-module version metadata to exports.") },
        { AkronFeatureKind.GoldenStartHelper, new FeatureDefinition(AkronFeatureKind.GoldenStartHelper, AkronStatus.GoldberryHardlistClean, "Golden start helper", "Runs Celeste's first-room golden-start helper.") },
        { AkronFeatureKind.GoldenTransparency, new FeatureDefinition(AkronFeatureKind.GoldenTransparency, AkronStatus.RegularClean, "Golden transparency", "Changes golden berry/follower presentation without changing gameplay.") },
        { AkronFeatureKind.LagPauser, new FeatureDefinition(AkronFeatureKind.LagPauser, AkronStatus.GoldberryHardlistClean, "Lag pauser", "Pauses after a detected frame-time spike.") },
        { AkronFeatureKind.Logging, new FeatureDefinition(AkronFeatureKind.Logging, AkronStatus.GoldberryHardlistClean, "Logging", "Records local Akron diagnostics without changing gameplay state.") },
        { AkronFeatureKind.JournalSnapshotCompare, new FeatureDefinition(AkronFeatureKind.JournalSnapshotCompare, AkronStatus.GoldberryHardlistClean, "Journal snapshot / compare", "Exports and compares save-file journal stats.") },
        { AkronFeatureKind.Backups, new FeatureDefinition(AkronFeatureKind.Backups, AkronStatus.GoldberryHardlistClean, "Backups", "Creates, lists, restores, and prunes local save backup files without changing live gameplay.") },
        { AkronFeatureKind.EntitySpawn, new FeatureDefinition(AkronFeatureKind.EntitySpawn, AkronStatus.Cheat, "Entity spawn", "Adds a held item the map did not place.") },
        { AkronFeatureKind.PauseBuffering, new FeatureDefinition(AkronFeatureKind.PauseBuffering, AkronStatus.Cheat, "Pause buffering", "Lets bound Akron actions fire while Celeste is paused.") },
        { AkronFeatureKind.CutsceneSkip, new FeatureDefinition(AkronFeatureKind.CutsceneSkip, AkronStatus.RegularClean, "Cutscene skip", "Runs the active cutscene's own skip callback on request.") },
        { AkronFeatureKind.MadelineColors, new FeatureDefinition(AkronFeatureKind.MadelineColors, AkronStatus.RegularClean, "Madeline colors", "Recolors Madeline's hair without changing gameplay.") },
        { AkronFeatureKind.AttemptsLabel, new FeatureDefinition(AkronFeatureKind.AttemptsLabel, AkronStatus.GoldberryHardlistClean, "Attempts label", "Displays the current map's attempt count without mutating play.") },
        { AkronFeatureKind.StatusLabels, new FeatureDefinition(AkronFeatureKind.StatusLabels, AkronStatus.RegularClean, "Status labels", "Displays overlay and attempt status text in the Akron HUD.") },
        { AkronFeatureKind.PracticeCounters, new FeatureDefinition(AkronFeatureKind.PracticeCounters, AkronStatus.RegularClean, "Dash and jump stats", "Displays local dash and jump counts in the Akron HUD.") },
        { AkronFeatureKind.RoomStatTracker, new FeatureDefinition(AkronFeatureKind.RoomStatTracker, AkronStatus.RegularClean, "Room stat tracker", "Displays per-room deaths, time, berries, and alive time in the Akron HUD.") },
        { AkronFeatureKind.CaptureCheatOptions, new FeatureDefinition(AkronFeatureKind.CaptureCheatOptions, AkronStatus.Cheat, "Capture cheat options", "Freezes level time or hides and uncollides Madeline while a capture runs.") },
        { AkronFeatureKind.Autosave, new FeatureDefinition(AkronFeatureKind.Autosave, AkronStatus.GoldberryHardlistClean, "Autosave", "Saves the active file at configured triggers without changing gameplay.") },
        { AkronFeatureKind.DeathHitboxes, new FeatureDefinition(AkronFeatureKind.DeathHitboxes, AkronStatus.RegularClean, "Death hitboxes", "Draws the hitboxes involved in the last death after it happened.") },
        { AkronFeatureKind.SoundVolumeOverride, new FeatureDefinition(AkronFeatureKind.SoundVolumeOverride, AkronStatus.RegularClean, "Sound volume overrides", "Changes the volume of selected sounds as an accessibility setting.") }
    };

    private static readonly FeatureDefinition?[] DefinitionByKind = BuildDefinitionByKind();
    private static readonly AkronStatus[] ClassificationByKind = BuildClassificationByKind();

    // Row classifications come from the row's feature kind, so a row cannot show a class its
    // feature never records. Popup checkboxes have no kind of their own, which is why this
    // table still exists: it names the checkboxes whose class differs from their parent row.
    // Anything listed here must record through a feature kind at the point of use.
    private static readonly Dictionary<string, AkronStatus> UiSuboptionClassifications = new Dictionary<string, AkronStatus>(StringComparer.OrdinalIgnoreCase) {
        { BuildUiSuboptionKey("Safe Mode", "Freeze deaths"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Safe Mode", "Freeze jumps"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Safe Mode", "Freeze best run"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Autosave", "Interval"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Minimum delay"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Level load"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Spawn update"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Respawn"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Pause"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Avoid gameplay"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Save settings"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Hide saving icon"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Autosave", "Save now"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "On launch"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "On close"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "On save"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "On chapter"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "Timed"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Triggers", "Interval"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Retention", "Max count"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Retention", "Max age"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Retention", "Max size MB"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Retention", "Keep at least"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Death Stats", "PB loss prompt"), AkronStatus.RegularClean },
        { BuildUiSuboptionKey("Input History", "Input history"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Input History", "Rows"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Input History", "Pin on death"), AkronStatus.GoldberryHardlistClean },
        { BuildUiSuboptionKey("Input History", "Show on death"), AkronStatus.GoldberryHardlistClean },
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
        { BuildUiSuboptionKey("Timescale", "Enabled"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Timescale", "Decrease"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Timescale", "Increase"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Timescale", "Reset"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Transition Speed", "Multiplier"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Transition Speed", "Speed"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Room Capture", "Freeze time"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Room Capture", "Noclip + hide Madeline"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Map Capture", "Freeze time"), AkronStatus.Cheat },
        { BuildUiSuboptionKey("Map Capture", "Noclip + hide Madeline"), AkronStatus.Cheat }
    };

    public static FeatureDefinition Get(AkronFeatureKind kind)
    {
        int index = (int)kind;
        if (index >= 0 && index < DefinitionByKind.Length && DefinitionByKind[index].HasValue)
        {
            return DefinitionByKind[index].Value;
        }

        return Definitions[kind];
    }

    public static AkronStatus Classify(AkronFeatureKind kind)
    {
        int index = (int)kind;
        return index >= 0 && index < ClassificationByKind.Length
            ? ClassificationByKind[index]
            : Get(kind).Classification;
    }

    public static bool TryClassifyUiSuboption(string parentLabel, string suboptionLabel, out AkronStatus status)
    {
        return UiSuboptionClassifications.TryGetValue(BuildUiSuboptionKey(parentLabel, suboptionLabel), out status);
    }

    private static string BuildUiSuboptionKey(string parentLabel, string suboptionLabel)
    {
        return (parentLabel ?? string.Empty).Trim() + "\n" + (suboptionLabel ?? string.Empty).Trim();
    }

    private static FeatureDefinition?[] BuildDefinitionByKind()
    {
        Array values = Enum.GetValues(typeof(AkronFeatureKind));
        int max = 0;
        foreach (AkronFeatureKind kind in values)
        {
            max = Math.Max(max, (int)kind);
        }

        FeatureDefinition?[] definitions = new FeatureDefinition?[max + 1];
        foreach (AkronFeatureKind kind in values)
        {
            definitions[(int)kind] = Definitions[kind];
        }

        return definitions;
    }

    private static AkronStatus[] BuildClassificationByKind()
    {
        AkronStatus[] classifications = new AkronStatus[DefinitionByKind.Length];
        for (int index = 0; index < DefinitionByKind.Length; index++)
        {
            if (DefinitionByKind[index].HasValue)
            {
                classifications[index] = DefinitionByKind[index].Value.Classification;
            }
        }

        return classifications;
    }
}
