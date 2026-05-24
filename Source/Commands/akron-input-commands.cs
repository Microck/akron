using System;
using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_input_history", "control input history: on|off|status|length <n>|placement left|right|opacity <30-100>|compact on|off")]
    public static void InputHistory(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.InputHistoryPanel = true;
                break;
            case "off":
                AkronModule.Settings.InputHistoryPanel = false;
                break;
            case "toggle":
                AkronModule.Settings.InputHistoryPanel = !AkronModule.Settings.InputHistoryPanel;
                break;
            case "length":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length)) {
                    Log("invalid input-history length: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryLength = Calc.Clamp(length, 1, 20);
                break;
            case "placement":
                AkronModule.Settings.InputHistoryPlacement = NormalizeToken(value) == "right" ? AkronHudPlacement.Right : AkronHudPlacement.Left;
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid input-history opacity: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryOpacity = Calc.Clamp(opacity, 30, 100);
                break;
            case "compact":
                if (!TryParseBoolean(value, out bool compact)) {
                    Log("invalid input-history compact: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryCompact = compact;
                break;
            case "pinondeath":
            case "pin-on-death":
                if (!TryParseBoolean(value, out bool pinOnDeath)) {
                    Log("invalid input-history pin-on-death: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryPinOnDeath = pinOnDeath;
                break;
            case "showondeath":
            case "show-on-death":
                if (!TryParseBoolean(value, out bool showOnDeath)) {
                    Log("invalid input-history show-on-death: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryShowOnDeath = showOnDeath;
                break;
            case "transitions":
            case "transition-rows":
                if (!TryParseBoolean(value, out bool transitions)) {
                    Log("invalid input-history transitions: " + value);
                    return;
                }
                AkronModule.Settings.InputHistoryShowTransitions = transitions;
                break;
            default:
                Log("unknown input-history action: " + action);
                return;
        }

        Log("input-history: " + AkronModule.Settings.InputHistoryPanel.ToString().ToLowerInvariant());
        Log("input-history-length: " + AkronModule.Settings.InputHistoryLength.ToString(CultureInfo.InvariantCulture));
        Log("input-history-placement: " + AkronModule.Settings.InputHistoryPlacement);
        Log("input-history-opacity: " + AkronModule.Settings.InputHistoryOpacity.ToString(CultureInfo.InvariantCulture));
        Log("input-history-compact: " + AkronModule.Settings.InputHistoryCompact.ToString().ToLowerInvariant());
        Log("input-history-pin-on-death: " + AkronModule.Settings.InputHistoryPinOnDeath.ToString().ToLowerInvariant());
        Log("input-history-show-on-death: " + AkronModule.Settings.InputHistoryShowOnDeath.ToString().ToLowerInvariant());
        Log("input-history-transitions: " + AkronModule.Settings.InputHistoryShowTransitions.ToString().ToLowerInvariant());
        Log("input-history-samples: " + AkronInputHistory.Describe());
    }

    [Command("akron_control_display", "control live input board: on|off|toggle|status|preset default|split|compact|keyboard|bar|reset|source GameActions|KeyboardKeys|labels Names|Keyboard|Arrows|Short|export [name]|import <file-or-name>|import-latest")]
    public static void ControlDisplay(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        string joined = JoinCommandText(value, part2, part3, part4, part5, part6);
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.ShowTaps)) {
                    return;
                }
                AkronModule.Settings.ShowTaps = true;
                break;
            case "off":
                AkronModule.Settings.ShowTaps = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.ShowTaps && !AkronModule.TryUse(AkronFeatureKind.ShowTaps)) {
                    return;
                }
                AkronModule.Settings.ShowTaps = !AkronModule.Settings.ShowTaps;
                break;
            case "preset":
                switch (NormalizeToken(value)) {
                    case "":
                    case "default":
                        AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildDefaultElements();
                        break;
                    case "compact":
                        AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildCompactElements();
                        break;
                    case "split":
                        AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildSplitElements();
                        break;
                    case "keyboard":
                        AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildKeyboardElements();
                        break;
                    case "bar":
                        AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildBarElements();
                        break;
                    default:
                        Log("invalid control-display preset: " + value);
                        return;
                }
                break;
            case "reset":
                AkronModule.Settings.InputBoardElements = AkronInputBoard.BuildDefaultElements();
                AkronModule.Settings.TapDisplayCorner = IndicatorCorner.BottomRight;
                AkronModule.Settings.TapDisplayScale = 100;
                AkronModule.Settings.TapDisplayOpacity = 80;
                AkronModule.Settings.InputBoardSource = AkronInputBoardSource.GameActions;
                AkronModule.Settings.InputBoardLabelPreset = AkronInputBoardLabelPreset.Keyboard;
                break;
            case "corner":
                if (!Enum.TryParse(value, ignoreCase: true, out IndicatorCorner corner)) {
                    Log("invalid control-display corner: " + value);
                    return;
                }
                AkronModule.Settings.TapDisplayCorner = corner;
                break;
            case "scale":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale)) {
                    Log("invalid control-display scale: " + value);
                    return;
                }
                AkronModule.Settings.TapDisplayScale = AkronModuleSettings.ClampPercent(scale, 50, 250);
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid control-display opacity: " + value);
                    return;
                }
                AkronModule.Settings.TapDisplayOpacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            case "source":
                if (!Enum.TryParse(value, ignoreCase: true, out AkronInputBoardSource source)) {
                    Log("invalid control-display source: " + value);
                    return;
                }
                AkronModule.Settings.InputBoardSource = source;
                break;
            case "labels":
                if (!Enum.TryParse(value, ignoreCase: true, out AkronInputBoardLabelPreset labelPreset)) {
                    Log("invalid control-display labels: " + value);
                    return;
                }
                AkronModule.Settings.InputBoardLabelPreset = labelPreset;
                AkronInputBoard.ApplyLabelPreset(AkronModule.Settings.InputBoardElements, labelPreset);
                break;
            case "export":
                string exportedPath = AkronControlDisplayPresets.ExportCurrent(joined);
                Log("control-display-export: " + exportedPath);
                break;
            case "import":
                if (string.IsNullOrWhiteSpace(joined)) {
                    Log("usage: akron_control_display import <file-or-name>");
                    return;
                }

                Log("control-display-imported: " + AkronControlDisplayPresets.Import(joined).ToString().ToLowerInvariant());
                break;
            case "importlatest":
                string importedPath = AkronControlDisplayPresets.ImportLatest();
                Log("control-display-import-latest: " + importedPath);
                break;
            default:
                Log("unknown control-display action: " + action);
                return;
        }

        LogControlDisplaySettings();
    }

    [Command("akron_ips", "control inputs per second: on|off|status|placement left|right|scale <50-250>|opacity <0-100>|total on|off|max on|off|movement on|off|actions on|off|menu on|off|color <hex>|reset")]
    public static void InputsPerSecond(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.InputsPerSecondCounter)) {
                    Log("inputs-per-second: blocked");
                    return;
                }
                AkronModule.Settings.InputsPerSecondCounter = true;
                break;
            case "off":
                AkronModule.Settings.InputsPerSecondCounter = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.InputsPerSecondCounter && !AkronModule.TryUse(AkronFeatureKind.InputsPerSecondCounter)) {
                    Log("inputs-per-second: blocked");
                    return;
                }
                AkronModule.Settings.InputsPerSecondCounter = !AkronModule.Settings.InputsPerSecondCounter;
                break;
            case "placement":
                AkronModule.Settings.InputsPerSecondPlacement = NormalizeToken(value) == "right" ? AkronHudPlacement.Right : AkronHudPlacement.Left;
                break;
            case "scale":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale)) {
                    Log("invalid inputs-per-second scale: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondScale = AkronModuleSettings.ClampPercent(scale, 50, 250);
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid inputs-per-second opacity: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondOpacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            case "total":
                if (!TryParseBoolean(value, out bool showTotal)) {
                    Log("invalid inputs-per-second total toggle: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondShowTotal = showTotal;
                break;
            case "max":
                if (!TryParseBoolean(value, out bool showMax)) {
                    Log("invalid inputs-per-second max toggle: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondShowMax = showMax;
                break;
            case "movement":
                if (!TryParseBoolean(value, out bool movement)) {
                    Log("invalid inputs-per-second movement toggle: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondCountMovement = movement;
                break;
            case "actions":
                if (!TryParseBoolean(value, out bool actions)) {
                    Log("invalid inputs-per-second actions toggle: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondCountActions = actions;
                break;
            case "menu":
                if (!TryParseBoolean(value, out bool menu)) {
                    Log("invalid inputs-per-second menu toggle: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondCountMenu = menu;
                break;
            case "color":
                if (!TryParseRgb(value, out int color)) {
                    Log("invalid inputs-per-second color: " + value);
                    return;
                }
                AkronModule.Settings.InputsPerSecondTextColor = color;
                break;
            case "reset":
                AkronInputHistory.ResetInputsPerSecond();
                break;
            default:
                Log("unknown inputs-per-second action: " + action);
                return;
        }

        LogInputsPerSecondSettings();
    }
}
