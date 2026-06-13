using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_auto_kill", "control auto kill: on|off|status|timer on|off|seconds <n>|area <x,y,w,h>|area-add <x,y,w,h>|area-clear|show-area on|off|show-on-death on|off|speed on|off|min-speed <n>|max-speed <n>|h-speed on|off|min-h-speed <n>|max-h-speed <n>|v-speed on|off|min-v-speed <n>|max-v-speed <n>|dash-count on|off|dashes <n>|ground any|grounded|airborne|horizontal any|left|right|still|vertical any|up|down|still|state on|off|state-id <n>|invert on|off")]
    public static void AutoKill(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "") {
        switch (NormalizeToken(action)) {
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
            case "areaclear":
            case "cleararea":
                AkronModule.ClearAutoKillArea();
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
                AkronModule.Settings.AutoKillSpeedCondition = speedCondition;
                break;
            case "minspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minSpeed)) {
                    Log("invalid auto-kill min-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMinSpeed = AkronModuleSettings.ClampAutoKillSpeed(minSpeed);
                break;
            case "maxspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxSpeed)) {
                    Log("invalid auto-kill max-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMaxSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxSpeed);
                break;
            case "hspeed":
            case "horizontalspeed":
                if (!TryParseBoolean(value, out bool horizontalSpeedCondition)) {
                    Log("invalid auto-kill horizontal speed condition toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillHorizontalSpeedCondition = horizontalSpeedCondition;
                break;
            case "minhspeed":
            case "minhorizontalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minHorizontalSpeed)) {
                    Log("invalid auto-kill min-h-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMinHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(minHorizontalSpeed);
                break;
            case "maxhspeed":
            case "maxhorizontalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxHorizontalSpeed)) {
                    Log("invalid auto-kill max-h-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMaxHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxHorizontalSpeed);
                break;
            case "vspeed":
            case "verticalspeed":
                if (!TryParseBoolean(value, out bool verticalSpeedCondition)) {
                    Log("invalid auto-kill vertical speed condition toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillVerticalSpeedCondition = verticalSpeedCondition;
                break;
            case "minvspeed":
            case "minverticalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minVerticalSpeed)) {
                    Log("invalid auto-kill min-v-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMinVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(minVerticalSpeed);
                break;
            case "maxvspeed":
            case "maxverticalspeed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxVerticalSpeed)) {
                    Log("invalid auto-kill max-v-speed: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillMaxVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(maxVerticalSpeed);
                break;
            case "dashcount":
            case "dashcondition":
                if (!TryParseBoolean(value, out bool dashCondition)) {
                    Log("invalid auto-kill dash-count condition toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillDashCountCondition = dashCondition;
                break;
            case "dashes":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dashes)) {
                    Log("invalid auto-kill dashes: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillDashCount = AkronModuleSettings.ClampAutoKillDashCount(dashes);
                break;
            case "ground":
                if (!TryParseAutoKillGroundCondition(value, out AkronAutoKillGroundCondition groundCondition)) {
                    Log("invalid auto-kill ground condition: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillGroundCondition = groundCondition;
                break;
            case "grounded":
                AkronModule.Settings.AutoKillGroundCondition = AkronAutoKillGroundCondition.Grounded;
                break;
            case "airborne":
                AkronModule.Settings.AutoKillGroundCondition = AkronAutoKillGroundCondition.Airborne;
                break;
            case "horizontal":
            case "hdir":
            case "horizontaldirection":
                if (!TryParseAutoKillAxisCondition(value, horizontal: true, out AkronAutoKillAxisCondition horizontalCondition)) {
                    Log("invalid auto-kill horizontal direction: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillHorizontalDirection = horizontalCondition;
                break;
            case "left":
                AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Negative;
                break;
            case "right":
                AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Positive;
                break;
            case "vertical":
            case "vdir":
            case "verticaldirection":
                if (!TryParseAutoKillAxisCondition(value, horizontal: false, out AkronAutoKillAxisCondition verticalCondition)) {
                    Log("invalid auto-kill vertical direction: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillVerticalDirection = verticalCondition;
                break;
            case "up":
                AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Negative;
                break;
            case "down":
                AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Positive;
                break;
            case "still":
            case "stationary":
                AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Zero;
                AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Zero;
                break;
            case "state":
            case "statecondition":
                if (!TryParseBoolean(value, out bool stateCondition)) {
                    Log("invalid auto-kill state condition toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillPlayerStateCondition = stateCondition;
                break;
            case "stateid":
            case "playerstate":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerState)) {
                    Log("invalid auto-kill state-id: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillPlayerState = AkronModuleSettings.ClampAutoKillPlayerState(playerState);
                break;
            case "invert":
            case "inverse":
                if (!TryParseBoolean(value, out bool invert)) {
                    Log("invalid auto-kill invert toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillInvertConditions = invert;
                break;
            default:
                Log("unknown auto-kill action: " + action);
                return;
        }

        Rectangle rect = AkronModule.GetAutoKillArea();
        Log("auto-kill: " + AkronModule.Settings.AutoKill.ToString().ToLowerInvariant());
        Log("auto-kill-timer: " + AkronModule.Settings.AutoKillTimer.ToString().ToLowerInvariant());
        Log("auto-kill-seconds: " + AkronModule.Settings.AutoKillSeconds.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-area: " + AkronModule.Settings.AutoKillArea.ToString().ToLowerInvariant());
        Log("auto-kill-area-count: " + AkronModule.GetAutoKillAreas().Count.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-area-rect: " + rect.X.ToString(CultureInfo.InvariantCulture) + ", " + rect.Y.ToString(CultureInfo.InvariantCulture) + ", " + rect.Width.ToString(CultureInfo.InvariantCulture) + ", " + rect.Height.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-show-area: " + AkronModule.Settings.AutoKillShowArea.ToString().ToLowerInvariant());
        Log("auto-kill-show-area-on-death: " + AkronModule.Settings.AutoKillShowAreaOnDeath.ToString().ToLowerInvariant());
        Log("auto-kill-speed-condition: " + AkronModule.Settings.AutoKillSpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-speed: " + AkronModule.Settings.AutoKillMinSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-speed: " + AkronModule.Settings.AutoKillMaxSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-horizontal-speed-condition: " + AkronModule.Settings.AutoKillHorizontalSpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-horizontal-speed: " + AkronModule.Settings.AutoKillMinHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-horizontal-speed: " + AkronModule.Settings.AutoKillMaxHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-vertical-speed-condition: " + AkronModule.Settings.AutoKillVerticalSpeedCondition.ToString().ToLowerInvariant());
        Log("auto-kill-min-vertical-speed: " + AkronModule.Settings.AutoKillMinVerticalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-max-vertical-speed: " + AkronModule.Settings.AutoKillMaxVerticalSpeed.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-dash-count-condition: " + AkronModule.Settings.AutoKillDashCountCondition.ToString().ToLowerInvariant());
        Log("auto-kill-dash-count: " + AkronModule.Settings.AutoKillDashCount.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-ground-condition: " + AkronModule.Settings.AutoKillGroundCondition.ToString().ToLowerInvariant());
        Log("auto-kill-horizontal-direction: " + DescribeAutoKillAxisCondition(AkronModule.Settings.AutoKillHorizontalDirection, horizontal: true));
        Log("auto-kill-vertical-direction: " + DescribeAutoKillAxisCondition(AkronModule.Settings.AutoKillVerticalDirection, horizontal: false));
        Log("auto-kill-state-condition: " + AkronModule.Settings.AutoKillPlayerStateCondition.ToString().ToLowerInvariant());
        Log("auto-kill-state-id: " + AkronModule.Settings.AutoKillPlayerState.ToString(CultureInfo.InvariantCulture));
        Log("auto-kill-invert-conditions: " + AkronModule.Settings.AutoKillInvertConditions.ToString().ToLowerInvariant());
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
