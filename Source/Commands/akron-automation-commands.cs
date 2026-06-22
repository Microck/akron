using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_auto_kill", "control auto kill: on|off|status|timer on|off|seconds <n>|area <x,y,w,h>|area-add <x,y,w,h>|area-select <n>|area-clear-selected|area-clear|default <condition-action>|default-from-selected|show-area on|off|show-on-death on|off|speed on|off|min-speed <n>|max-speed <n>|h-speed on|off|min-h-speed <n>|max-h-speed <n>|v-speed on|off|min-v-speed <n>|max-v-speed <n>|dash-count on|off|dashes <n>|ground any|grounded|airborne|horizontal any|left|right|still|vertical any|up|down|still|state on|off|state-id <n>|invert on|off")]
    public static void AutoKill(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "") {
        string normalizedAction = NormalizeToken(action);
        bool editDefaults = normalizedAction is "default" or "defaults" or "template";
        if (editDefaults) {
            action = value;
            value = part2;
            part2 = part3;
            part3 = part4;
            part4 = "";
            normalizedAction = NormalizeToken(action);
        }

        bool TryGetConditionTarget(out AkronAutoKillAreaData selectedArea) {
            if (editDefaults) {
                selectedArea = AkronModule.GetAutoKillDefaultAreaConditions();
                return true;
            }

            if (AkronModule.TryGetSelectedAutoKillArea(out selectedArea)) {
                return true;
            }

            Log("auto-kill: no selected area");
            return false;
        }

        switch (normalizedAction) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.AutoKill)) {
                    Log("auto-kill: blocked");
                    return;
                }
                AkronModule.Settings.AutoKill = true;
                if (!AkronModule.Settings.AutoKillTimer && !AkronModule.Settings.AutoKillArea) {
                    AkronModule.Settings.AutoKillTimer = true;
                }
                break;
            case "off":
                AkronModule.Settings.AutoKill = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.AutoKill && !AkronModule.TryUse(AkronFeatureKind.AutoKill)) {
                    Log("auto-kill: blocked");
                    return;
                }
                AkronModule.Settings.AutoKill = !AkronModule.Settings.AutoKill;
                if (AkronModule.Settings.AutoKill && !AkronModule.Settings.AutoKillTimer && !AkronModule.Settings.AutoKillArea) {
                    AkronModule.Settings.AutoKillTimer = true;
                }
                break;
            case "timer":
                if (!TryParseBoolean(value, out bool timer)) {
                    Log("invalid auto-kill timer toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillTimer = timer;
                if (timer) {
                    AkronModule.Settings.AutoKillArea = false;
                }
                break;
            case "seconds":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)) {
                    Log("invalid auto-kill seconds: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillSeconds = AkronModuleSettings.ClampAutoKillSeconds(seconds);
                break;
            case "area":
            case "areareplace":
                string rectangleValue = string.Join(",", new[] { value, part2, part3, part4 }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!TryParseRectangle(rectangleValue, out Rectangle area)) {
                    Log("invalid auto-kill area, expected x,y,width,height: " + rectangleValue);
                    return;
                }
                AkronModule.SetAutoKillArea(area);
                AkronModule.Settings.AutoKill = true;
                break;
            case "areaadd":
            case "addarea":
                string addedRectangleValue = string.Join(",", new[] { value, part2, part3, part4 }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!TryParseRectangle(addedRectangleValue, out Rectangle addedArea)) {
                    Log("invalid auto-kill area, expected x,y,width,height: " + addedRectangleValue);
                    return;
                }
                AkronModule.AddAutoKillArea(addedArea);
                AkronModule.Settings.AutoKill = true;
                break;
            case "areaselect":
            case "selectarea":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requestedAreaNumber) ||
                    !AkronModule.TrySelectAutoKillArea(requestedAreaNumber - 1)) {
                    Log("invalid auto-kill area index: " + value);
                    return;
                }
                break;
            case "areaclearselected":
            case "clearselectedarea":
                if (!AkronModule.RemoveSelectedAutoKillArea()) {
                    Log("auto-kill: no selected area");
                    return;
                }
                break;
            case "areaclear":
            case "cleararea":
                AkronModule.ClearAutoKillArea();
                break;
            case "defaultfromselected":
            case "usedefaultfromselected":
            case "useselectedasdefault":
                if (!AkronModule.UseSelectedAutoKillAreaAsDefault()) {
                    Log("auto-kill: no selected area");
                    return;
                }
                break;
            case "showarea":
                if (!TryParseBoolean(value, out bool showArea)) {
                    Log("invalid auto-kill show-area toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillShowArea = showArea;
                break;
            case "showondeath":
            case "showareaondeath":
                if (!TryParseBoolean(value, out bool showOnDeath)) {
                    Log("invalid auto-kill show-on-death toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillShowAreaOnDeath = showOnDeath;
                break;
            case "speed":
            case "speedcondition":
                if (!TryParseBoolean(value, out bool speedCondition)) {
                    Log("invalid auto-kill speed condition toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData speedArea)) {
                    return;
                }
                speedArea.SpeedCondition = speedCondition;
                break;
            case "minspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minSpeed)) {
                    Log("invalid auto-kill min-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData minSpeedArea)) {
                    return;
                }
                minSpeedArea.MinSpeed = AkronModuleSettings.ClampAutoKillSpeed(minSpeed);
                break;
            case "maxspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxSpeed)) {
                    Log("invalid auto-kill max-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData maxSpeedArea)) {
                    return;
                }
                maxSpeedArea.MaxSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxSpeed);
                break;
            case "hspeed":
            case "horizontalspeed":
                if (!TryParseBoolean(value, out bool horizontalSpeedCondition)) {
                    Log("invalid auto-kill horizontal speed condition toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData horizontalSpeedArea)) {
                    return;
                }
                horizontalSpeedArea.HorizontalSpeedCondition = horizontalSpeedCondition;
                break;
            case "minhspeed":
            case "minhorizontalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minHorizontalSpeed)) {
                    Log("invalid auto-kill min-h-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData minHorizontalSpeedArea)) {
                    return;
                }
                minHorizontalSpeedArea.MinHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(minHorizontalSpeed);
                break;
            case "maxhspeed":
            case "maxhorizontalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxHorizontalSpeed)) {
                    Log("invalid auto-kill max-h-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData maxHorizontalSpeedArea)) {
                    return;
                }
                maxHorizontalSpeedArea.MaxHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxHorizontalSpeed);
                break;
            case "vspeed":
            case "verticalspeed":
                if (!TryParseBoolean(value, out bool verticalSpeedCondition)) {
                    Log("invalid auto-kill vertical speed condition toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData verticalSpeedArea)) {
                    return;
                }
                verticalSpeedArea.VerticalSpeedCondition = verticalSpeedCondition;
                break;
            case "minvspeed":
            case "minverticalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minVerticalSpeed)) {
                    Log("invalid auto-kill min-v-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData minVerticalSpeedArea)) {
                    return;
                }
                minVerticalSpeedArea.MinVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(minVerticalSpeed);
                break;
            case "maxvspeed":
            case "maxverticalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxVerticalSpeed)) {
                    Log("invalid auto-kill max-v-speed: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData maxVerticalSpeedArea)) {
                    return;
                }
                maxVerticalSpeedArea.MaxVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxVerticalSpeed);
                break;
            case "dashcount":
            case "dashcondition":
                if (!TryParseBoolean(value, out bool dashCondition)) {
                    Log("invalid auto-kill dash-count condition toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData dashArea)) {
                    return;
                }
                dashArea.DashCountCondition = dashCondition;
                break;
            case "dashes":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dashes)) {
                    Log("invalid auto-kill dashes: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData dashesArea)) {
                    return;
                }
                dashesArea.DashCount = AkronModuleSettings.ClampAutoKillDashCount(dashes);
                break;
            case "ground":
                if (!TryParseAutoKillGroundCondition(value, out AkronAutoKillGroundCondition groundCondition)) {
                    Log("invalid auto-kill ground condition: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData groundArea)) {
                    return;
                }
                groundArea.GroundCondition = groundCondition;
                break;
            case "grounded":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData groundedArea)) {
                    return;
                }
                groundedArea.GroundCondition = AkronAutoKillGroundCondition.Grounded;
                break;
            case "airborne":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData airborneArea)) {
                    return;
                }
                airborneArea.GroundCondition = AkronAutoKillGroundCondition.Airborne;
                break;
            case "horizontal":
            case "hdir":
            case "horizontaldirection":
                if (!TryParseAutoKillAxisCondition(value, horizontal: true, out AkronAutoKillAxisCondition horizontalCondition)) {
                    Log("invalid auto-kill horizontal direction: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData horizontalArea)) {
                    return;
                }
                horizontalArea.HorizontalDirection = horizontalCondition;
                break;
            case "left":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData leftArea)) {
                    return;
                }
                leftArea.HorizontalDirection = AkronAutoKillAxisCondition.Negative;
                break;
            case "right":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData rightArea)) {
                    return;
                }
                rightArea.HorizontalDirection = AkronAutoKillAxisCondition.Positive;
                break;
            case "vertical":
            case "vdir":
            case "verticaldirection":
                if (!TryParseAutoKillAxisCondition(value, horizontal: false, out AkronAutoKillAxisCondition verticalCondition)) {
                    Log("invalid auto-kill vertical direction: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData verticalArea)) {
                    return;
                }
                verticalArea.VerticalDirection = verticalCondition;
                break;
            case "up":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData upArea)) {
                    return;
                }
                upArea.VerticalDirection = AkronAutoKillAxisCondition.Negative;
                break;
            case "down":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData downArea)) {
                    return;
                }
                downArea.VerticalDirection = AkronAutoKillAxisCondition.Positive;
                break;
            case "still":
            case "stationary":
                if (!TryGetConditionTarget(out AkronAutoKillAreaData stillArea)) {
                    return;
                }
                stillArea.HorizontalDirection = AkronAutoKillAxisCondition.Zero;
                stillArea.VerticalDirection = AkronAutoKillAxisCondition.Zero;
                break;
            case "state":
            case "statecondition":
                if (!TryParseBoolean(value, out bool stateCondition)) {
                    Log("invalid auto-kill state condition toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData stateArea)) {
                    return;
                }
                stateArea.PlayerStateCondition = stateCondition;
                break;
            case "stateid":
            case "playerstate":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerState)) {
                    Log("invalid auto-kill state-id: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData playerStateArea)) {
                    return;
                }
                playerStateArea.PlayerState = AkronModuleSettings.ClampAutoKillPlayerState(playerState);
                break;
            case "invert":
            case "inverse":
                if (!TryParseBoolean(value, out bool invert)) {
                    Log("invalid auto-kill invert toggle: " + value);
                    return;
                }
                if (!TryGetConditionTarget(out AkronAutoKillAreaData invertArea)) {
                    return;
                }
                invertArea.InvertConditions = invert;
                break;
            default:
                Log("unknown auto-kill action: " + action);
                return;
        }

        bool hasSelectedArea = AkronModule.TryGetSelectedAutoKillArea(out AkronAutoKillAreaData statusArea);
        bool hasConditionStatus = editDefaults || hasSelectedArea;
        if (editDefaults) {
            statusArea = AkronModule.GetAutoKillDefaultAreaConditions();
        }
        int selectedAreaNumber = hasSelectedArea ? AkronModule.GetSelectedAutoKillAreaIndex() + 1 : 0;
        Rectangle rect = hasSelectedArea ? AkronModule.GetSelectedAutoKillArea() : AkronModule.GetAutoKillArea();
        Log("auto-kill: " + AkronModule.Settings.AutoKill.ToString().ToLowerInvariant());
        Log("auto-kill-timer: " + AkronModule.Settings.AutoKillTimer.ToString().ToLowerInvariant());
        Log("auto-kill-seconds: " + AkronModule.Settings.AutoKillSeconds.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-area: " + AkronModule.Settings.AutoKillArea.ToString().ToLowerInvariant());
        Log("auto-kill-area-count: " + AkronModule.GetAutoKillAreas().Count.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-selected-area: " + selectedAreaNumber.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-area-rect: " + rect.X.ToString(CultureInfo.InvariantCulture) + ", " + rect.Y.ToString(CultureInfo.InvariantCulture) + ", " + rect.Width.ToString(CultureInfo.InvariantCulture) + ", " + rect.Height.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-condition-target: " + (editDefaults ? "default" : "selected"));
        Log("auto-kill-show-area: " + AkronModule.Settings.AutoKillShowArea.ToString().ToLowerInvariant());
        Log("auto-kill-show-area-on-death: " + AkronModule.Settings.AutoKillShowAreaOnDeath.ToString().ToLowerInvariant());
        if (!hasConditionStatus) {
            Log("auto-kill-area-conditions: none");
            return;
        }

        Log("auto-kill-speed-condition: " + statusArea.SpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-speed: " + statusArea.MinSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-speed: " + statusArea.MaxSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-horizontal-speed-condition: " + statusArea.HorizontalSpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-horizontal-speed: " + statusArea.MinHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-horizontal-speed: " + statusArea.MaxHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-vertical-speed-condition: " + statusArea.VerticalSpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-vertical-speed: " + statusArea.MinVerticalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-vertical-speed: " + statusArea.MaxVerticalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-dash-count-condition: " + statusArea.DashCountCondition.ToString().ToLowerInvariant());
        Log("auto-kill-dash-count: " + statusArea.DashCount.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-ground-condition: " + statusArea.GroundCondition.ToString().ToLowerInvariant());
        Log("auto-kill-horizontal-direction: " + DescribeAutoKillAxisCondition(statusArea.HorizontalDirection, horizontal: true));
        Log("auto-kill-vertical-direction: " + DescribeAutoKillAxisCondition(statusArea.VerticalDirection, horizontal: false));
        Log("auto-kill-state-condition: " + statusArea.PlayerStateCondition.ToString().ToLowerInvariant());
        Log("auto-kill-state-id: " + statusArea.PlayerState.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-invert-conditions: " + statusArea.InvertConditions.ToString().ToLowerInvariant());
    }

    [Command("akron_auto_deafen", "control auto deafen: on|off|status|hotkey <combo>|hotkey-clear|test|area <x,y,w,h>|area-add <x,y,w,h>|area-clear|show-area on|off|restore")]
    public static void AutoDeafen(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.AutoDeafen)) {
                    Log("auto-deafen: blocked");
                    return;
                }
                AkronModule.Settings.AutoDeafen = true;
                break;
            case "off":
                AkronModule.Settings.AutoDeafen = false;
                AkronActions.RestoreAutoDeafen();
                break;
            case "toggle":
                if (!AkronModule.Settings.AutoDeafen && !AkronModule.TryUse(AkronFeatureKind.AutoDeafen)) {
                    Log("auto-deafen: blocked");
                    return;
                }
                AkronModule.Settings.AutoDeafen = !AkronModule.Settings.AutoDeafen;
                if (!AkronModule.Settings.AutoDeafen) {
                    AkronActions.RestoreAutoDeafen();
                }
                break;
            case "area":
            case "areareplace":
                string rectangleValue = string.Join(",", new[] { value, part2, part3, part4 }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!TryParseRectangle(rectangleValue, out Rectangle area)) {
                    Log("invalid auto-deafen area, expected x,y,width,height: " + rectangleValue);
                    return;
                }
                AkronModule.SetAutoDeafenArea(area);
                AkronModule.Settings.AutoDeafen = true;
                break;
            case "areaadd":
            case "addarea":
                string addedRectangleValue = string.Join(",", new[] { value, part2, part3, part4 }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!TryParseRectangle(addedRectangleValue, out Rectangle addedArea)) {
                    Log("invalid auto-deafen area, expected x,y,width,height: " + addedRectangleValue);
                    return;
                }
                AkronModule.AddAutoDeafenArea(addedArea);
                AkronModule.Settings.AutoDeafen = true;
                break;
            case "areaclear":
            case "cleararea":
                AkronModule.ClearAutoDeafenArea();
                break;
            case "showarea":
                if (!TryParseBoolean(value, out bool showArea)) {
                    Log("invalid auto-deafen show-area toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoDeafenShowArea = showArea;
                break;
            case "hotkey":
            case "bind":
                string hotkeyValue = JoinCommandText(value, part2, part3, part4);
                if (!AkronActions.SetAutoDeafenHotkey(hotkeyValue, out string hotkeyError)) {
                    Log(hotkeyError);
                    return;
                }
                break;
            case "hotkeyclear":
            case "clearhotkey":
            case "unbind":
                AkronActions.RestoreAutoDeafen();
                AkronModule.Settings.AutoDeafenHotkey = string.Empty;
                break;
            case "test":
            case "testtoggle":
                if (!AkronActions.ToggleAutoDeafenHotkeyForTest(out string testError)) {
                    Log(testError);
                    return;
                }
                break;
            case "restore":
            case "undeafen":
                AkronActions.RestoreAutoDeafen();
                break;
            default:
                Log("unknown auto-deafen action: " + action);
                return;
        }

        Rectangle rect = AkronModule.GetAutoDeafenArea();
        Log("auto-deafen: " + AkronModule.Settings.AutoDeafen.ToString().ToLowerInvariant());
        Log("auto-deafen-active: " + AkronActions.AutoDeafenActive.ToString().ToLowerInvariant());
        Log("auto-deafen-method: discord-hotkey");
        Log("auto-deafen-hotkey: " + AkronActions.DescribeAutoDeafenHotkey());
        Log("auto-deafen-area: " + AkronModule.Settings.AutoDeafenArea.ToString().ToLowerInvariant());
        Log("auto-deafen-area-count: " + AkronModule.GetAutoDeafenAreas().Count.ToString(CultureInfo.InvariantCulture));
        Log("auto-deafen-area-rect: " + rect.X.ToString(CultureInfo.InvariantCulture) + ", " + rect.Y.ToString(CultureInfo.InvariantCulture) + ", " + rect.Width.ToString(CultureInfo.InvariantCulture) + ", " + rect.Height.ToString(CultureInfo.InvariantCulture));
        Log("auto-deafen-show-area: " + AkronModule.Settings.AutoDeafenShowArea.ToString().ToLowerInvariant());
    }
}
