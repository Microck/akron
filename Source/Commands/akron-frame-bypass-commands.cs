using System;
using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands
{
    [Command("akron_frame_bypass", "control frame bypass: status|fps on/off|fps-target <60-480>|tps on/off|tps-target <30-480>|method interval|dynamic|camera fancy|fast|off|objects extrapolate|interpolate|tas on/off|subpixel on/off|background on/off|foreground on/off|hide-edges on/off|nasty on/off")]
    public static void FrameBypass(string action = "status", string value = "")
    {
        string normalized = NormalizeToken(action);
        if (!AkronMotionSmoothingInterop.Loaded && normalized != string.Empty && normalized != "status")
        {
            Log("motion-smoothing: missing");
            return;
        }

        switch (normalized)
        {
            case "":
            case "status":
                LogFrameBypassStatus();
                return;
            case "fps":
                if (!TryParseBoolean(value, out bool fpsEnabled))
                {
                    Log("usage: akron_frame_bypass fps on|off");
                    return;
                }

                AkronModule.Settings.FpsBypass = fpsEnabled;
                LogFrameBypassStatus();
                return;
            case "fpstarget":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fpsTarget))
                {
                    Log("usage: akron_frame_bypass fps-target <60-480>");
                    return;
                }

                AkronModule.Settings.FpsBypassTarget = AkronModuleSettings.ClampFpsTarget(fpsTarget);
                LogFrameBypassStatus();
                return;
            case "tps":
                if (!TryParseBoolean(value, out bool tpsEnabled))
                {
                    Log("usage: akron_frame_bypass tps on|off");
                    return;
                }

                AkronModule.Settings.TpsBypass = tpsEnabled;
                LogFrameBypassStatus();
                return;
            case "tpstarget":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tpsTarget))
                {
                    Log("usage: akron_frame_bypass tps-target <30-480>");
                    return;
                }

                AkronModule.Settings.TpsBypassTarget = AkronModuleSettings.ClampTpsTarget(tpsTarget);
                LogFrameBypassStatus();
                return;
            case "method":
                if (!Enum.TryParse(value, ignoreCase: true, out AkronFrameIncreaseMethod method))
                {
                    Log("usage: akron_frame_bypass method interval|dynamic");
                    return;
                }

                AkronModule.Settings.FrameBypassMethod = method;
                LogFrameBypassStatus();
                return;
            case "camera":
            case "smoothcamera":
                if (!Enum.TryParse(value, ignoreCase: true, out AkronCameraSmoothingMode cameraMode))
                {
                    Log("usage: akron_frame_bypass camera fancy|fast|off");
                    return;
                }

                AkronModule.Settings.FrameBypassCameraSmoothing = cameraMode;
                LogFrameBypassStatus();
                return;
            case "objects":
            case "object":
            case "objectsmoothing":
                if (!Enum.TryParse(value, ignoreCase: true, out AkronObjectSmoothingMode objectMode))
                {
                    Log("usage: akron_frame_bypass objects extrapolate|interpolate");
                    return;
                }

                if (objectMode == AkronObjectSmoothingMode.Interpolate)
                {
                    AkronPolicy.RecordCheatUse("Motion Smoothing object interpolation was enabled.");
                }
                AkronModule.Settings.FrameBypassObjectSmoothing = objectMode;
                LogFrameBypassStatus();
                return;
            case "tas":
            case "tasmode":
                if (!TryParseBoolean(value, out bool tasMode))
                {
                    Log("usage: akron_frame_bypass tas on|off");
                    return;
                }

                if (tasMode)
                {
                    AkronPolicy.RecordCheatUse("Motion Smoothing TAS mode was enabled.");
                }
                AkronModule.Settings.FrameBypassTasMode = tasMode;
                LogFrameBypassStatus();
                return;
            case "subpixel":
            case "subpixelmadeline":
                if (!TryParseBoolean(value, out bool subpixelMadeline))
                {
                    Log("usage: akron_frame_bypass subpixel on|off");
                    return;
                }

                AkronModule.Settings.FrameBypassSubpixelMadeline = subpixelMadeline;
                LogFrameBypassStatus();
                return;
            case "background":
            case "smoothbackground":
                if (!TryParseBoolean(value, out bool smoothBackground))
                {
                    Log("usage: akron_frame_bypass background on|off");
                    return;
                }

                AkronModule.Settings.FrameBypassSmoothBackground = smoothBackground;
                LogFrameBypassStatus();
                return;
            case "foreground":
            case "smoothforeground":
                if (!TryParseBoolean(value, out bool smoothForeground))
                {
                    Log("usage: akron_frame_bypass foreground on|off");
                    return;
                }

                AkronModule.Settings.FrameBypassSmoothForeground = smoothForeground;
                LogFrameBypassStatus();
                return;
            case "hideedges":
            case "hideedgegaps":
                if (!TryParseBoolean(value, out bool hideEdges))
                {
                    Log("usage: akron_frame_bypass hide-edges on|off");
                    return;
                }

                AkronModule.Settings.FrameBypassHideStretchedEdges = hideEdges;
                LogFrameBypassStatus();
                return;
            case "nasty":
            case "nastymode":
                if (!TryParseBoolean(value, out bool nastyMode))
                {
                    Log("usage: akron_frame_bypass nasty on|off");
                    return;
                }

                if (nastyMode)
                {
                    AkronPolicy.RecordCheatUse("Motion Smoothing Nasty mode was enabled.");
                }
                AkronModule.Settings.FrameBypassSillyMode = nastyMode;
                LogFrameBypassStatus();
                return;
            default:
                Log("usage: akron_frame_bypass status|fps on/off|fps-target <60-480>|tps on/off|tps-target <30-480>|method interval|dynamic|camera fancy|fast|off|objects extrapolate|interpolate|tas on/off|subpixel on/off|background on/off|foreground on/off|hide-edges on/off|nasty on/off");
                return;
        }
    }

    private static void LogFrameBypassStatus()
    {
        AkronFrameBypassRates rates = AkronRuntimeOptions.ResolveCurrentFrameBypassRates();
        Log("fps-bypass-enabled: " + AkronModule.Settings.FpsBypass.ToString().ToLowerInvariant());
        Log("fps-bypass-target: " + AkronModuleSettings.ClampFpsTarget(AkronModule.Settings.FpsBypassTarget).ToString(CultureInfo.InvariantCulture));
        Log("tps-bypass-enabled: " + AkronModule.Settings.TpsBypass.ToString().ToLowerInvariant());
        Log("tps-bypass-target: " + AkronModuleSettings.ClampTpsTarget(AkronModule.Settings.TpsBypassTarget).ToString(CultureInfo.InvariantCulture));
        Log("frame-bypass-active: " + rates.Active.ToString().ToLowerInvariant());
        Log("frame-bypass-draw-rate: " + rates.DrawRate.ToString(CultureInfo.InvariantCulture));
        Log("frame-bypass-update-rate: " + rates.UpdateRate.ToString(CultureInfo.InvariantCulture));
        Log("frame-bypass-requested-draw-rate: " + rates.RequestedDrawRate.ToString(CultureInfo.InvariantCulture));
        Log("frame-bypass-method: " + AkronModule.Settings.FrameBypassMethod);
        Log("frame-bypass-camera: " + AkronModule.Settings.FrameBypassCameraSmoothing);
        Log("frame-bypass-objects: " + AkronModule.Settings.FrameBypassObjectSmoothing);
        Log("frame-bypass-tas-mode: " + AkronModule.Settings.FrameBypassTasMode.ToString().ToLowerInvariant());
        Log("frame-bypass-subpixel-madeline: " + AkronModule.Settings.FrameBypassSubpixelMadeline.ToString().ToLowerInvariant());
        Log("frame-bypass-smooth-background: " + AkronModule.Settings.FrameBypassSmoothBackground.ToString().ToLowerInvariant());
        Log("frame-bypass-smooth-foreground: " + AkronModule.Settings.FrameBypassSmoothForeground.ToString().ToLowerInvariant());
        Log("frame-bypass-hide-edge-gaps: " + AkronModule.Settings.FrameBypassHideStretchedEdges.ToString().ToLowerInvariant());
        Log("frame-bypass-nasty-mode: " + AkronModule.Settings.FrameBypassSillyMode.ToString().ToLowerInvariant());
    }
}
