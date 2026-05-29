using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_auto_kill", "control auto kill: on|off|status|timer on|off|seconds <n>|area <x,y,w,h>|area-add <x,y,w,h>|area-clear|show-area on|off|show-on-death on|off")]
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
                break;
            case "timer":
                if (!TryParseBoolean(value, out bool timer)) {
                    Log("invalid auto-kill timer toggle: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillTimer = timer;
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
