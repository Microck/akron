using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    // These console commands exist for live debugging and automation when normal
    // keyboard injection is unreliable. They intentionally route into the same
    // Akron runtime state and action helpers used by the in-game UI.
    //
    // Surface rule: commands that only exist here are developer/QA surfaces, not
    // first-case player UI. Keep those commands documented here so future docs
    // do not accidentally present them as overlay or Everest mod-option flows.

    [Command("akron_menu_input", "control whether Akron consumes menu input while open: on|off|status")]
    public static void MenuInput(string action = "status") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                break;
            case "on":
            case "true":
                AkronModule.Settings.ConsumeGameplayInputInMenu = true;
                break;
            case "off":
            case "false":
                AkronModule.Settings.ConsumeGameplayInputInMenu = false;
                break;
            default:
                Log("unknown menu-input action: " + action);
                return;
        }

        Log("consume-menu-input: " + AkronModule.Settings.ConsumeGameplayInputInMenu.ToString().ToLowerInvariant());
    }

    [Command("akron_grab_mode", "show or set Celeste grab mode: on|off|toggle-enabled|hold|toggle|invert|status")]
    public static void GrabMode(string value = "") {
        string action = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(action) || action == "status") {
            LogGrabModeStatus();
            return;
        }

        switch (action) {
            case "on":
            case "true":
                AkronModule.Settings.GrabModeOverrideEnabled = true;
                ApplyConfiguredGrabMode();
                LogGrabModeStatus();
                return;
            case "off":
            case "false":
                AkronModule.Settings.GrabModeOverrideEnabled = false;
                Settings.Instance.GrabMode = GrabModes.Hold;
                LogGrabModeStatus();
                return;
            case "toggleenabled":
            case "enabledtoggle":
                AkronModule.Settings.GrabModeOverrideEnabled = !AkronModule.Settings.GrabModeOverrideEnabled;
                if (AkronModule.Settings.GrabModeOverrideEnabled) {
                    ApplyConfiguredGrabMode();
                } else {
                    Settings.Instance.GrabMode = GrabModes.Hold;
                }
                LogGrabModeStatus();
                return;
        }

        if (!TryParseGrabMode(value, out GrabModes mode)) {
            Log("invalid grab-mode: " + value);
            return;
        }

        AkronModule.Settings.GrabModeOverrideMode = mode;
        ApplyConfiguredGrabMode();
        LogGrabModeStatus();
    }

    // Command-only diagnostic output. This is intentionally not an overlay
    // option because it reports renderer timing for development and QA.
    [Command("akron_perf", "show Akron performance telemetry: status|reset")]
    public static void Performance(string action = "status") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "status":
                break;
            case "reset":
                AkronPerformanceTelemetry.Reset();
                break;
            default:
                Log("unknown performance action: " + action);
                return;
        }

        Log(AkronPerformanceTelemetry.DescribeOverlayRenderCadence());
    }

    [Command("akron_showcase_mark", "hidden showcase marker log: sync <label>|note <label>|status")]
    public static void ShowcaseMark(string action = "status", string label = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                Log("showcase-markers: " + AkronShowcaseMarkers.DescribeStatus());
                return;
            case "sync":
                if (!AkronShowcaseMarkers.Enabled) {
                    Log("showcase-markers: disabled");
                    return;
                }

                AkronShowcaseMarkers.MarkSync(label);
                Log("showcase-sync: " + (string.IsNullOrWhiteSpace(label) ? "SYNC" : label.Trim()));
                return;
            case "note":
                if (!AkronShowcaseMarkers.Enabled) {
                    Log("showcase-markers: disabled");
                    return;
                }

                AkronShowcaseMarkers.MarkNote(label);
                Log("showcase-note: " + (string.IsNullOrWhiteSpace(label) ? "note" : label.Trim()));
                return;
            default:
                Log("unknown showcase marker action: " + action);
                Log("usage: akron_showcase_mark sync <label>|note <label>|status");
                return;
        }
    }

    [Command("akron_menu_pause", "control whether Akron pauses gameplay while open: toggle|on|off|status")]
    public static void MenuPause(string action = "toggle") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "toggle":
                AkronModule.Settings.PauseGameplayInMenu = !AkronModule.Settings.PauseGameplayInMenu;
                break;
            case "on":
            case "true":
                AkronModule.Settings.PauseGameplayInMenu = true;
                break;
            case "off":
            case "false":
                AkronModule.Settings.PauseGameplayInMenu = false;
                break;
            case "status":
                break;
            default:
                Log("unknown menu pause action: " + action);
                return;
        }

        Log("pause-gameplay-in-menu: " + AkronModule.Settings.PauseGameplayInMenu.ToString().ToLowerInvariant());
    }

    private static Level RequireLevel() {
        if (Engine.Scene is Level level) {
            return level;
        }

        Log("Akron command requires an active Level scene.");
        return null;
    }

    private static Scene RequireScene() {
        if (Engine.Scene != null) {
            return Engine.Scene;
        }

        Log("Akron command requires an active Celeste scene.");
        return null;
    }

    private static AreaMode ParseAreaMode(string modeText) {
        return NormalizeToken(modeText) switch {
            "b" or "bside" or "bsidechapter" => AreaMode.BSide,
            "c" or "cside" or "csidechapter" => AreaMode.CSide,
            _ => AreaMode.Normal
        };
    }

    private static bool TryParseGrabMode(string value, out GrabModes mode) {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant()) {
            case "hold":
                mode = GrabModes.Hold;
                return true;
            case "toggle":
                mode = GrabModes.Toggle;
                return true;
            case "invert":
                mode = GrabModes.Invert;
                return true;
            default:
                mode = Settings.Instance.GrabMode;
                return false;
        }
    }

    private static void ApplyConfiguredGrabMode() {
        if (!AkronModule.Settings.GrabModeOverrideEnabled) {
            return;
        }

        GrabModes mode = AkronModule.Settings.GrabModeOverrideMode;
        if (Settings.Instance.GrabMode != mode && !AkronModule.TryUse(AkronFeatureKind.GrabModeHotkey)) {
            return;
        }

        Settings.Instance.GrabMode = mode;
    }

    private static void LogGrabModeStatus() {
        Log("grab-mode-enabled: " + AkronModule.Settings.GrabModeOverrideEnabled.ToString().ToLowerInvariant());
        Log("grab-mode-configured: " + AkronModule.Settings.GrabModeOverrideMode);
        Log("grab-mode-active: " + Settings.Instance.GrabMode);
    }

    private static bool TryParseNoclipAccuracyTintMode(string value, out AkronNoclipAccuracyTintMode mode) {
        switch (NormalizeToken(value)) {
            case "entry":
            case "invalidentry":
            case "oninvalidentry":
            case "newinvalid":
                mode = AkronNoclipAccuracyTintMode.OnInvalidEntry;
                return true;
            case "touch":
            case "touching":
            case "whiletouching":
            case "contact":
                mode = AkronNoclipAccuracyTintMode.WhileTouching;
                return true;
            default:
                mode = AkronModule.Settings.NoclipAccuracyTintMode;
                return false;
        }
    }

    private static void SetPolicyToggle(AkronFeatureKind feature, Func<bool> getter, Action<bool> setter) {
        bool next = !getter();
        if (next && !AkronModule.TryUse(feature)) {
            return;
        }

        setter(next);
    }

    private static bool SetPreventDownDashRedirects(string action) {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "off":
            case "false":
            case "disabled":
                AkronModule.Settings.PreventDownDashRedirectsEnabled = false;
                break;
            case "on":
            case "true":
                AkronModule.Settings.PreventDownDashRedirectsEnabled = true;
                break;
            case "normal":
                AkronModule.Settings.PreventDownDashRedirects = AkronPreventDownDashRedirectMode.Normal;
                break;
            case "diagonal":
                AkronModule.Settings.PreventDownDashRedirects = AkronPreventDownDashRedirectMode.Diagonal;
                break;
            case "toggle":
                AkronModule.Settings.PreventDownDashRedirectsEnabled = !AkronModule.Settings.PreventDownDashRedirectsEnabled;
                break;
            default:
                Log("unknown prevent-down-dash-redirects action: " + action);
                return true;
        }

        Log("prevent-down-dash-redirects-enabled: " + AkronModule.Settings.PreventDownDashRedirectsEnabled.ToString().ToLowerInvariant());
        Log("prevent-down-dash-redirects-mode: " + AkronModule.Settings.PreventDownDashRedirects.ToString().ToLowerInvariant());
        return true;
    }

    private static void LogVisualNoiseSettings() {
        Log("low-distraction: " + AkronModule.Settings.IsLowDistractionActive().ToString().ToLowerInvariant());
        Log("reduced-visual-noise: " + AkronModule.Settings.ReducedVisualNoise.ToString().ToLowerInvariant());
        Log("no-particles: " + AkronModule.Settings.NoParticles.ToString().ToLowerInvariant());
        Log("no-trails: " + AkronModule.Settings.NoTrails.ToString().ToLowerInvariant());
        Log("no-glitch: " + AkronModule.Settings.NoGlitch.ToString().ToLowerInvariant());
        Log("no-anxiety: " + AkronModule.Settings.NoAnxiety.ToString().ToLowerInvariant());
        Log("no-distortion: " + AkronModule.Settings.NoDistortion.ToString().ToLowerInvariant());
        Log("glitch-value: " + Glitch.Value.ToString("0.###", CultureInfo.InvariantCulture));
        Log("distort-anxiety: " + Distort.Anxiety.ToString("0.###", CultureInfo.InvariantCulture));
        Log("distort-game-rate: " + Distort.GameRate.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static void LogVisualTuningSettings() {
        Log("visual-tuning: " + AkronRuntimeOptions.DescribeVisualTuning());
        Log("light-level: " + AkronModule.Settings.LightLevel.ToString().ToLowerInvariant());
        Log("light-level-percent: " + AkronModule.Settings.LightLevelPercent.ToString(CultureInfo.InvariantCulture));
        Log("bloom-level: " + AkronModule.Settings.BloomLevel.ToString().ToLowerInvariant());
        Log("bloom-level-percent: " + AkronModule.Settings.BloomLevelPercent.ToString(CultureInfo.InvariantCulture));
        Log("screen-tint: " + AkronModule.Settings.ScreenTint.ToString().ToLowerInvariant());
        Log("screen-tint-color: " + FormatRgb(AkronModule.Settings.ScreenTintColor));
        Log("screen-tint-opacity: " + AkronModule.Settings.ScreenTintOpacity.ToString(CultureInfo.InvariantCulture));
    }

    private static void LogInputsPerSecondSettings() {
        AkronInputsPerSecondSnapshot snapshot = AkronInputHistory.GetInputsPerSecondSnapshot();
        Log("inputs-per-second: " + AkronModule.Settings.InputsPerSecondCounter.ToString().ToLowerInvariant());
        Log("inputs-per-second-current: " + snapshot.Current.ToString(CultureInfo.InvariantCulture));
        Log("inputs-per-second-total: " + snapshot.Total.ToString(CultureInfo.InvariantCulture));
        Log("inputs-per-second-max: " + snapshot.Max.ToString(CultureInfo.InvariantCulture));
        Log("inputs-per-second-placement: " + AkronModule.Settings.InputsPerSecondPlacement);
        Log("inputs-per-second-scale: " + AkronModule.Settings.InputsPerSecondScale.ToString(CultureInfo.InvariantCulture));
        Log("inputs-per-second-opacity: " + AkronModule.Settings.InputsPerSecondOpacity.ToString(CultureInfo.InvariantCulture));
        Log("inputs-per-second-color: " + FormatRgb(AkronModule.Settings.InputsPerSecondTextColor));
        Log("inputs-per-second-show-total: " + AkronModule.Settings.InputsPerSecondShowTotal.ToString().ToLowerInvariant());
        Log("inputs-per-second-show-max: " + AkronModule.Settings.InputsPerSecondShowMax.ToString().ToLowerInvariant());
        Log("inputs-per-second-count-movement: " + AkronModule.Settings.InputsPerSecondCountMovement.ToString().ToLowerInvariant());
        Log("inputs-per-second-count-actions: " + AkronModule.Settings.InputsPerSecondCountActions.ToString().ToLowerInvariant());
        Log("inputs-per-second-count-menu: " + AkronModule.Settings.InputsPerSecondCountMenu.ToString().ToLowerInvariant());
    }

    private static void LogControlDisplaySettings() {
        Log("control-display: " + AkronModule.Settings.ShowTaps.ToString().ToLowerInvariant());
        Log("control-display-corner: " + AkronModule.Settings.TapDisplayCorner);
        Log("control-display-scale: " + AkronModule.Settings.TapDisplayScale.ToString(CultureInfo.InvariantCulture));
        Log("control-display-opacity: " + AkronModule.Settings.TapDisplayOpacity.ToString(CultureInfo.InvariantCulture));
        Log("control-display-elements: " + AkronInputBoard.Describe(AkronModule.Settings.InputBoardElements));
        Log("control-display-source: " + AkronModule.Settings.InputBoardSource);
        Log("control-display-labels: " + AkronModule.Settings.InputBoardLabelPreset);
    }

    private static void LogHudCheatIndicatorSettings() {
        Log("hud-cheat-indicator: " + AkronModule.Settings.HudCheatIndicator.ToString().ToLowerInvariant());
        Log("hud-cheat-indicator-style: " + AkronModule.Settings.HudCheatIndicatorStyle);
        Log("hud-cheat-indicator-anchor: " + AkronModule.Settings.HudCheatIndicatorAnchor);
        Log("hud-cheat-indicator-only-flagged: " + AkronModule.Settings.HudCheatIndicatorOnlyFlagged.ToString().ToLowerInvariant());
        Log("hud-cheat-indicator-scale: " + AkronModule.Settings.HudCheatIndicatorScale.ToString(CultureInfo.InvariantCulture));
        Log("hud-cheat-indicator-opacity: " + AkronModule.Settings.HudCheatIndicatorOpacity.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryParseRgb(string value, out int rgb) {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal)) {
            normalized = normalized.Substring(1);
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(2);
        }

        if (normalized.Length != 6 ||
            !int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) {
            rgb = 0;
            return false;
        }

        rgb &= 0xFFFFFF;
        return true;
    }

    private static bool TryParseHudAnchor(string value, out AkronHudAnchor anchor) {
        switch (NormalizeToken(value)) {
            case "topleft":
                anchor = AkronHudAnchor.TopLeft;
                return true;
            case "topcenter":
                anchor = AkronHudAnchor.TopCenter;
                return true;
            case "topright":
                anchor = AkronHudAnchor.TopRight;
                return true;
            case "middleleft":
            case "centerleft":
                anchor = AkronHudAnchor.MiddleLeft;
                return true;
            case "middle":
            case "center":
                anchor = AkronHudAnchor.Center;
                return true;
            case "middleright":
            case "centerright":
                anchor = AkronHudAnchor.MiddleRight;
                return true;
            case "bottomleft":
                anchor = AkronHudAnchor.BottomLeft;
                return true;
            case "bottomcenter":
                anchor = AkronHudAnchor.BottomCenter;
                return true;
            case "bottomright":
                anchor = AkronHudAnchor.BottomRight;
                return true;
            case "absolute":
                anchor = AkronHudAnchor.Absolute;
                return true;
            default:
                anchor = AkronHudAnchor.TopLeft;
                return false;
        }
    }

    private static bool TryParseHudCheatIndicatorStyle(string value, out AkronHudCheatIndicatorStyle style) {
        switch (NormalizeToken(value)) {
            case "text":
            case "badge":
                style = AkronHudCheatIndicatorStyle.Text;
                return true;
            case "dot":
            case "point":
                style = AkronHudCheatIndicatorStyle.Dot;
                return true;
            default:
                style = AkronHudCheatIndicatorStyle.Text;
                return false;
        }
    }

    private static bool TryParseRectangle(string value, out Rectangle rectangle) {
        rectangle = Rectangle.Empty;
        string normalized = (value ?? string.Empty).Trim().Trim('"', '\'');
        string[] parts = normalized.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)) {
            return false;
        }

        rectangle = new Rectangle(x, y, AkronModuleSettings.ClampAutoKillAreaSize(width), AkronModuleSettings.ClampAutoKillAreaSize(height));
        return rectangle.Width > 0 && rectangle.Height > 0;
    }

    private static bool TryParseStaminaPlayerPosition(string value, out AkronStaminaPlayerBarPosition position) {
        switch (NormalizeToken(value)) {
            case "above":
            case "top":
            case "up":
                position = AkronStaminaPlayerBarPosition.Above;
                return true;
            case "below":
            case "bottom":
            case "down":
                position = AkronStaminaPlayerBarPosition.Below;
                return true;
            default:
                position = AkronStaminaPlayerBarPosition.Above;
                return false;
        }
    }

    private static bool TryParseStaminaHudPosition(string value, out AkronStaminaHudPosition position) {
        switch (NormalizeToken(value)) {
            case "topleft":
            case "lefttop":
                position = AkronStaminaHudPosition.TopLeft;
                return true;
            case "topcenter":
            case "centertop":
            case "topmiddle":
            case "middletop":
                position = AkronStaminaHudPosition.TopCenter;
                return true;
            case "topright":
            case "righttop":
                position = AkronStaminaHudPosition.TopRight;
                return true;
            case "bottomright":
            case "rightbottom":
                position = AkronStaminaHudPosition.BottomRight;
                return true;
            case "bottomcenter":
            case "centerbottom":
            case "bottommiddle":
            case "middlebottom":
                position = AkronStaminaHudPosition.BottomCenter;
                return true;
            case "bottomleft":
            case "leftbottom":
                position = AkronStaminaHudPosition.BottomLeft;
                return true;
            default:
                position = AkronStaminaHudPosition.TopRight;
                return false;
        }
    }

    private static bool TryParseStaminaBarStyle(string value, out AkronStaminaBarStyle style) {
        switch (NormalizeToken(value)) {
            case "bar":
            case "rect":
            case "rectangle":
                style = AkronStaminaBarStyle.Bar;
                return true;
            case "ring":
            case "circle":
            case "circular":
            case "pie":
                style = AkronStaminaBarStyle.Ring;
                return true;
            default:
                style = AkronStaminaBarStyle.Bar;
                return false;
        }
    }

    private static bool TryParseDashBarStyle(string value, out AkronDashBarStyle style) {
        switch (NormalizeToken(value)) {
            case "pips":
            case "pip":
            case "dots":
                style = AkronDashBarStyle.Pips;
                return true;
            case "bar":
            case "rect":
            case "rectangle":
            case "meter":
                style = AkronDashBarStyle.Bar;
                return true;
            default:
                style = AkronDashBarStyle.Pips;
                return false;
        }
    }

    private static bool TryParseSpeedNumberMode(string value, out AkronSpeedNumberMode mode) {
        switch (NormalizeToken(value)) {
            case "total":
            case "overall":
            case "all":
                mode = AkronSpeedNumberMode.Total;
                return true;
            case "horizontal":
            case "x":
                mode = AkronSpeedNumberMode.Horizontal;
                return true;
            case "vertical":
            case "y":
                mode = AkronSpeedNumberMode.Vertical;
                return true;
            default:
                mode = AkronSpeedNumberMode.Total;
                return false;
        }
    }

    private static bool TryParseTrailVisibility(string value, out AkronTrailVisibility visibility) {
        switch (NormalizeToken(value)) {
            case "vanilla":
            case "normal":
            case "default":
                visibility = AkronTrailVisibility.Vanilla;
                return true;
            case "hidden":
            case "hide":
            case "off":
            case "notrail":
                visibility = AkronTrailVisibility.Hidden;
                return true;
            case "always":
            case "on":
            case "force":
            case "forced":
                visibility = AkronTrailVisibility.Always;
                return true;
            default:
                visibility = AkronTrailVisibility.Vanilla;
                return false;
        }
    }

    private static string FormatRgb(int rgb) {
        return "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
    }

    private static void LogDashBarSettings() {
        Log("dash-bar: " + AkronModule.Settings.DashBar.ToString().ToLowerInvariant());
        Log("dash-player-bar: " + AkronModule.Settings.DashBarPlayer.ToString().ToLowerInvariant());
        Log("dash-hud-bar: " + AkronModule.Settings.DashBarHud.ToString().ToLowerInvariant());
        Log("dash-player-position: " + AkronModule.Settings.DashBarPlayerPosition);
        Log("dash-hud-position: " + AkronModule.Settings.DashBarHudPosition);
        Log("dash-style: " + AkronModule.Settings.DashBarStyle);
        Log("dash-player-offset: " + AkronModule.Settings.DashBarPlayerOffsetX.ToString(CultureInfo.InvariantCulture) + ", " + AkronModule.Settings.DashBarPlayerOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("dash-player-scale: " + AkronModule.Settings.DashBarPlayerScale.ToString(CultureInfo.InvariantCulture));
        Log("dash-always-visible: " + AkronModule.Settings.DashBarAlwaysVisible.ToString().ToLowerInvariant());
        Log("dash-show-label: " + AkronModule.Settings.DashBarShowText.ToString().ToLowerInvariant());
        Log("dash-empty-pips: " + AkronModule.Settings.DashBarShowEmptyPips.ToString().ToLowerInvariant());
        Log("dash-hide-paused: " + AkronModule.Settings.DashBarHideWhilePaused.ToString().ToLowerInvariant());
        Log("dash-hud-offset: " + AkronModule.Settings.DashBarHudOffsetX.ToString(CultureInfo.InvariantCulture) + ", " + AkronModule.Settings.DashBarHudOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("dash-available-color: " + FormatRgb(AkronModule.Settings.DashBarAvailableColor));
        Log("dash-empty-color: " + FormatRgb(AkronModule.Settings.DashBarEmptyColor));
        Log("dash-low-color: " + FormatRgb(AkronModule.Settings.DashBarLowColor));
        Log("dash-fill-color: " + FormatRgb(AkronModule.Settings.DashBarFillColor));
        Log("dash-line-color: " + FormatRgb(AkronModule.Settings.DashBarLineColor));
    }

    private static void LogDashCountSettings() {
        Log("dash-count: " + AkronModule.Settings.DashCountOverride.ToString().ToLowerInvariant());
        Log("dash-count-max: " + AkronModule.Settings.DashCountOverrideValue.ToString(CultureInfo.InvariantCulture));
        Log("dash-count-room-entry: " + AkronModule.Settings.DashCountRefillOnRoomEntry.ToString().ToLowerInvariant());
        Log("dash-count-transition: " + AkronModule.Settings.DashCountRefillOnTransition.ToString().ToLowerInvariant());
        Log("dash-number: " + AkronModule.Settings.DashNumber.ToString().ToLowerInvariant());
        Log("dash-number-offset-y: " + AkronModule.Settings.DashNumberOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("dash-number-opacity: " + AkronModule.Settings.DashNumberOpacity.ToString(CultureInfo.InvariantCulture));
        Log("dash-number-color: " + FormatRgb(AkronModule.Settings.DashNumberColor));
        Log("dash-number-outline-color: " + FormatRgb(AkronModule.Settings.DashNumberOutlineColor));
    }

    private static void LogSpeedNumberSettings() {
        Log("speed-number: " + AkronModule.Settings.SpeedNumber.ToString().ToLowerInvariant());
        Log("speed-number-mode: " + AkronModule.Settings.SpeedNumberMode);
        Log("speed-number-offset-y: " + AkronModule.Settings.SpeedNumberOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("speed-number-opacity: " + AkronModule.Settings.SpeedNumberOpacity.ToString(CultureInfo.InvariantCulture));
        Log("speed-number-color: " + FormatRgb(AkronModule.Settings.SpeedNumberColor));
        Log("speed-number-outline-color: " + FormatRgb(AkronModule.Settings.SpeedNumberOutlineColor));
    }

    private static void LogAirJumpsSettings() {
        Log("air-jumps: " + AkronModule.Settings.JumpHack.ToString().ToLowerInvariant());
        Log("air-jumps-infinite: " + AkronModule.Settings.JumpHackInfinite.ToString().ToLowerInvariant());
        Log("air-jumps-extra: " + AkronModule.Settings.JumpHackExtraJumps.ToString(CultureInfo.InvariantCulture));
        Log("air-jumps-dash-verticals: " + AkronModule.Settings.JumpHackAllowVerticalDashJumps.ToString().ToLowerInvariant());
    }

    private static void LogGroundRefillSettings() {
        Log("ground-refills: " + AkronModule.Settings.GroundRefillRules.ToString().ToLowerInvariant());
        Log("ground-refill-dash: " + AkronModule.Settings.GroundDashRefill.ToString().ToLowerInvariant());
        Log("ground-refill-stamina: " + AkronModule.Settings.GroundStaminaRefill.ToString().ToLowerInvariant());
    }

    private static void LogMadelineColorSettings() {
        Log("madeline-colors: " + AkronModule.Settings.MadelineColors.ToString().ToLowerInvariant());
        Log("madeline-color-no-dash: " + AkronModule.Settings.MadelineColorNoDash.ToString().ToLowerInvariant());
        Log("madeline-color-one-dash: " + AkronModule.Settings.MadelineColorOneDash.ToString().ToLowerInvariant());
        Log("madeline-color-two-dash: " + AkronModule.Settings.MadelineColorTwoDash.ToString().ToLowerInvariant());
        Log("madeline-color-three-dash: " + AkronModule.Settings.MadelineColorThreeDash.ToString().ToLowerInvariant());
        Log("madeline-color-four-dash: " + AkronModule.Settings.MadelineColorFourDash.ToString().ToLowerInvariant());
        Log("madeline-color-five-dash: " + AkronModule.Settings.MadelineColorFiveDash.ToString().ToLowerInvariant());
        Log("madeline-gradient: " + AkronModule.Settings.MadelineColorGradient.ToString().ToLowerInvariant());
        Log("madeline-gradient-speed: " + AkronModule.Settings.MadelineColorGradientSpeed.ToString("0.0", CultureInfo.InvariantCulture));
        Log("madeline-no-dash-color: " + FormatRgb(AkronModule.Settings.MadelineNoDashColor));
        Log("madeline-one-dash-color: " + FormatRgb(AkronModule.Settings.MadelineOneDashColor));
        Log("madeline-two-dash-color: " + FormatRgb(AkronModule.Settings.MadelineTwoDashColor));
        Log("madeline-three-dash-color: " + FormatRgb(AkronModule.Settings.MadelineThreeDashColor));
        Log("madeline-four-dash-color: " + FormatRgb(AkronModule.Settings.MadelineFourDashColor));
        Log("madeline-five-dash-color: " + FormatRgb(AkronModule.Settings.MadelineFiveDashColor));
        Log("madeline-gradient-a: " + FormatRgb(AkronModule.Settings.MadelineGradientColorA));
        Log("madeline-gradient-b: " + FormatRgb(AkronModule.Settings.MadelineGradientColorB));
    }

    private static string NormalizeToken(string token) {
        return new string((token ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string JoinCommandText(params string[] parts) {
        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static bool TryParseBoolean(string value, out bool parsed) {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant()) {
            case "on":
            case "true":
            case "yes":
            case "1":
                parsed = true;
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static string FormatVector(Vector2 value) {
        return value.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " + value.Y.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string SafeStatusValue(Func<string> describe, string fallback) {
        try {
            return describe();
        } catch {
            return fallback;
        }
    }

    private static bool TryMovePlayerToNearestSpikes(Level level, Player player) {
        Entity nearestSpikes = level.Tracker.GetEntities<Spikes>()
            .OrderBy(entity => Vector2.DistanceSquared(entity.Center, player.Center))
            .FirstOrDefault();
        if (nearestSpikes == null) {
            return false;
        }

        player.Position = nearestSpikes.Center;
        player.Speed = Vector2.Zero;
        return true;
    }

    private static void LogPlayerSummary(Player player) {
        if (player == null) {
            Log("player: missing");
            return;
        }

        Log("player-position: " + FormatVector(player.Position));
        Log("player-dead: " + player.Dead.ToString().ToLowerInvariant());
        Log("player-stamina: " + player.Stamina.ToString("0.##", CultureInfo.InvariantCulture));
        Log("player-dashes: " + player.Dashes.ToString(CultureInfo.InvariantCulture));
    }

    private static string DescribeSelectedFlag(Level level) {
        List<string> flags = AkronSessionFlagView.GetEditableFlags(level, 12).ToList();
        if (flags.Count == 0) {
            return "no flags";
        }

        int index = Calc.Clamp(AkronModule.Session.EditableFlagIndex, 0, flags.Count - 1);
        return flags[index];
    }

    private static void Log(string line) {
        Engine.Commands?.Log(line);
        AkronAutomationService.RecordOutput(line);
        AkronLog.Info(nameof(AkronCommands), line);
    }
}
