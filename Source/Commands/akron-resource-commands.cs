using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_hud_cheat_indicator", "control HUD cheat indicator: on|off|status|style text|dot|anchor <name>|only-flagged on|off|scale <n>|opacity <n>")]
    public static void HudCheatIndicator(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.HudCheatIndicator = true;
                break;
            case "off":
                AkronModule.Settings.HudCheatIndicator = false;
                break;
            case "style":
                if (!TryParseHudCheatIndicatorStyle(value, out AkronHudCheatIndicatorStyle style)) {
                    Log("invalid HUD cheat indicator style: " + value);
                    return;
                }
                AkronModule.Settings.HudCheatIndicatorStyle = style;
                break;
            case "anchor":
                if (!TryParseHudAnchor(value, out AkronHudAnchor anchor)) {
                    Log("invalid HUD cheat indicator anchor: " + value);
                    return;
                }
                AkronModule.Settings.HudCheatIndicatorAnchor = anchor == AkronHudAnchor.Absolute ? AkronHudAnchor.TopRight : anchor;
                break;
            case "onlyflagged":
            case "onlycheating":
                if (!TryParseBoolean(value, out bool onlyFlagged)) {
                    Log("invalid HUD cheat indicator only-flagged toggle: " + value);
                    return;
                }
                AkronModule.Settings.HudCheatIndicatorOnlyFlagged = onlyFlagged;
                break;
            case "scale":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale)) {
                    Log("invalid HUD cheat indicator scale: " + value);
                    return;
                }
                AkronModule.Settings.HudCheatIndicatorScale = AkronModuleSettings.ClampPercent(scale, 50, 250);
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid HUD cheat indicator opacity: " + value);
                    return;
                }
                AkronModule.Settings.HudCheatIndicatorOpacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            default:
                Log("unknown HUD cheat indicator action: " + action);
                return;
        }

        LogHudCheatIndicatorSettings();
    }

    [Command("akron_resource_hud", "control resource HUD: on|off|status|stamina on|off|dash on|off|threshold <n>|overflow on|off|hide-paused on|off|normal-color <hex>|low-color <hex>")]
    public static void ResourceHud(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.StaminaBar = true;
                AkronModule.Settings.DashBar = true;
                break;
            case "off":
                AkronModule.Settings.StaminaBar = false;
                AkronModule.Settings.DashBar = false;
                break;
            case "toggle":
                bool next = !(AkronModule.Settings.StaminaBar || AkronModule.Settings.DashBar);
                AkronModule.Settings.StaminaBar = next;
                AkronModule.Settings.DashBar = next;
                break;
            case "dash":
            case "pips":
                if (!TryParseBoolean(value, out bool pips)) {
                    Log("invalid dash bar toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBar = pips;
                break;
            case "stamina":
            case "staminabar":
                if (!TryParseBoolean(value, out bool staminaBar)) {
                    Log("invalid resource stamina bar: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBar = staminaBar;
                break;
            case "player":
            case "small":
                if (!TryParseBoolean(value, out bool playerBar)) {
                    Log("invalid stamina player bar toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBarPlayer = playerBar;
                break;
            case "hud":
            case "large":
                if (!TryParseBoolean(value, out bool hudBar)) {
                    Log("invalid stamina HUD bar toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBarHud = hudBar;
                break;
            case "playerposition":
            case "smallposition":
                if (!TryParseStaminaPlayerPosition(value, out AkronStaminaPlayerBarPosition playerPosition)) {
                    Log("invalid stamina player position: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBarPlayerPosition = playerPosition;
                break;
            case "hudposition":
            case "largeposition":
                if (!TryParseStaminaHudPosition(value, out AkronStaminaHudPosition hudPosition)) {
                    Log("invalid stamina HUD position: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBarHudPosition = hudPosition;
                break;
            case "style":
                if (!TryParseStaminaBarStyle(value, out AkronStaminaBarStyle style)) {
                    Log("invalid stamina style: " + value);
                    return;
                }
                AkronModule.Settings.StaminaBarStyle = style;
                break;
            case "alwaysvisible":
            case "always":
                if (!TryParseBoolean(value, out bool alwaysVisible)) {
                    Log("invalid stamina always-visible toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaAlwaysVisible = alwaysVisible;
                break;
            case "dangermarker":
            case "danger":
                if (!TryParseBoolean(value, out bool dangerMarker)) {
                    Log("invalid stamina danger-marker toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaShowDangerMarker = dangerMarker;
                break;
            case "changepulse":
            case "pulse":
                if (!TryParseBoolean(value, out bool changePulse)) {
                    Log("invalid stamina change-pulse toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaShowChangePulse = changePulse;
                break;
            case "hudoffsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetX)) {
                    Log("invalid stamina HUD offset X: " + value);
                    return;
                }
                AkronModule.Settings.StaminaHudOffsetX = AkronModuleSettings.ClampStaminaHudOffset(offsetX);
                break;
            case "hudoffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("invalid stamina HUD offset Y: " + value);
                    return;
                }
                AkronModule.Settings.StaminaHudOffsetY = AkronModuleSettings.ClampStaminaHudOffset(offsetY);
                break;
            case "playeroffsetx":
            case "smalloffsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerOffsetX)) {
                    Log("invalid stamina player offset X: " + value);
                    return;
                }
                AkronModule.Settings.StaminaPlayerOffsetX = AkronModuleSettings.ClampResourcePlayerOffset(playerOffsetX);
                break;
            case "playeroffsety":
            case "smalloffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerOffsetY)) {
                    Log("invalid stamina player offset Y: " + value);
                    return;
                }
                AkronModule.Settings.StaminaPlayerOffsetY = AkronModuleSettings.ClampResourcePlayerOffset(playerOffsetY);
                break;
            case "playerscale":
            case "smallscale":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerScale)) {
                    Log("invalid stamina player scale: " + value);
                    return;
                }
                AkronModule.Settings.StaminaPlayerScale = AkronModuleSettings.ClampResourcePlayerScale(playerScale);
                break;
            case "overflow":
                if (!TryParseBoolean(value, out bool overflow)) {
                    Log("invalid stamina overflow toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaShowOverflow = overflow;
                break;
            case "hidepaused":
                if (!TryParseBoolean(value, out bool hidePaused)) {
                    Log("invalid stamina hide-paused toggle: " + value);
                    return;
                }
                AkronModule.Settings.StaminaHideWhilePaused = hidePaused;
                break;
            case "normalcolor":
                if (!TryParseRgb(value, out int normalColor)) {
                    Log("invalid stamina normal color: " + value);
                    return;
                }
                AkronModule.Settings.StaminaNormalColor = normalColor;
                break;
            case "lowcolor":
                if (!TryParseRgb(value, out int lowColor)) {
                    Log("invalid stamina low color: " + value);
                    return;
                }
                AkronModule.Settings.StaminaLowColor = lowColor;
                break;
            case "overflowcolor":
                if (!TryParseRgb(value, out int overflowColor)) {
                    Log("invalid stamina overflow color: " + value);
                    return;
                }
                AkronModule.Settings.StaminaOverflowColor = overflowColor;
                break;
            case "threshold":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold)) {
                    Log("invalid low-stamina threshold: " + value);
                    return;
                }
                AkronModule.Settings.LowStaminaThreshold = Calc.Clamp(threshold, 1, 100);
                break;
            default:
                Log("unknown resource-hud action: " + action);
                return;
        }

        Log("stamina-bar: " + AkronModule.Settings.StaminaBar.ToString().ToLowerInvariant());
        Log("stamina-player-bar: " + AkronModule.Settings.StaminaBarPlayer.ToString().ToLowerInvariant());
        Log("stamina-hud-bar: " + AkronModule.Settings.StaminaBarHud.ToString().ToLowerInvariant());
        Log("stamina-player-position: " + AkronModule.Settings.StaminaBarPlayerPosition);
        Log("stamina-hud-position: " + AkronModule.Settings.StaminaBarHudPosition);
        Log("stamina-style: " + AkronModule.Settings.StaminaBarStyle);
        Log("stamina-player-offset: " + AkronModule.Settings.StaminaPlayerOffsetX.ToString(CultureInfo.InvariantCulture) + ", " + AkronModule.Settings.StaminaPlayerOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("stamina-player-scale: " + AkronModule.Settings.StaminaPlayerScale.ToString(CultureInfo.InvariantCulture));
        Log("stamina-always-visible: " + AkronModule.Settings.StaminaAlwaysVisible.ToString().ToLowerInvariant());
        Log("stamina-danger-marker: " + AkronModule.Settings.StaminaShowDangerMarker.ToString().ToLowerInvariant());
        Log("stamina-change-pulse: " + AkronModule.Settings.StaminaShowChangePulse.ToString().ToLowerInvariant());
        Log("stamina-hud-offset: " + AkronModule.Settings.StaminaHudOffsetX.ToString(CultureInfo.InvariantCulture) + ", " + AkronModule.Settings.StaminaHudOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("dash-bar: " + AkronModule.Settings.DashBar.ToString().ToLowerInvariant());
        Log("low-stamina-threshold: " + AkronModule.Settings.LowStaminaThreshold.ToString(CultureInfo.InvariantCulture));
        Log("stamina-overflow: " + AkronModule.Settings.StaminaShowOverflow.ToString().ToLowerInvariant());
        Log("stamina-hide-paused: " + AkronModule.Settings.StaminaHideWhilePaused.ToString().ToLowerInvariant());
        Log("stamina-normal-color: " + FormatRgb(AkronModule.Settings.StaminaNormalColor));
        Log("stamina-low-color: " + FormatRgb(AkronModule.Settings.StaminaLowColor));
        Log("stamina-overflow-color: " + FormatRgb(AkronModule.Settings.StaminaOverflowColor));
    }

    [Command("akron_dash_bar", "control dash bar: on|off|status|player on|off|hud on|off|player-position above|below|hud-position <pos>|style pips|bar|always on|off|label on|off|empty-pips on|off|hide-paused on|off|hud-offset-x <n>|hud-offset-y <n>|available-color <hex>|empty-color <hex>|low-color <hex>|fill-color <hex>|line-color <hex>")]
    public static void DashBar(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                AkronModule.Settings.DashBar = true;
                break;
            case "off":
                AkronModule.Settings.DashBar = false;
                break;
            case "toggle":
                AkronModule.Settings.DashBar = !AkronModule.Settings.DashBar;
                break;
            case "player":
            case "playerbar":
                if (!TryParseBoolean(value, out bool playerBar)) {
                    Log("invalid dash player-bar toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarPlayer = playerBar;
                break;
            case "hud":
            case "hudbar":
                if (!TryParseBoolean(value, out bool hudBar)) {
                    Log("invalid dash HUD-bar toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarHud = hudBar;
                break;
            case "playerposition":
            case "playerpos":
                if (!TryParseStaminaPlayerPosition(value, out AkronStaminaPlayerBarPosition playerPosition)) {
                    Log("invalid dash player position: " + value);
                    return;
                }
                AkronModule.Settings.DashBarPlayerPosition = playerPosition;
                break;
            case "hudposition":
            case "hudpos":
                if (!TryParseStaminaHudPosition(value, out AkronStaminaHudPosition hudPosition)) {
                    Log("invalid dash HUD position: " + value);
                    return;
                }
                AkronModule.Settings.DashBarHudPosition = hudPosition;
                break;
            case "style":
                if (!TryParseDashBarStyle(value, out AkronDashBarStyle style)) {
                    Log("invalid dash style: " + value);
                    return;
                }
                AkronModule.Settings.DashBarStyle = style;
                break;
            case "alwaysvisible":
            case "always":
                if (!TryParseBoolean(value, out bool alwaysVisible)) {
                    Log("invalid dash always-visible toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarAlwaysVisible = alwaysVisible;
                break;
            case "label":
            case "text":
                if (!TryParseBoolean(value, out bool showText)) {
                    Log("invalid dash label toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarShowText = showText;
                break;
            case "emptypips":
            case "empty":
                if (!TryParseBoolean(value, out bool emptyPips)) {
                    Log("invalid dash empty-pips toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarShowEmptyPips = emptyPips;
                break;
            case "hidepaused":
                if (!TryParseBoolean(value, out bool hidePaused)) {
                    Log("invalid dash hide-paused toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashBarHideWhilePaused = hidePaused;
                break;
            case "hudoffsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetX)) {
                    Log("invalid dash HUD offset X: " + value);
                    return;
                }
                AkronModule.Settings.DashBarHudOffsetX = AkronModuleSettings.ClampDashHudOffset(offsetX);
                break;
            case "hudoffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("invalid dash HUD offset Y: " + value);
                    return;
                }
                AkronModule.Settings.DashBarHudOffsetY = AkronModuleSettings.ClampDashHudOffset(offsetY);
                break;
            case "playeroffsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerOffsetX)) {
                    Log("invalid dash player offset X: " + value);
                    return;
                }
                AkronModule.Settings.DashBarPlayerOffsetX = AkronModuleSettings.ClampResourcePlayerOffset(playerOffsetX);
                break;
            case "playeroffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerOffsetY)) {
                    Log("invalid dash player offset Y: " + value);
                    return;
                }
                AkronModule.Settings.DashBarPlayerOffsetY = AkronModuleSettings.ClampResourcePlayerOffset(playerOffsetY);
                break;
            case "playerscale":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerScale)) {
                    Log("invalid dash player scale: " + value);
                    return;
                }
                AkronModule.Settings.DashBarPlayerScale = AkronModuleSettings.ClampResourcePlayerScale(playerScale);
                break;
            case "availablecolor":
            case "normalcolor":
                if (!TryParseRgb(value, out int availableColor)) {
                    Log("invalid dash available color: " + value);
                    return;
                }
                AkronModule.Settings.DashBarAvailableColor = availableColor;
                break;
            case "emptycolor":
                if (!TryParseRgb(value, out int emptyColor)) {
                    Log("invalid dash empty color: " + value);
                    return;
                }
                AkronModule.Settings.DashBarEmptyColor = emptyColor;
                break;
            case "lowcolor":
                if (!TryParseRgb(value, out int lowColor)) {
                    Log("invalid dash low color: " + value);
                    return;
                }
                AkronModule.Settings.DashBarLowColor = lowColor;
                break;
            case "fillcolor":
                if (!TryParseRgb(value, out int fillColor)) {
                    Log("invalid dash fill color: " + value);
                    return;
                }
                AkronModule.Settings.DashBarFillColor = fillColor;
                break;
            case "linecolor":
            case "outlinecolor":
                if (!TryParseRgb(value, out int lineColor)) {
                    Log("invalid dash line color: " + value);
                    return;
                }
                AkronModule.Settings.DashBarLineColor = lineColor;
                break;
            default:
                Log("unknown dash-bar action: " + action);
                return;
        }

        LogDashBarSettings();
    }

    [Command("akron_dash_count", "control dash count tools: on|off|status|max <0-5>|room-entry on|off|transition on|off|number on|off|number-offset-y <n>|number-opacity <n>|number-color <hex>|number-outline-color <hex>")]
    public static void DashCount(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.DashCountOverride)) {
                    Log("dash-count: blocked");
                    return;
                }
                AkronModule.Settings.DashCountOverride = true;
                break;
            case "off":
                AkronModule.Settings.DashCountOverride = false;
                break;
            case "toggle":
                bool next = !AkronModule.Settings.DashCountOverride;
                if (next && !AkronModule.TryUse(AkronFeatureKind.DashCountOverride)) {
                    Log("dash-count: blocked");
                    return;
                }
                AkronModule.Settings.DashCountOverride = next;
                break;
            case "max":
            case "count":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxDashes)) {
                    Log("invalid dash count: " + value);
                    return;
                }
                AkronModule.Settings.DashCountOverrideValue = AkronModuleSettings.ClampDashCountOverride(maxDashes);
                break;
            case "roomentry":
                if (!TryParseBoolean(value, out bool roomEntry)) {
                    Log("invalid room-entry toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashCountRefillOnRoomEntry = roomEntry;
                break;
            case "transition":
                if (!TryParseBoolean(value, out bool transition)) {
                    Log("invalid transition toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashCountRefillOnTransition = transition;
                break;
            case "number":
                if (!TryParseBoolean(value, out bool number)) {
                    Log("invalid dash-number toggle: " + value);
                    return;
                }
                AkronModule.Settings.DashNumber = number;
                break;
            case "numberoffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("invalid dash-number offset Y: " + value);
                    return;
                }
                AkronModule.Settings.DashNumberOffsetY = AkronModuleSettings.ClampDashNumberOffsetY(offsetY);
                break;
            case "numberopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid dash-number opacity: " + value);
                    return;
                }
                AkronModule.Settings.DashNumberOpacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            case "numbercolor":
                if (!TryParseRgb(value, out int numberColor)) {
                    Log("invalid dash-number color: " + value);
                    return;
                }
                AkronModule.Settings.DashNumberColor = numberColor;
                break;
            case "numberoutlinecolor":
                if (!TryParseRgb(value, out int outlineColor)) {
                    Log("invalid dash-number outline color: " + value);
                    return;
                }
                AkronModule.Settings.DashNumberOutlineColor = outlineColor;
                break;
            default:
                Log("unknown dash-count action: " + action);
                return;
        }

        LogDashCountSettings();
    }

    [Command("akron_speed_number", "control Speed Number: on|off|status|mode total|horizontal|vertical|offset-y <n>|opacity <n>|color <hex>|outline-color <hex>")]
    public static void SpeedNumber(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.SpeedNumber)) {
                    Log("speed-number: blocked");
                    return;
                }
                AkronModule.Settings.SpeedNumber = true;
                break;
            case "off":
                AkronModule.Settings.SpeedNumber = false;
                break;
            case "toggle":
                bool next = !AkronModule.Settings.SpeedNumber;
                if (next && !AkronModule.TryUse(AkronFeatureKind.SpeedNumber)) {
                    Log("speed-number: blocked");
                    return;
                }
                AkronModule.Settings.SpeedNumber = next;
                break;
            case "mode":
                if (!TryParseSpeedNumberMode(value, out AkronSpeedNumberMode mode)) {
                    Log("invalid speed-number mode: " + value);
                    return;
                }
                AkronModule.Settings.SpeedNumberMode = mode;
                break;
            case "offsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("invalid speed-number offset Y: " + value);
                    return;
                }
                AkronModule.Settings.SpeedNumberOffsetY = AkronModuleSettings.ClampSpeedNumberOffsetY(offsetY);
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid speed-number opacity: " + value);
                    return;
                }
                AkronModule.Settings.SpeedNumberOpacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            case "color":
                if (!TryParseRgb(value, out int color)) {
                    Log("invalid speed-number color: " + value);
                    return;
                }
                AkronModule.Settings.SpeedNumberColor = color;
                break;
            case "outlinecolor":
                if (!TryParseRgb(value, out int outlineColor)) {
                    Log("invalid speed-number outline color: " + value);
                    return;
                }
                AkronModule.Settings.SpeedNumberOutlineColor = outlineColor;
                break;
            default:
                Log("unknown speed-number action: " + action);
                return;
        }

        LogSpeedNumberSettings();
    }

    [Command("akron_air_jumps", "control Air Jumps: on|off|status|infinite on|off|extra <n>|dash-verticals on|off")]
    public static void AirJumps(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
                    Log("air-jumps: blocked");
                    return;
                }
                AkronModule.Settings.JumpHack = true;
                break;
            case "off":
                AkronModule.Settings.JumpHack = false;
                break;
            case "toggle":
                bool next = !AkronModule.Settings.JumpHack;
                if (next && !AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
                    Log("air-jumps: blocked");
                    return;
                }
                AkronModule.Settings.JumpHack = next;
                break;
            case "infinite":
                if (!TryParseBoolean(value, out bool infinite)) {
                    Log("invalid air-jumps infinite toggle: " + value);
                    return;
                }
                AkronModule.Settings.JumpHackInfinite = infinite;
                break;
            case "extra":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int extra)) {
                    Log("invalid air-jumps extra count: " + value);
                    return;
                }
                AkronModule.Settings.JumpHackExtraJumps = AkronModuleSettings.ClampJumpHackExtraJumps(extra);
                break;
            case "dashverticals":
            case "dash-verticals":
                if (!TryParseBoolean(value, out bool dashVerticals)) {
                    Log("invalid air-jumps dash-verticals toggle: " + value);
                    return;
                }
                AkronModule.Settings.JumpHackAllowVerticalDashJumps = dashVerticals;
                break;
            default:
                Log("unknown air-jumps action: " + action);
                return;
        }

        LogAirJumpsSettings();
    }

    [Command("akron_ground_refills", "control Ground Refills: on|off|status|dash on|off|stamina on|off")]
    public static void GroundRefills(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.GroundRefillRules)) {
                    Log("ground-refills: blocked");
                    return;
                }
                AkronModule.Settings.GroundRefillRules = true;
                break;
            case "off":
                AkronModule.Settings.GroundRefillRules = false;
                break;
            case "toggle":
                bool next = !AkronModule.Settings.GroundRefillRules;
                if (next && !AkronModule.TryUse(AkronFeatureKind.GroundRefillRules)) {
                    Log("ground-refills: blocked");
                    return;
                }
                AkronModule.Settings.GroundRefillRules = next;
                break;
            case "dash":
                if (!TryParseBoolean(value, out bool dash)) {
                    Log("invalid ground dash refill toggle: " + value);
                    return;
                }
                AkronModule.Settings.GroundDashRefill = dash;
                break;
            case "stamina":
                if (!TryParseBoolean(value, out bool stamina)) {
                    Log("invalid ground stamina refill toggle: " + value);
                    return;
                }
                AkronModule.Settings.GroundStaminaRefill = stamina;
                break;
            default:
                Log("unknown ground-refills action: " + action);
                return;
        }

        LogGroundRefillSettings();
    }
}
