using System;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    // Low-level command wrapper over many normal UI toggles. Prefer documenting
    // the concrete overlay row first; use this only for automation and debugging.
    [Command("akron_feature", "control Akron feature toggles: name [on|off|toggle|status]")]
    public static void Feature(string name = "", string action = "status") {
        string normalizedName = NormalizeToken(name);
        if (string.IsNullOrWhiteSpace(normalizedName)) {
            Log("usage: akron_feature <infinite-stamina|infinite-dash|auto-kill|auto-deafen|click-teleport|cursor-zoom|noclip|hazard-accuracy|free-camera|fps-bypass|tps-bypass|invincibility|air-jumps|input-viewer|input-history|inputs-per-second|stamina-bar|dash-bar|speed-number|room-labels|room-timer|room-stat-tracker|freeze-timer-paused|fast-lookout|skip-postcards|skip-intro|death-pb-loss-prompt|dash-redirect|death-stats|total-attempts|status-labels|madeline-colors|madeline-hair-length|madeline-effect-sync|hide-player|no-death-effect|no-death-wipe|fix-hitbox-pixels|no-freeze-frames|ground-refills|reduced-visual-noise|light-level|bloom-level|screen-tint|no-particles|no-trails|no-glitch|no-anxiety|no-distortion|hide-snow|hide-wind-snow|hide-waterfalls|hide-tentacles|hide-heat-distortion|streamer-mode|proof-mode|submission-mode|proof-recorder-guard|end-screen-helper|pause-tracker|map-version-stamp|golden-transparency|lag-pauser|logging> [on|off|toggle|status]");
            return;
        }

        bool handled = normalizedName switch {
            "infinitestamina" => SetFeatureToggle(action, AkronFeatureKind.InfiniteStamina, () => AkronModule.Settings.InfiniteStamina, value => AkronModule.Settings.InfiniteStamina = value, "infinite-stamina"),
            "infinitedash" => SetFeatureToggle(action, AkronFeatureKind.InfiniteDash, () => AkronModule.Settings.InfiniteDash, value => AkronModule.Settings.InfiniteDash = value, "infinite-dash"),
            "autokill" => SetFeatureToggle(action, AkronFeatureKind.AutoKill, () => AkronModule.Settings.AutoKill, value => AkronModule.Settings.AutoKill = value, "auto-kill"),
            "autodeafen" => SetFeatureToggle(action, AkronFeatureKind.AutoDeafen, () => AkronModule.Settings.AutoDeafen, value => {
                AkronModule.Settings.AutoDeafen = value;
                if (!value) {
                    AkronActions.RestoreAutoDeafen();
                }
            }, "auto-deafen"),
            "clickteleport" => SetFeatureToggle(action, AkronFeatureKind.ClickTeleport, () => AkronModule.Settings.ClickTeleport, value => AkronModule.Settings.ClickTeleport = value, "click-teleport"),
            "cursorzoom" => SetFeatureToggle(action, AkronFeatureKind.CursorZoom, () => AkronModule.Settings.CursorZoom, value => {
                AkronModule.Settings.CursorZoom = value;
                if (!value) {
                    AkronModule.ResetCursorZoom(Engine.Scene as Level);
                }
            }, "cursor-zoom"),
            "noclip" => SetFeatureToggle(action, AkronFeatureKind.Noclip, () => AkronModule.Settings.Noclip, value => AkronModule.Settings.Noclip = value, "noclip"),
            "hazardaccuracy" => SetFeatureToggle(action, AkronFeatureKind.HazardAccuracy, () => AkronModule.Settings.NoclipAccuracy, value => {
                AkronModule.Settings.NoclipAccuracy = value;
                if (!value) {
                    AkronModule.ResetNoclipAccuracy();
                }
            }, "hazard-accuracy"),
            "freecamera" => SetFeatureToggle(action, AkronFeatureKind.FreeCamera, () => AkronModule.Settings.FreeCamera, value => AkronModule.Settings.FreeCamera = value, "free-camera"),
            "fpsbypass" => AkronMotionSmoothingInterop.Loaded
                ? SetFeatureToggle(action, AkronFeatureKind.FpsBypass, () => AkronModule.Settings.FpsBypass, value => AkronModule.Settings.FpsBypass = value, "fps-bypass")
                : LogMissingExternalFeature("motion-smoothing"),
            "tpsbypass" => AkronMotionSmoothingInterop.Loaded
                ? SetFeatureToggle(action, AkronFeatureKind.TpsBypass, () => AkronModule.Settings.TpsBypass, value => AkronModule.Settings.TpsBypass = value, "tps-bypass")
                : LogMissingExternalFeature("motion-smoothing"),
            "invincibility" => SetFeatureToggle(action, AkronFeatureKind.Invincibility, () => AkronModule.Settings.Invincibility, value => AkronModule.Settings.Invincibility = value, "invincibility"),
            "airjumps" => SetFeatureToggle(action, AkronFeatureKind.MovementStatMutation, () => AkronModule.Settings.JumpHack, value => AkronModule.Settings.JumpHack = value, "air-jumps"),
            "inputviewer" => SetFeatureToggle(action, AkronFeatureKind.InputViewer, () => AkronModule.Settings.InputViewer, value => AkronModule.Settings.InputViewer = value, "input-viewer"),
            "inputhistory" => SetFeatureToggle(action, AkronFeatureKind.InputHistory, () => AkronModule.Settings.InputHistoryPanel, value => AkronModule.Settings.InputHistoryPanel = value, "input-history"),
            "inputspersecond" or "ips" => SetFeatureToggle(action, AkronFeatureKind.InputsPerSecondCounter, () => AkronModule.Settings.InputsPerSecondCounter, value => AkronModule.Settings.InputsPerSecondCounter = value, "inputs-per-second"),
            "resourcebars" => SetFeatureToggle(action, AkronFeatureKind.ResourceBars, () => AkronModule.Settings.StaminaBar || AkronModule.Settings.DashBar, value => { AkronModule.Settings.StaminaBar = value; AkronModule.Settings.DashBar = value; }, "resource-bars"),
            "staminabar" => SetFeatureToggle(action, AkronFeatureKind.ResourceBars, () => AkronModule.Settings.StaminaBar, value => AkronModule.Settings.StaminaBar = value, "stamina-bar"),
            "dashbar" => SetFeatureToggle(action, AkronFeatureKind.ResourceBars, () => AkronModule.Settings.DashBar, value => AkronModule.Settings.DashBar = value, "dash-bar"),
            "dashpips" => SetFeatureToggle(action, AkronFeatureKind.ResourceBars, () => AkronModule.Settings.DashBar, value => AkronModule.Settings.DashBar = value, "dash-bar"),
            "speednumber" => SetFeatureToggle(action, AkronFeatureKind.SpeedNumber, () => AkronModule.Settings.SpeedNumber, value => AkronModule.Settings.SpeedNumber = value, "speed-number"),
            "labels" or "labelsvisible" or "labelsystem" => SetPlainToggle(action, () => AkronModule.Settings.LabelSystemVisible, value => AkronModule.Settings.LabelSystemVisible = value, "labels-visible"),
            "roomlabels" => SetPlainToggle(action, () => AkronModule.Settings.RoomLabels, value => AkronModule.Settings.RoomLabels = value, "room-labels"),
            "roomtimer" => SetFeatureToggle(action, AkronFeatureKind.RoomTimer, () => AkronModule.Settings.RoomTimerWidget, value => AkronModule.Settings.RoomTimerWidget = value, "room-timer"),
            "roomstattracker" => SetFeatureToggle(action, AkronFeatureKind.RoomTimer, () => AkronModule.Settings.RoomStatTracker, value => AkronModule.Settings.RoomStatTracker = value, "room-stat-tracker"),
            "freezetimerpaused" => SetFeatureToggle(action, AkronFeatureKind.PauseTimerFreeze, () => AkronModule.Settings.FreezeTimerWhilePaused, value => AkronModule.Settings.FreezeTimerWhilePaused = value, "freeze-timer-paused"),
            "fastlookout" => SetFeatureToggle(action, AkronFeatureKind.FastLookout, () => AkronModule.Settings.FastLookout, value => AkronModule.Settings.FastLookout = value, "fast-lookout"),
            "skippostcards" => SetFeatureToggle(action, AkronFeatureKind.LevelEnterSkip, () => AkronModule.Settings.SkipPostcards, value => AkronModule.Settings.SkipPostcards = value, "skip-postcards"),
            "skipintro" => SetFeatureToggle(action, AkronFeatureKind.LevelEnterSkip, () => AkronModule.Settings.SkipIntro, value => AkronModule.Settings.SkipIntro = value, "skip-intro"),
            "deathpblossprompt" => SetFeatureToggle(action, AkronFeatureKind.DeathPbLossRestart, () => AkronModule.Settings.DeathPbLossPrompt, value => AkronModule.Settings.DeathPbLossPrompt = value, "death-pb-loss-prompt"),
            "dashredirect" => SetFeatureToggle(action, AkronFeatureKind.InputAssistShortcut, () => AkronModule.Settings.DashRedirectEnabled, value => AkronModule.Settings.DashRedirectEnabled = value, "dash-redirect"),
            "deathstats" => SetFeatureToggle(action, AkronFeatureKind.DeathStats, () => AkronModule.Settings.DeathStatsWidget, value => AkronModule.Settings.DeathStatsWidget = value, "death-stats"),
            "totalattempts" => SetFeatureToggle(action, AkronFeatureKind.DeathStats, () => AkronModule.Settings.TotalAttemptsWidget, value => AkronModule.Settings.TotalAttemptsWidget = value, "total-attempts"),
            "statuslabels" => SetPlainToggle(action, () => AkronModule.Settings.StatusLabelsWidget, value => AkronModule.Settings.StatusLabelsWidget = value, "status-labels"),
            "madelinecolors" or "haircolors" => SetPlainToggle(action, () => AkronModule.Settings.MadelineColors, value => AkronModule.Settings.MadelineColors = value, "madeline-colors"),
            "madelinehairlength" or "hairlength" => SetFeatureToggle(action, AkronFeatureKind.MadelineHairLength, () => AkronModule.Settings.MadelineHairLength, value => AkronModule.Settings.MadelineHairLength = value, "madeline-hair-length"),
            "madelineeffectsync" or "effectsync" => SetFeatureToggle(action, AkronFeatureKind.MadelineEffectSync, () => AkronModule.Settings.MadelineEffectSync, value => AkronModule.Settings.MadelineEffectSync = value, "madeline-effect-sync"),
            "hideplayer" => SetFeatureToggle(action, AkronFeatureKind.HidePlayer, () => AkronModule.Settings.HidePlayer, value => AkronModule.Settings.HidePlayer = value, "hide-player"),
            "nodeatheffect" => SetFeatureToggle(action, AkronFeatureKind.DeathVisuals, () => AkronModule.Settings.NoDeathEffect, value => AkronModule.Settings.NoDeathEffect = value, "no-death-effect"),
            "nodeathwipe" => SetFeatureToggle(action, AkronFeatureKind.DeathVisuals, () => AkronModule.Settings.NoDeathWipe, value => AkronModule.Settings.NoDeathWipe = value, "no-death-wipe"),
            "fixhitboxpixels" or "hitboxpixels" => SetPlainToggle(action, () => AkronModule.Settings.FixHitboxPixels, value => AkronModule.Settings.FixHitboxPixels = value, "fix-hitbox-pixels"),
            "nofreezeframes" => SetFeatureToggle(action, AkronFeatureKind.FreezeFrames, () => AkronModule.Settings.NoFreezeFrames, value => AkronModule.Settings.NoFreezeFrames = value, "no-freeze-frames"),
            "groundrefills" => SetFeatureToggle(action, AkronFeatureKind.GroundRefillRules, () => AkronModule.Settings.GroundRefillRules, value => AkronModule.Settings.GroundRefillRules = value, "ground-refills"),
            "reducedvisualnoise" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.ReducedVisualNoise, value => AkronModule.Settings.ReducedVisualNoise = value, "reduced-visual-noise"),
            "lightlevel" => SetFeatureToggle(action, AkronFeatureKind.VisualTuning, () => AkronModule.Settings.LightLevel, value => AkronModule.Settings.LightLevel = value, "light-level"),
            "bloomlevel" => SetFeatureToggle(action, AkronFeatureKind.VisualTuning, () => AkronModule.Settings.BloomLevel, value => AkronModule.Settings.BloomLevel = value, "bloom-level"),
            "screentint" => SetFeatureToggle(action, AkronFeatureKind.VisualTuning, () => AkronModule.Settings.ScreenTint, value => AkronModule.Settings.ScreenTint = value, "screen-tint"),
            "noparticles" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoParticles, AkronModule.Settings.SetNoParticles, "no-particles"),
            "notrails" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoTrails, AkronModule.Settings.SetNoTrails, "no-trails"),
            "noglitch" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoGlitch, AkronModule.Settings.SetNoGlitch, "no-glitch"),
            "noanxiety" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoAnxiety, AkronModule.Settings.SetNoAnxiety, "no-anxiety"),
            "nodistortion" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoDistortion, AkronModule.Settings.SetNoDistortion, "no-distortion"),
            "hidesnow" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideSnow, value => AkronModule.Settings.HideSnow = value, "hide-snow"),
            "hidewindsnow" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideWindSnow, value => AkronModule.Settings.HideWindSnow = value, "hide-wind-snow"),
            "hidewaterfalls" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideWaterfalls, value => AkronModule.Settings.HideWaterfalls = value, "hide-waterfalls"),
            "hidetentacles" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideTentacles, value => AkronModule.Settings.HideTentacles = value, "hide-tentacles"),
            "hideheatdistortion" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideHeatDistortion, value => AkronModule.Settings.HideHeatDistortion = value, "hide-heat-distortion"),
            "streamermode" => SetPlainToggle(action, () => AkronModule.Settings.StreamerMode, value => AkronModule.Settings.StreamerMode = value, "streamer-mode"),
            "proofmode" => SetPlainToggle(action, () => AkronModule.Settings.ProofModeOverlay, value => AkronModule.Settings.ProofModeOverlay = value, "proof-mode"),
            "submissionmode" => SetFeatureToggle(action, AkronFeatureKind.SubmissionMode, () => AkronModule.Settings.SubmissionMode, SetSubmissionMode, "submission-mode"),
            "proofrecorderguard" => SetFeatureToggle(action, AkronFeatureKind.ProofRecorderGuard, () => AkronModule.Settings.ProofRecorderGuard, value => AkronModule.Settings.ProofRecorderGuard = value, "proof-recorder-guard"),
            "endscreenhelper" => SetFeatureToggle(action, AkronFeatureKind.EndScreenHelper, () => AkronModule.Settings.EndScreenHelper, value => AkronModule.Settings.EndScreenHelper = value, "end-screen-helper"),
            "pausetracker" => SetFeatureToggle(action, AkronFeatureKind.PauseTracker, () => AkronModule.Settings.PauseTracker, value => AkronModule.Settings.PauseTracker = value, "pause-tracker"),
            "mapversionstamp" => SetFeatureToggle(action, AkronFeatureKind.MapVersionStamp, () => AkronModule.Settings.MapVersionStamp, value => AkronModule.Settings.MapVersionStamp = value, "map-version-stamp"),
            "goldentransparency" => SetFeatureToggle(action, AkronFeatureKind.GoldenTransparency, () => AkronModule.Settings.GoldenTransparency, value => AkronModule.Settings.GoldenTransparency = value, "golden-transparency"),
            "lagpauser" => SetFeatureToggle(action, AkronFeatureKind.LagPauser, () => AkronModule.Settings.LagPauser, value => AkronModule.Settings.LagPauser = value, "lag-pauser"),
            "logging" or "logs" => SetPlainToggle(action, () => AkronModule.Settings.Logging, value => {
                AkronModule.Settings.Logging = value;
                AkronLog.LogSettingsChanged("enabled=" + value.ToString().ToLowerInvariant());
            }, "logging"),
            _ => false
        };

        if (!handled) {
            Log("unknown feature: " + name);
        }
    }

    private static bool SetFeatureToggle(string action, AkronFeatureKind feature, Func<bool> getter, Action<bool> setter, string label) {
        return SetToggle(action, () => {
            if (!AkronModule.TryUse(feature)) {
                return false;
            }

            setter(true);
            return true;
        }, () => setter(false), getter, label);
    }

    private static bool LogMissingExternalFeature(string label) {
        Log(label + ": missing");
        return true;
    }

    private static bool SetPlainToggle(string action, Func<bool> getter, Action<bool> setter, string label) {
        return SetToggle(action, () => {
            setter(true);
            return true;
        }, () => setter(false), getter, label);
    }

    private static void SetSubmissionMode(bool enabled) {
        AkronModule.Settings.SubmissionMode = enabled;
        if (!enabled) {
            return;
        }

        // Submission mode is a convenience bundle for proof-safe guardrails. Each
        // child feature stays independently toggleable after the bundle is enabled.
        AkronModule.Settings.ProofModeOverlay = true;
        AkronModule.Settings.ProofRecorderGuard = true;
        AkronModule.Settings.EndScreenHelper = true;
        AkronModule.Settings.PauseTracker = true;
        AkronModule.Settings.MapVersionStamp = true;
    }

    private static bool SetToggle(string action, Func<bool> enable, Action disable, Func<bool> getter, string label) {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                break;
            case "on":
            case "true":
                if (!getter() && !enable()) {
                    Log(label + ": blocked");
                    return true;
                }
                break;
            case "off":
            case "false":
                disable();
                break;
            case "toggle":
                if (getter()) {
                    disable();
                } else if (!enable()) {
                    Log(label + ": blocked");
                    return true;
                }
                break;
            default:
                Log("unknown feature action: " + action);
                return true;
        }

        Log(label + ": " + getter().ToString().ToLowerInvariant());
        return true;
    }
}
