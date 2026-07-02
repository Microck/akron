using System;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_room_capture", "control Room Capture: start|stop|status|format <png|jpg>|markers <on|off>|startpos <on|off>|autokill <on|off>|autodeafen <on|off>|downscale <on|off>|removebg <on|off>|removefg <on|off>|wait <frames>|horizontal <tiles>|vertical <tiles>")]
    public static void RoomCapture(string action = "status", string value = "") {
        Level level = Engine.Scene as Level;
        if (!ApplyScreenshotCaptureSetting(action, value, out bool handled)) {
            return;
        }

        switch (handled ? "status" : NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "start":
            case "capture":
                AkronScreenshotScanner.ScanRoom(level);
                break;
            case "stop":
                AkronScreenshotScanner.Stop();
                break;
            default:
                Log("unknown room-capture action: " + action);
                return;
        }

        Log("room-capture: " + AkronScreenshotScanner.Describe());
        Log("capture-last: " + (string.IsNullOrWhiteSpace(AkronScreenshotScanner.LastExportPath) ? "none" : AkronModule.Settings.FormatPathForDisplay(AkronScreenshotScanner.LastExportPath)));
        LogScreenshotCaptureSettings();
    }

    [Command("akron_map_capture", "control Map Capture: start|stop|status|format <png|jpg>|markers <on|off>|startpos <on|off>|autokill <on|off>|autodeafen <on|off>|downscale <on|off>|removebg <on|off>|removefg <on|off>|wait <frames>|horizontal <tiles>|vertical <tiles>")]
    public static void MapCapture(string action = "status", string value = "") {
        Level level = Engine.Scene as Level;
        if (!ApplyScreenshotCaptureSetting(action, value, out bool handled)) {
            return;
        }

        switch (handled ? "status" : NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "start":
            case "capture":
                AkronScreenshotScanner.ScanChapter(level);
                break;
            case "stop":
                AkronScreenshotScanner.Stop();
                break;
            default:
                Log("unknown map-capture action: " + action);
                return;
        }

        Log("map-capture: " + AkronScreenshotScanner.Describe());
        Log("capture-last: " + (string.IsNullOrWhiteSpace(AkronScreenshotScanner.LastExportPath) ? "none" : AkronModule.Settings.FormatPathForDisplay(AkronScreenshotScanner.LastExportPath)));
        LogScreenshotCaptureSettings();
    }

    private static bool ApplyScreenshotCaptureSetting(string action, string value, out bool handled) {
        handled = true;
        switch (NormalizeToken(action)) {
            case "format":
                if (NormalizeToken(value) is "jpg" or "jpeg") {
                    AkronModule.Settings.ScreenshotScannerImageFormat = AkronScreenshotImageFormat.Jpeg;
                    return true;
                }
                if (NormalizeToken(value) == "png") {
                    AkronModule.Settings.ScreenshotScannerImageFormat = AkronScreenshotImageFormat.Png;
                    return true;
                }

                Log("usage: capture format <png|jpg>");
                return false;
            case "markers":
            case "exportmarkers":
                if (!TryParseBoolean(value, out bool markers)) {
                    Log("usage: capture markers <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerExportMarkers = markers;
                return true;
            case "startpos":
            case "startpositions":
                if (!TryParseBoolean(value, out bool startPositions)) {
                    Log("usage: capture startpos <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerExportStartPositions = startPositions;
                return true;
            case "autokill":
            case "autokillareas":
                if (!TryParseBoolean(value, out bool autoKillAreas)) {
                    Log("usage: capture autokill <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerExportAutoKillAreas = autoKillAreas;
                return true;
            case "autodeafen":
            case "autodeafenareas":
                if (!TryParseBoolean(value, out bool autoDeafenAreas)) {
                    Log("usage: capture autodeafen <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerExportAutoDeafenAreas = autoDeafenAreas;
                return true;
            case "downscale":
            case "downscalemap":
            case "mapdownscale":
                if (!TryParseBoolean(value, out bool downscaleMap)) {
                    Log("usage: capture downscale <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerDownscaleMapCapture = downscaleMap;
                return true;
            case "removebg":
            case "removebackground":
            case "background":
                if (!TryParseBoolean(value, out bool removeBackground)) {
                    Log("usage: capture removebg <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerRemoveBackground = removeBackground;
                return true;
            case "removefg":
            case "removeforeground":
            case "foreground":
                if (!TryParseBoolean(value, out bool removeForeground)) {
                    Log("usage: capture removefg <on|off>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerRemoveForeground = removeForeground;
                return true;
            case "wait":
            case "waitframes":
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int waitFrames)) {
                    Log("usage: capture wait <frames>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerWaitFrames = AkronModuleSettings.ClampScreenshotScannerWaitFrames(waitFrames);
                return true;
            case "horizontal":
            case "h":
            case "x":
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int horizontalTiles)) {
                    Log("usage: capture horizontal <tiles>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerHorizontalOffsetTiles = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(horizontalTiles);
                return true;
            case "vertical":
            case "v":
            case "y":
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int verticalTiles)) {
                    Log("usage: capture vertical <tiles>");
                    return false;
                }
                AkronModule.Settings.ScreenshotScannerVerticalOffsetTiles = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(verticalTiles);
                return true;
            default:
                handled = false;
                return true;
        }
    }

    private static void LogScreenshotCaptureSettings() {
        Log("capture-format: " + AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat));
        Log("capture-markers: " + AkronModule.Settings.ScreenshotScannerExportMarkers.ToString().ToLowerInvariant());
        Log("capture-markers-startpos: " + AkronModule.Settings.ScreenshotScannerExportStartPositions.ToString().ToLowerInvariant());
        Log("capture-markers-autokill: " + AkronModule.Settings.ScreenshotScannerExportAutoKillAreas.ToString().ToLowerInvariant());
        Log("capture-markers-autodeafen: " + AkronModule.Settings.ScreenshotScannerExportAutoDeafenAreas.ToString().ToLowerInvariant());
        Log("capture-map-downscale: " + AkronModule.Settings.ScreenshotScannerDownscaleMapCapture.ToString().ToLowerInvariant());
        Log("capture-freeze-time: " + AkronModule.Settings.ScreenshotScannerFreezeTime.ToString().ToLowerInvariant());
        Log("capture-noclip-hide-madeline: " + AkronModule.Settings.ScreenshotScannerNoclipHideMadeline.ToString().ToLowerInvariant());
        Log("capture-remove-background: " + AkronModule.Settings.ScreenshotScannerRemoveBackground.ToString().ToLowerInvariant());
        Log("capture-remove-foreground: " + AkronModule.Settings.ScreenshotScannerRemoveForeground.ToString().ToLowerInvariant());
        Log("capture-wait-frames: " + AkronModuleSettings.ClampScreenshotScannerWaitFrames(AkronModule.Settings.ScreenshotScannerWaitFrames).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Log("capture-horizontal-tiles: " + AkronModuleSettings.ClampScreenshotScannerOffsetTiles(AkronModule.Settings.ScreenshotScannerHorizontalOffsetTiles).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Log("capture-vertical-tiles: " + AkronModuleSettings.ClampScreenshotScannerOffsetTiles(AkronModule.Settings.ScreenshotScannerVerticalOffsetTiles).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
