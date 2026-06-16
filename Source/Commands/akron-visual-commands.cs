using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_noclip", "control fly noclip: on|off|status|speed <n>|float-speed <n>|draw-on-top <on|off>|hide-player <on|off>")]
    public static void Noclip(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.Noclip)) {
                    Log("noclip: blocked");
                    return;
                }
                AkronModule.Settings.Noclip = true;
                break;
            case "off":
                AkronModule.Settings.Noclip = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.Noclip && !AkronModule.TryUse(AkronFeatureKind.Noclip)) {
                    Log("noclip: blocked");
                    return;
                }
                AkronModule.Settings.Noclip = !AkronModule.Settings.Noclip;
                break;
            case "speed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int speed)) {
                    Log("invalid noclip speed: " + value);
                    return;
                }
                AkronModule.Settings.NoclipSpeed = AkronModuleSettings.ClampNoclipSpeed(speed);
                break;
            case "floatspeed":
            case "slowspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int floatSpeed)) {
                    Log("invalid noclip float-speed: " + value);
                    return;
                }
                AkronModule.Settings.NoclipFloatSpeed = AkronModuleSettings.ClampNoclipFloatSpeed(floatSpeed);
                break;
            case "drawontop":
            case "ontop":
                if (!TryParseBoolean(value, out bool drawOnTop)) {
                    Log("invalid noclip draw-on-top: " + value);
                    return;
                }
                AkronModule.Settings.NoclipDrawOnTop = drawOnTop;
                break;
            case "hideplayer":
            case "invisible":
                if (!TryParseBoolean(value, out bool hidePlayer)) {
                    Log("invalid noclip hide-player: " + value);
                    return;
                }
                AkronModule.Settings.NoclipHidePlayer = hidePlayer;
                break;
            default:
                Log("unknown noclip action: " + action);
                return;
        }

        Log("noclip: " + AkronModule.Settings.Noclip.ToString().ToLowerInvariant());
        Log("noclip-speed: " + AkronModule.Settings.NoclipSpeed.ToString(CultureInfo.InvariantCulture));
        Log("noclip-float-speed: " + AkronModule.Settings.NoclipFloatSpeed.ToString(CultureInfo.InvariantCulture));
        Log("noclip-draw-on-top: " + AkronModule.Settings.NoclipDrawOnTop.ToString().ToLowerInvariant());
        Log("noclip-hide-player: " + AkronModule.Settings.NoclipHidePlayer.ToString().ToLowerInvariant());
    }

    [Command("akron_hazard_accuracy", "control playable hazard accuracy: on|off|toggle|status|reset|defaults|invalid-limit <n>|tint <on|off>|tint-mode <entry|touch>|tint-color <hex>|tint-opacity <0-100>|tint-duration <ms>")]
    public static void HazardAccuracy(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.HazardAccuracy)) {
                    Log("hazard-accuracy: blocked");
                    return;
                }
                AkronModule.Settings.NoclipAccuracy = true;
                break;
            case "off":
                AkronModule.Settings.NoclipAccuracy = false;
                AkronModule.ResetNoclipAccuracy();
                break;
            case "toggle":
                if (!AkronModule.Settings.NoclipAccuracy && !AkronModule.TryUse(AkronFeatureKind.HazardAccuracy)) {
                    Log("hazard-accuracy: blocked");
                    return;
                }
                AkronModule.Settings.NoclipAccuracy = !AkronModule.Settings.NoclipAccuracy;
                if (!AkronModule.Settings.NoclipAccuracy) {
                    AkronModule.ResetNoclipAccuracy();
                }
                break;
            case "reset":
                AkronModule.ResetNoclipAccuracy();
                break;
            case "defaults":
            case "resetdefaults":
            case "megahackdefaults":
                AkronModule.Settings.ResetHazardAccuracyDefaults();
                AkronModule.ResetNoclipAccuracy();
                break;
            case "invalidlimit":
            case "limit":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int invalidLimit)) {
                    Log("invalid hazard-accuracy invalid-limit: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyInvalidLimit = AkronModuleSettings.ClampNoclipAccuracyInvalidLimit(invalidLimit);
                break;
            case "tint":
            case "screentint":
                if (!TryParseBoolean(value, out bool tint)) {
                    Log("invalid hazard-accuracy tint: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyTint = tint;
                break;
            case "tintmode":
            case "screenmode":
                if (!TryParseNoclipAccuracyTintMode(value, out AkronNoclipAccuracyTintMode tintMode)) {
                    Log("invalid hazard-accuracy tint-mode: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyTintMode = tintMode;
                break;
            case "tintcolor":
            case "screencolor":
                if (!TryParseRgb(value, out int tintColor)) {
                    Log("invalid hazard-accuracy tint-color: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyTintColor = tintColor;
                break;
            case "tintopacity":
            case "screenopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tintOpacity)) {
                    Log("invalid hazard-accuracy tint-opacity: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyTintOpacity = AkronModuleSettings.ClampOpacity(tintOpacity);
                break;
            case "tintduration":
            case "screenduration":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tintDurationMs)) {
                    Log("invalid hazard-accuracy tint-duration: " + value);
                    return;
                }
                AkronModule.Settings.NoclipAccuracyTintDurationMs = AkronModuleSettings.ClampNoclipAccuracyTintDurationMs(tintDurationMs);
                break;
            default:
                Log("unknown hazard-accuracy action: " + action);
                return;
        }

        LogHazardAccuracyStatus();
    }

    private static void LogHazardAccuracyStatus() {
        Log("hazard-accuracy: " + AkronModule.Settings.NoclipAccuracy.ToString().ToLowerInvariant());
        Log("hazard-accuracy-invalid-limit: " + AkronModule.Settings.NoclipAccuracyInvalidLimit.ToString(CultureInfo.InvariantCulture));
        Log("hazard-accuracy-tint: " + AkronModule.Settings.NoclipAccuracyTint.ToString().ToLowerInvariant());
        Log("hazard-accuracy-tint-mode: " + AkronModule.Settings.NoclipAccuracyTintMode);
        Log("hazard-accuracy-tint-color: " + FormatRgb(AkronModule.Settings.NoclipAccuracyTintColor));
        Log("hazard-accuracy-tint-opacity: " + AkronModule.Settings.NoclipAccuracyTintOpacity.ToString(CultureInfo.InvariantCulture));
        Log("hazard-accuracy-tint-duration-ms: " + AkronModule.Settings.NoclipAccuracyTintDurationMs.ToString(CultureInfo.InvariantCulture));
        Log("hazard-accuracy-state: " + AkronModule.GetNoclipAccuracySnapshot().Describe());
    }

    [Command("akron_invincibility", "control Invincibility: on|off|toggle|status|mode <akron|native>|bottomless-rescue <on|off>|crush-collision <on|off>|lava-ice-pushback <on|off>|spike-ground-refills <on|off>|defaults")]
    public static void Invincibility(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.Invincibility)) {
                    Log("invincibility: blocked");
                    return;
                }
                AkronModule.Settings.Invincibility = true;
                break;
            case "off":
                AkronModule.Settings.Invincibility = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.Invincibility && !AkronModule.TryUse(AkronFeatureKind.Invincibility)) {
                    Log("invincibility: blocked");
                    return;
                }
                AkronModule.Settings.Invincibility = !AkronModule.Settings.Invincibility;
                break;
            case "mode":
                if (!TryParseInvincibilityMode(value, out AkronInvincibilityMode mode)) {
                    Log("invalid invincibility mode: " + value);
                    return;
                }
                AkronModule.Settings.InvincibilityMode = mode;
                break;
            case "bottomlessrescue":
            case "bottomless":
            case "rescue":
                if (!TryParseBoolean(value, out bool bottomlessRescue)) {
                    Log("invalid invincibility bottomless-rescue: " + value);
                    return;
                }
                AkronModule.Settings.InvincibilityBottomlessFallRescue = bottomlessRescue;
                break;
            case "crushcollision":
            case "crush":
                if (!TryParseBoolean(value, out bool crushCollisionChanges)) {
                    Log("invalid invincibility crush-collision: " + value);
                    return;
                }
                AkronModule.Settings.InvincibilityCrushCollisionChanges = crushCollisionChanges;
                break;
            case "lavaicepushback":
            case "lavapushback":
            case "icepushback":
                if (!TryParseBoolean(value, out bool lavaIcePushback)) {
                    Log("invalid invincibility lava-ice-pushback: " + value);
                    return;
                }
                AkronModule.Settings.InvincibilityLavaIcePushback = lavaIcePushback;
                break;
            case "spikegroundrefills":
            case "spikerefills":
                if (!TryParseBoolean(value, out bool spikeGroundRefills)) {
                    Log("invalid invincibility spike-ground-refills: " + value);
                    return;
                }
                AkronModule.Settings.InvincibilitySpikeGroundRefills = spikeGroundRefills;
                break;
            case "defaults":
            case "resetdefaults":
                AkronModule.Settings.InvincibilityMode = AkronInvincibilityMode.Akron;
                AkronModule.Settings.InvincibilityBottomlessFallRescue = true;
                AkronModule.Settings.InvincibilityCrushCollisionChanges = true;
                AkronModule.Settings.InvincibilityLavaIcePushback = true;
                AkronModule.Settings.InvincibilitySpikeGroundRefills = true;
                break;
            default:
                Log("unknown invincibility action: " + action);
                return;
        }

        LogInvincibilityStatus();
    }

    private static bool TryParseInvincibilityMode(string value, out AkronInvincibilityMode mode) {
        switch (NormalizeToken(value)) {
            case "akron":
            case "custom":
                mode = AkronInvincibilityMode.Akron;
                return true;
            case "native":
            case "assist":
                mode = AkronInvincibilityMode.Native;
                return true;
            default:
                mode = AkronInvincibilityMode.Akron;
                return false;
        }
    }

    private static void LogInvincibilityStatus() {
        Log("invincibility: " + AkronModule.Settings.Invincibility.ToString().ToLowerInvariant());
        Log("invincibility-mode: " + AkronModuleSettings.NormalizeInvincibilityMode(AkronModule.Settings.InvincibilityMode).ToString().ToLowerInvariant());
        Log("invincibility-bottomless-fall-rescue: " + AkronModule.Settings.InvincibilityBottomlessFallRescue.ToString().ToLowerInvariant());
        Log("invincibility-crush-collision-changes: " + AkronModule.Settings.InvincibilityCrushCollisionChanges.ToString().ToLowerInvariant());
        Log("invincibility-lava-ice-pushback: " + AkronModule.Settings.InvincibilityLavaIcePushback.ToString().ToLowerInvariant());
        Log("invincibility-spike-ground-refills: " + AkronModule.Settings.InvincibilitySpikeGroundRefills.ToString().ToLowerInvariant());
    }

    [Command("akron_visual_noise", "control visual suppression: particles|trails|glitch|anxiety|distortion|snow|wind-snow|waterfalls|tentacles|heat-distortion <on|off|toggle|status>")]
    public static void VisualNoise(string channel = "status", string action = "status") {
        string normalizedChannel = NormalizeToken(channel);
        bool handled = normalizedChannel switch {
            "" or "status" => true,
            "particles" or "noparticles" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoParticles, AkronModule.Settings.SetNoParticles, "no-particles"),
            "trails" or "notrails" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoTrails, AkronModule.Settings.SetNoTrails, "no-trails"),
            "glitch" or "noglitch" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoGlitch, AkronModule.Settings.SetNoGlitch, "no-glitch"),
            "anxiety" or "noanxiety" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoAnxiety, AkronModule.Settings.SetNoAnxiety, "no-anxiety"),
            "distortion" or "nodistortion" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.NoDistortion, AkronModule.Settings.SetNoDistortion, "no-distortion"),
            "snow" or "hidesnow" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideSnow, value => AkronModule.Settings.HideSnow = value, "hide-snow"),
            "windsnow" or "hidewindsnow" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideWindSnow, value => AkronModule.Settings.HideWindSnow = value, "hide-wind-snow"),
            "waterfalls" or "hidewaterfalls" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideWaterfalls, value => AkronModule.Settings.HideWaterfalls = value, "hide-waterfalls"),
            "tentacles" or "hidetentacles" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideTentacles, value => AkronModule.Settings.HideTentacles = value, "hide-tentacles"),
            "heatdistortion" or "hideheatdistortion" => SetFeatureToggle(action, AkronFeatureKind.ReducedVisualNoise, () => AkronModule.Settings.HideHeatDistortion, value => AkronModule.Settings.HideHeatDistortion = value, "hide-heat-distortion"),
            _ => false
        };

        if (!handled) {
            Log("unknown visual-noise channel: " + channel);
            return;
        }

        LogVisualNoiseSettings();
    }

    [Command("akron_visual_tuning", "control visual tuning: status|light on/off|light-level <0-100>|bloom on/off|bloom-level <0-300>|tint on/off|tint-opacity <0-100>|tint-color <hex>")]
    public static void VisualTuning(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "light":
                if (!TryParseBoolean(value, out bool light)) {
                    Log("invalid light-level toggle: " + value);
                    return;
                }
                if (light && !AkronModule.TryUse(AkronFeatureKind.VisualTuning)) {
                    Log("light-level: blocked");
                    return;
                }
                AkronModule.Settings.LightLevel = light;
                break;
            case "lightlevel":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lightLevel)) {
                    Log("invalid light-level percent: " + value);
                    return;
                }
                AkronModule.Settings.LightLevelPercent = AkronModuleSettings.ClampLightLevelPercent(lightLevel);
                break;
            case "bloom":
                if (!TryParseBoolean(value, out bool bloom)) {
                    Log("invalid bloom-level toggle: " + value);
                    return;
                }
                if (bloom && !AkronModule.TryUse(AkronFeatureKind.VisualTuning)) {
                    Log("bloom-level: blocked");
                    return;
                }
                AkronModule.Settings.BloomLevel = bloom;
                break;
            case "bloomlevel":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bloomLevel)) {
                    Log("invalid bloom-level percent: " + value);
                    return;
                }
                AkronModule.Settings.BloomLevelPercent = AkronModuleSettings.ClampBloomLevelPercent(bloomLevel);
                break;
            case "tint":
            case "screentint":
                if (!TryParseBoolean(value, out bool tint)) {
                    Log("invalid screen-tint toggle: " + value);
                    return;
                }
                if (tint && !AkronModule.TryUse(AkronFeatureKind.VisualTuning)) {
                    Log("screen-tint: blocked");
                    return;
                }
                AkronModule.Settings.ScreenTint = tint;
                break;
            case "tintopacity":
            case "screenopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tintOpacity)) {
                    Log("invalid screen-tint opacity: " + value);
                    return;
                }
                AkronModule.Settings.ScreenTintOpacity = AkronModuleSettings.ClampOpacity(tintOpacity);
                break;
            case "tintcolor":
            case "screencolor":
                if (!TryParseRgb(value, out int tintColor)) {
                    Log("invalid screen-tint color: " + value);
                    return;
                }
                AkronModule.Settings.ScreenTintColor = tintColor;
                break;
            default:
                Log("unknown visual-tuning action: " + action);
                return;
        }

        LogVisualTuningSettings();
    }
}
