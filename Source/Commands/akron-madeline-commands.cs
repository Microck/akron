using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_no_freeze_frames", "control No Freeze Frames: on|off|toggle|status")]
    public static void NoFreezeFrames(string action = "status") {
        SetFeatureToggle(action, AkronFeatureKind.FreezeFrames, () => AkronModule.Settings.NoFreezeFrames, value => AkronModule.Settings.NoFreezeFrames = value, "no-freeze-frames");
    }

    [Command("akron_madeline_colors", "control Madeline Colors: on|off|status|no-dash on|off|one-dash on|off|two-dash on|off|three-dash on|off|four-dash on|off|five-dash on|off|gradient on|off|speed <n>|*-dash-color <hex>|gradient-a <hex>|gradient-b <hex>")]
    public static void MadelineColors(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.MadelineColors = true;
                break;
            case "off":
                AkronModule.Settings.MadelineColors = false;
                break;
            case "toggle":
                AkronModule.Settings.MadelineColors = !AkronModule.Settings.MadelineColors;
                break;
            case "nodash":
                if (!TryParseBoolean(value, out bool noDash)) {
                    Log("invalid no-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorNoDash = noDash;
                break;
            case "onedash":
            case "baseline":
                if (!TryParseBoolean(value, out bool oneDash)) {
                    Log("invalid one-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorOneDash = oneDash;
                break;
            case "twodash":
                if (!TryParseBoolean(value, out bool twoDash)) {
                    Log("invalid two-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorTwoDash = twoDash;
                break;
            case "threedash":
                if (!TryParseBoolean(value, out bool threeDash)) {
                    Log("invalid three-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorThreeDash = threeDash;
                break;
            case "fourdash":
                if (!TryParseBoolean(value, out bool fourDash)) {
                    Log("invalid four-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorFourDash = fourDash;
                break;
            case "fivedash":
                if (!TryParseBoolean(value, out bool fiveDash)) {
                    Log("invalid five-dash color toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorFiveDash = fiveDash;
                break;
            case "gradient":
                if (!TryParseBoolean(value, out bool gradient)) {
                    Log("invalid gradient toggle: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorGradient = gradient;
                break;
            case "speed":
            case "gradientspeed":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)) {
                    Log("invalid gradient speed: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColorGradientSpeed = AkronModuleSettings.ClampMadelineGradientSpeed(speed);
                break;
            case "nodashcolor":
                if (!TryParseRgb(value, out int noDashColor)) {
                    Log("invalid no-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineNoDashColor = noDashColor;
                break;
            case "onedashcolor":
            case "baselinecolor":
                if (!TryParseRgb(value, out int oneDashColor)) {
                    Log("invalid one-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineOneDashColor = oneDashColor;
                break;
            case "twodashcolor":
                if (!TryParseRgb(value, out int twoDashColor)) {
                    Log("invalid two-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineTwoDashColor = twoDashColor;
                break;
            case "threedashcolor":
                if (!TryParseRgb(value, out int threeDashColor)) {
                    Log("invalid three-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineThreeDashColor = threeDashColor;
                break;
            case "fourdashcolor":
                if (!TryParseRgb(value, out int fourDashColor)) {
                    Log("invalid four-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineFourDashColor = fourDashColor;
                break;
            case "fivedashcolor":
                if (!TryParseRgb(value, out int fiveDashColor)) {
                    Log("invalid five-dash color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineFiveDashColor = fiveDashColor;
                break;
            case "gradienta":
                if (!TryParseRgb(value, out int gradientA)) {
                    Log("invalid gradient A color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineGradientColorA = gradientA;
                break;
            case "gradientb":
                if (!TryParseRgb(value, out int gradientB)) {
                    Log("invalid gradient B color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineGradientColorB = gradientB;
                break;
            default:
                Log("unknown Madeline Colors action: " + action);
                return;
        }

        LogMadelineColorSettings();
    }
}
