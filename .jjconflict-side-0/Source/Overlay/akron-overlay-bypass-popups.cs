using Celeste;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawTransitionSpeedPopupControls(string popupId) {
        DrawFloatStepperRow(
            "Speed",
            () => AkronModule.Settings.TransitionSpeedMultiplier,
            value => AkronModule.Settings.TransitionSpeedMultiplier = AkronModuleSettings.ClampTransitionSpeedMultiplier(value),
            -0.1f,
            0.1f,
            0.1f,
            3f,
            "%.1f",
            popupId,
            "Multiplier for Celeste's next room-transition duration.");
    }

    private void DrawFrameStepperPopupControls(string popupId) {
        if (AkronModule.Session == null) {
            ImGui.TextUnformatted("Unavailable outside a save session.");
            return;
        }

        if (ImGui.Button("Step once##" + popupId, new NumericsVector2(112f, 0f)) && AkronModule.Session.FreezeGameplay) {
            AkronModule.Session.StepFrameRequested = true;
        }
        DrawPopupTooltip("Advance one frame. Only works while Freeze Gameplay is on.");

        bool repeat = AkronModule.Settings.StepHoldRepeat;
        if (ImGui.Checkbox("Hold key repeats##" + popupId, ref repeat)) {
            AkronModule.Settings.StepHoldRepeat = repeat;
        }
        DrawPopupTooltip("When enabled, holding the step key advances frames repeatedly.");

        DrawIntStepperRow(
            "Delay",
            () => AkronModule.Settings.StepHoldDelayFrames,
            value => AkronModule.Settings.StepHoldDelayFrames = Calc.Clamp(value, 1, 120),
            -1,
            1,
            1,
            120,
            popupId,
            "Frames to wait before hold-repeat starts.");

        DrawIntStepperRow(
            "Every",
            () => AkronModule.Settings.StepHoldIntervalFrames,
            value => AkronModule.Settings.StepHoldIntervalFrames = Calc.Clamp(value, 1, 60),
            -1,
            1,
            1,
            60,
            popupId,
            "Frames between repeated steps after the delay.");
    }

    private void DrawRespawnTimePopupControls(string popupId) {
        DrawFloatStepperRow(
            "Seconds",
            () => AkronModule.Settings.RespawnTimeSeconds,
            value => AkronModule.Settings.RespawnTimeSeconds = AkronModuleSettings.ClampRespawnTimeSeconds(value),
            -0.1f,
            0.1f,
            0.1f,
            10f,
            "%.1f",
            popupId,
            "Total time from death to forced respawn when Respawn Time is enabled.");

        bool ignore = AkronModule.Settings.RespawnTimeIgnoreSpeedhack;
        if (ImGui.Checkbox("Ignore speedhack##" + popupId, ref ignore)) {
            AkronModule.Settings.RespawnTimeIgnoreSpeedhack = ignore;
        }
        DrawPopupTooltip("Use real elapsed time so Akron timescale does not stretch or shrink the respawn delay.");
    }

    private void DrawPauseCountdownPopupControls(string popupId) {
        bool hideTint = AkronModule.Settings.PauseCountdownHidePauseTint;
        if (ImGui.Checkbox("Hide pause tint##" + popupId, ref hideTint)) {
            AkronModule.Settings.PauseCountdownHidePauseTint = hideTint;
        }
        DrawPopupTooltip("Remove Celeste's leftover pause darkening while the countdown is holding gameplay.");

        DrawFloatStepperRow(
            "Seconds",
            () => AkronModule.Settings.PauseCountdownSeconds,
            value => AkronModule.Settings.PauseCountdownSeconds = AkronModuleSettings.ClampPauseCountdownSeconds(value),
            -0.1f,
            0.1f,
            0.1f,
            15f,
            "%.1f",
            popupId,
            "Time to hold gameplay after the pause menu closes.");
    }

    private void DrawFastLookoutPopupControls(string popupId) {
        DrawIntStepperRow(
            "Multiplier",
            () => AkronModule.Settings.FastLookoutMultiplier,
            value => AkronModule.Settings.FastLookoutMultiplier = AkronModuleSettings.ClampFastLookoutMultiplier(value),
            -1,
            1,
            1,
            10,
            popupId,
            "Lookout camera speed multiplier while Fast Lookout is enabled.");
    }

    private void DrawNoDeathWipePopupControls(string popupId) {
        DrawNoDeathWipeModeChoice("Death only", AkronNoDeathWipeMode.DeathOnly, popupId);
        DrawNoDeathWipeModeChoice("All wipes", AkronNoDeathWipeMode.AllWipes, popupId);

        bool runCallbacks = AkronModule.Settings.NoDeathWipeRunCallbacks;
        if (ImGui.Checkbox("Run callbacks##" + popupId, ref runCallbacks)) {
            AkronModule.Settings.NoDeathWipeRunCallbacks = runCallbacks;
        }
        DrawPopupTooltip("Run the wipe completion action even when Akron suppresses the visual wipe. Leave this on unless a map-specific test requires blocking callbacks too.");
    }

    private void DrawNoDeathWipeModeChoice(string label, AkronNoDeathWipeMode mode, string popupId) {
        bool selected = AkronModule.Settings.NoDeathWipeMode == mode;
        if (ImGui.RadioButton(label + "##death-wipe-" + popupId, selected)) {
            AkronModule.Settings.NoDeathWipeMode = mode;
        }
        DrawPopupTooltip(mode == AkronNoDeathWipeMode.AllWipes
            ? "Suppress every AreaData screen wipe while enabled."
            : "Suppress wipes only while a level is in the death-body/dead-player window.");
    }

    private static string FormatNoDeathWipeMode(AkronNoDeathWipeMode mode) {
        return mode == AkronNoDeathWipeMode.AllWipes ? "All wipes" : "Death only";
    }

    private void DrawAllowLowVolumePopupControls(string popupId) {
        DrawFloatValueRow(
            "Music",
            () => AkronModule.Settings.LowVolumeMusic,
            AkronActions.SetLowVolumeMusic,
            -0.1f,
            0.1f,
            0f,
            10f,
            "%.1f",
            popupId,
            "Music level applied while Allow Low Volume is enabled. Decimals are allowed: 0.5 is half of Celeste's normal 1 step.");

        DrawFloatValueRow(
            "SFX",
            () => AkronModule.Settings.LowVolumeSfx,
            AkronActions.SetLowVolumeSfx,
            -0.1f,
            0.1f,
            0f,
            10f,
            "%.1f",
            popupId,
            "Sound-effects level applied while Allow Low Volume is enabled. Decimals are allowed: 0.5 is half of Celeste's normal 1 step.");

        ImGui.TextUnformatted(Settings.Instance == null
            ? "Current: unavailable"
            : "Current: music " + (Audio.MusicVolume * 10f).ToString("0.0") + " / SFX " + (Audio.SfxVolume * 10f).ToString("0.0"));
    }

    private void DrawAudioSpeedPopupControls(string popupId) {
        if (ImGui.Button("Policy: " + AkronModule.Settings.AudioSpeedPolicy + "##" + popupId)) {
            AkronModule.Settings.AudioSpeedPolicy = NextAudioSpeedPolicy(AkronModule.Settings.AudioSpeedPolicy);
        }
        DrawPopupTooltip("Normal leaves audio at 1x, Sync follows Akron timescale, Independent uses the multiplier below.");

        DrawFloatValueRow(
            "Speed",
            () => AkronModule.Settings.AudioSpeedMultiplier,
            value => AkronModule.Settings.AudioSpeedMultiplier = AkronModuleSettings.ClampAudioMultiplier(value),
            -0.1f,
            0.1f,
            0.1f,
            4f,
            "%.1f",
            popupId,
            "Independent audio speed multiplier.");
    }

    private void DrawPitchShiftPopupControls(string popupId) {
        if (ImGui.Button("Policy: " + AkronModule.Settings.PitchShiftPolicy + "##" + popupId)) {
            AkronModule.Settings.PitchShiftPolicy = NextPitchPolicy(AkronModule.Settings.PitchShiftPolicy);
        }
        DrawPopupTooltip("Preserve keeps pitch normal, Follow Speed tracks audio speed, Independent uses the multiplier below.");

        DrawFloatValueRow(
            "Pitch",
            () => AkronModule.Settings.PitchShiftMultiplier,
            value => AkronModule.Settings.PitchShiftMultiplier = AkronModuleSettings.ClampAudioMultiplier(value),
            -0.1f,
            0.1f,
            0.1f,
            4f,
            "%.1f",
            popupId,
            "Independent pitch multiplier.");
    }

    private void DrawFpsBypassPopupControls(string popupId) {
        DrawIntStepperRow(
            "Target FPS",
            () => AkronModule.Settings.FpsBypassTarget,
            value => AkronModule.Settings.FpsBypassTarget = AkronModuleSettings.ClampFpsTarget(value),
            -60,
            60,
            60,
            480,
            popupId,
            "Draw target while FPS Bypass is enabled. Interval mode rounds to a clean multiple of TPS.");

        if (ImGui.Button("Method: " + AkronModule.Settings.FrameBypassMethod + "##" + popupId)) {
            AkronModule.Settings.FrameBypassMethod = NextFrameIncreaseMethod(AkronModule.Settings.FrameBypassMethod);
        }
        DrawPopupTooltip("Interval keeps render cadence aligned to physics ticks. Dynamic allows arbitrary FPS but is riskier for code that hooks the main tick.");

        if (ImGui.Button("Smooth Camera: " + FormatCameraSmoothing(AkronModule.Settings.FrameBypassCameraSmoothing) + "##" + popupId)) {
            AkronModule.Settings.FrameBypassCameraSmoothing = NextCameraSmoothing(AkronModule.Settings.FrameBypassCameraSmoothing);
        }
        DrawPopupTooltip("Fancy is highest quality, Fast is cheaper but can jitter backgrounds, Off leaves the camera pixel-locked.");

        if (ImGui.Button("Objects: " + AkronModule.Settings.FrameBypassObjectSmoothing + "##" + popupId)) {
            AkronModule.Settings.FrameBypassObjectSmoothing = NextObjectSmoothing(AkronModule.Settings.FrameBypassObjectSmoothing);
        }
        DrawPopupTooltip("Extrapolate predicts between physics frames. Interpolate uses prior frames and can add 1-2 frames of delay.");

        bool tasMode = AkronModule.Settings.FrameBypassTasMode;
        if (ImGui.Checkbox("TAS mode##" + popupId, ref tasMode)) {
            AkronModule.Settings.FrameBypassTasMode = tasMode;
        }
        DrawPopupTooltip("Keeps overworld updates locked for TAS compatibility while level gameplay remains controlled by TPS Bypass.");

        bool subpixelMadeline = AkronModule.Settings.FrameBypassSubpixelMadeline;
        if (ImGui.Checkbox("Subpixel Madeline##" + popupId, ref subpixelMadeline)) {
            AkronModule.Settings.FrameBypassSubpixelMadeline = subpixelMadeline;
        }
        DrawPopupTooltip("Reference option for drawing Madeline and held items at subpixel render positions.");

        bool smoothBackground = AkronModule.Settings.FrameBypassSmoothBackground;
        if (ImGui.Checkbox("Smooth background##" + popupId, ref smoothBackground)) {
            AkronModule.Settings.FrameBypassSmoothBackground = smoothBackground;
        }
        DrawPopupTooltip("Reference option for high-resolution background compositing in Fancy camera smoothing.");

        bool smoothForeground = AkronModule.Settings.FrameBypassSmoothForeground;
        if (ImGui.Checkbox("Smooth foreground##" + popupId, ref smoothForeground)) {
            AkronModule.Settings.FrameBypassSmoothForeground = smoothForeground;
        }
        DrawPopupTooltip("Reference option for high-resolution foreground compositing in Fancy camera smoothing.");

        bool hideEdges = AkronModule.Settings.FrameBypassHideStretchedEdges;
        if (ImGui.Checkbox("Hide edge gaps##" + popupId, ref hideEdges)) {
            AkronModule.Settings.FrameBypassHideStretchedEdges = hideEdges;
        }
        DrawPopupTooltip("Reference option: slightly zooms the level to hide gaps introduced by fractional camera offsets.");

        bool sillyMode = AkronModule.Settings.FrameBypassSillyMode;
        if (ImGui.Checkbox("Silly mode##" + popupId, ref sillyMode)) {
            AkronModule.Settings.FrameBypassSillyMode = sillyMode;
        }
        DrawPopupTooltip("Apply an experimental smoothing preset to FPS Bypass rendering.");
    }

    private void DrawTpsBypassPopupControls(string popupId) {
        DrawIntStepperRow(
            "Target TPS",
            () => AkronModule.Settings.TpsBypassTarget,
            value => AkronModule.Settings.TpsBypassTarget = AkronModuleSettings.ClampTpsTarget(value),
            -10,
            10,
            30,
            480,
            popupId,
            "Simulation update target while TPS Bypass is enabled. Unlike FPS Bypass, this changes physics cadence.");
    }

    private void DrawSafeModePopupControls(string popupId) {
        bool freezeAttempts = AkronModule.Settings.SafeModeFreezeAttempts;
        if (ImGui.Checkbox("Freeze deaths##" + popupId, ref freezeAttempts)) {
            AkronModule.Settings.SafeModeFreezeAttempts = freezeAttempts;
        }
        DrawPopupTooltip("Restore the current save slot's death counter while Safe Mode is active.", "Freeze deaths");

        bool freezeJumps = AkronModule.Settings.SafeModeFreezeJumps;
        if (ImGui.Checkbox("Freeze jumps##" + popupId, ref freezeJumps)) {
            AkronModule.Settings.SafeModeFreezeJumps = freezeJumps;
        }
        DrawPopupTooltip("Restore the current save slot's jump counter while Safe Mode is active.", "Freeze jumps");

        bool freezeBest = AkronModule.Settings.SafeModeFreezeBestRun;
        if (ImGui.Checkbox("Freeze best run##" + popupId, ref freezeBest)) {
            AkronModule.Settings.SafeModeFreezeBestRun = freezeBest;
        }
        DrawPopupTooltip("Restore current best-time fields while Safe Mode is active.", "Freeze best run");
    }
}
