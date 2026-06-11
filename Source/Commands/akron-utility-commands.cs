using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_autosave", "control Autosave: on|off|toggle|save|hide-icon-on|hide-icon-off|hide-icon-toggle|status")]
    public static void Autosave(string action = "status") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.Autosave = true;
                break;
            case "off":
                AkronModule.Settings.Autosave = false;
                break;
            case "toggle":
                AkronModule.Settings.Autosave = !AkronModule.Settings.Autosave;
                break;
            case "save":
                AkronAutosave.SaveNow();
                break;
            case "hideiconon":
                AkronModule.Settings.AutosaveHideSavingIcon = true;
                break;
            case "hideiconoff":
                AkronModule.Settings.AutosaveHideSavingIcon = false;
                break;
            case "hideicontoggle":
                AkronModule.Settings.AutosaveHideSavingIcon = !AkronModule.Settings.AutosaveHideSavingIcon;
                break;
            default:
                Log("unknown autosave action: " + action);
                return;
        }

        Log("autosave: " + AkronModule.Settings.Autosave.ToString().ToLowerInvariant());
        Log("autosave-interval: " + AkronModule.Settings.AutosaveIntervalSeconds.ToString(CultureInfo.InvariantCulture));
        Log("autosave-minimum-delay: " + AkronModule.Settings.AutosaveMinimumDelaySeconds.ToString(CultureInfo.InvariantCulture));
        Log("autosave-hide-saving-icon: " + AkronModule.Settings.AutosaveHideSavingIcon.ToString().ToLowerInvariant());
    }

    [Command("akron_deload_spinners", "control spinner deload simulation: on|off|toggle|status|now|delay <seconds>")]
    public static void DeloadSpinners(string action = "status", string secondsBeforeDeload = "") {
        Level level = RequireLevel();
        if (level == null) {
            return;
        }

        float delay = AkronModule.Settings.DeloadSpinnerDelaySeconds;
        int steps = 0;
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.DeloadSimulation)) {
                    Log("deload-spinners: blocked");
                    return;
                }
                steps = AkronDeloadSimulator.Simulate(level, delay);
                AkronModule.Settings.DeloadSpinners = false;
                break;
            case "off":
                AkronModule.Settings.DeloadSpinners = false;
                break;
            case "toggle":
                if (!AkronModule.TryUse(AkronFeatureKind.DeloadSimulation)) {
                    Log("deload-spinners: blocked");
                    return;
                }
                steps = AkronDeloadSimulator.Simulate(level, delay);
                AkronModule.Settings.DeloadSpinners = false;
                break;
            case "now":
                if (!AkronModule.TryUse(AkronFeatureKind.DeloadSimulation)) {
                    Log("deload-spinners: blocked");
                    return;
                }
                steps = AkronDeloadSimulator.Simulate(level, delay);
                AkronModule.Settings.DeloadSpinners = false;
                break;
            case "delay":
                if (!float.TryParse(secondsBeforeDeload, NumberStyles.Float, CultureInfo.InvariantCulture, out delay)) {
                    Log("invalid deload seconds: " + secondsBeforeDeload);
                    return;
                }
                AkronModule.Settings.DeloadSpinnerDelaySeconds = AkronDeloadSimulator.ClampDelaySeconds(delay);
                delay = AkronModule.Settings.DeloadSpinnerDelaySeconds;
                break;
            default:
                if (!float.TryParse(action, NumberStyles.Float, CultureInfo.InvariantCulture, out delay)) {
                    Log("usage: akron_deload_spinners on|off|toggle|status|now|delay <seconds>");
                    return;
                }
                AkronModule.Settings.DeloadSpinnerDelaySeconds = AkronDeloadSimulator.ClampDelaySeconds(delay);
                delay = AkronModule.Settings.DeloadSpinnerDelaySeconds;
                break;
        }

        Log("deload-spinners: " + AkronModule.Settings.DeloadSpinners.ToString().ToLowerInvariant());
        Log("deload-spinners-delay: " + delay.ToString("0.###", CultureInfo.InvariantCulture));
        Log("deload-spinners-steps: " + steps.ToString(CultureInfo.InvariantCulture));
        Log("level-time-active: " + level.TimeActive.ToString("0.000", CultureInfo.InvariantCulture));
        Log("level-raw-time-active: " + level.RawTimeActive.ToString("0.000", CultureInfo.InvariantCulture));
    }

    [Command("akron_sound_volume", "control per-SFX volume: status|on <key>|off <key>|volume <key> <0-200>")]
    public static void SoundVolume(string action = "status", string key = "", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronEarAid.SetOverrideEnabled(key, true);
                break;
            case "off":
                AkronEarAid.SetOverrideEnabled(key, false);
                break;
            case "volume":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int volume)) {
                    Log("invalid sound volume: " + value);
                    return;
                }
                AkronEarAid.SetVolume(key, volume);
                break;
            default:
                Log("unknown sound-volume action: " + action);
                return;
        }

        foreach (AkronEarAid.SoundDefinition sound in AkronEarAid.Sounds) {
            Log("sound-volume-enabled-" + sound.Key + ": " + AkronEarAid.OverrideEnabled(sound.Key).ToString().ToLowerInvariant());
            Log("sound-volume-" + sound.Key + ": " + AkronEarAid.VolumeFor(sound.Key).ToString(CultureInfo.InvariantCulture));
        }
    }
}
