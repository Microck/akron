using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_camera_offset", "control camera offset: on|off|toggle|status|set <x> <y>|reset")]
    public static void CameraOffset(string action = "status", string x = "", string y = "") {
        Level level = Engine.Scene as Level;
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.CameraOffset)) {
                    break;
                }
                AkronModule.Settings.CameraOffset = true;
                AkronActions.ApplyCameraOffset(level);
                break;
            case "off":
                AkronModule.Settings.CameraOffset = false;
                AkronActions.ApplyCameraOffset(level);
                break;
            case "toggle":
                bool next = !AkronModule.Settings.CameraOffset;
                if (next && !AkronModule.TryUse(AkronFeatureKind.CameraOffset)) {
                    break;
                }
                AkronModule.Settings.CameraOffset = next;
                AkronActions.ApplyCameraOffset(level);
                break;
            case "reset":
                AkronActions.ResetCameraOffset(level);
                break;
            case "set":
                if (!int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetX) ||
                    !int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("usage: akron_camera_offset set <x> <y>");
                    return;
                }

                AkronModule.Settings.CameraOffsetX = AkronModuleSettings.ClampCameraOffset(offsetX);
                AkronModule.Settings.CameraOffsetY = AkronModuleSettings.ClampCameraOffset(offsetY);
                if (level != null && AkronModule.Settings.CameraOffset) {
                    AkronActions.ApplyCameraOffset(level);
                }
                break;
            default:
                Log("usage: akron_camera_offset on|off|toggle|status|set <x> <y>|reset");
                return;
        }

        Log("camera-offset: " + AkronActions.DescribeCameraOffset(level));
    }

    [Command("akron_cursor_zoom", "control cursor zoom: on|off|status|set <percent>|step <percent>|allow-zoom-out <on|off>|reset-on-deactivate <on|off>|mode <hold|toggle>|reset")]
    public static void CursorZoom(string action = "status", string value = "", string y = "") {
        Level level = Engine.Scene as Level;
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.CursorZoom)) {
                    break;
                }
                AkronModule.Settings.CursorZoom = true;
                break;
            case "off":
                AkronModule.Settings.CursorZoom = false;
                AkronModule.ResetCursorZoom(level);
                break;
            case "toggle":
                bool next = !AkronModule.Settings.CursorZoom;
                if (next && !AkronModule.TryUse(AkronFeatureKind.CursorZoom)) {
                    break;
                }
                AkronModule.Settings.CursorZoom = next;
                if (!next) {
                    AkronModule.ResetCursorZoom(level);
                }
                break;
            case "set":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)) {
                    Log("usage: akron_cursor_zoom set <percent>");
                    return;
                }
                AkronModule.Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(percent, AkronModule.Settings.CursorZoomAllowZoomOut);
                break;
            case "step":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)) {
                    Log("usage: akron_cursor_zoom step <percent>");
                    return;
                }
                AkronModule.Settings.CursorZoomStepPercent = AkronModuleSettings.ClampCursorZoomStepPercent(step);
                break;
            case "allowzoomout":
            case "allowdezoom":
                if (!TryParseBoolean(value, out bool allowZoomOut)) {
                    Log("usage: akron_cursor_zoom allow-zoom-out <on|off>");
                    return;
                }
                AkronModule.Settings.CursorZoomAllowZoomOut = allowZoomOut;
                AkronModule.Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(
                    AkronModule.Settings.CursorZoomPercent,
                    AkronModule.Settings.CursorZoomAllowZoomOut);
                break;
            case "resetondeactivate":
                if (!TryParseBoolean(value, out bool resetOnDeactivate)) {
                    Log("usage: akron_cursor_zoom reset-on-deactivate <on|off>");
                    return;
                }
                AkronModule.Settings.CursorZoomResetOnDeactivate = resetOnDeactivate;
                break;
            case "mode":
                switch (NormalizeToken(value)) {
                    case "hold":
                        AkronModule.Settings.CursorZoomActivationMode = AkronCursorZoomActivationMode.Hold;
                        AkronModule.ResetCursorZoom(level);
                        break;
                    case "toggle":
                        AkronModule.Settings.CursorZoomActivationMode = AkronCursorZoomActivationMode.Toggle;
                        AkronModule.ResetCursorZoom(level);
                        break;
                    default:
                        Log("usage: akron_cursor_zoom mode <hold|toggle>");
                        return;
                }
                break;
            case "reset":
                AkronModule.Settings.CursorZoomPercent = 100;
                AkronModule.Settings.CursorZoomStepPercent = 10;
                AkronModule.Settings.CursorZoomAllowZoomOut = false;
                AkronModule.Settings.CursorZoomResetOnDeactivate = false;
                AkronModule.Settings.CursorZoomActivationMode = AkronCursorZoomActivationMode.Hold;
                AkronModule.ResetCursorZoom(level);
                break;
            default:
                Log("usage: akron_cursor_zoom on|off|toggle|status|set <percent>|step <percent>|allow-zoom-out <on|off>|reset-on-deactivate <on|off>|mode <hold|toggle>|reset");
                return;
        }

        Log("cursor-zoom: " + AkronModule.DescribeCursorZoom(level));
    }
}
