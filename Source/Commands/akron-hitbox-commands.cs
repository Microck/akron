using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_hitboxes", "control Akron hitbox overlay: toggle|on|off|status|sync")]
    public static void Hitboxes(string action = "toggle") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "toggle":
                SetPolicyToggle(AkronFeatureKind.HitboxViewer, () => AkronModule.Settings.HitboxViewer, value => AkronModule.Settings.HitboxViewer = value);
                break;
            case "on":
                if (!AkronModule.Settings.HitboxViewer) {
                    SetPolicyToggle(AkronFeatureKind.HitboxViewer, () => AkronModule.Settings.HitboxViewer, value => AkronModule.Settings.HitboxViewer = value);
                }
                break;
            case "off":
                AkronModule.Settings.HitboxViewer = false;
                break;
            case "status":
                break;
            case "sync":
                AkronEntityInspector.SyncHitboxes();
                Log("hitboxes: synced");
                break;
            default:
                Log("unknown hitbox action: " + action);
                return;
        }

        Log("hitboxes: " + AkronModule.Settings.HitboxViewer.ToString().ToLowerInvariant());
        Log("fix-hitbox-pixels: " + AkronModule.Settings.FixHitboxPixels.ToString().ToLowerInvariant());
        Log(AkronEntityInspector.DescribeHitboxPerformance());
    }

    [Command("akron_inspector", "control Akron entity inspector: toggle|on|off|status")]
    public static void Inspector(string action = "toggle") {
        switch ((action ?? string.Empty).Trim().ToLowerInvariant()) {
            case "":
            case "toggle":
                SetPolicyToggle(AkronFeatureKind.EntityInspector, () => AkronModule.Settings.EntityInspector, value => AkronModule.Settings.EntityInspector = value);
                break;
            case "on":
                if (!AkronModule.Settings.EntityInspector) {
                    SetPolicyToggle(AkronFeatureKind.EntityInspector, () => AkronModule.Settings.EntityInspector, value => AkronModule.Settings.EntityInspector = value);
                }
                break;
            case "off":
                AkronModule.Settings.EntityInspector = false;
                break;
            case "status":
                break;
            default:
                Log("unknown inspector action: " + action);
                return;
        }

        Log("inspector: " + AkronModule.Settings.EntityInspector.ToString().ToLowerInvariant());
    }

    [Command("akron_hitbox_filter", "control hitbox filters: active-only|hide-player|player-hurtbox|hazards|solids|triggers|last-death|death-all <on|off|status>")]
    public static void HitboxFilter(string filter = "status", string action = "status") {
        string normalizedFilter = NormalizeToken(filter);
        bool handled = normalizedFilter switch {
            "" or "status" => true,
            "activeonly" => SetPlainToggle(action, () => AkronModule.Settings.HitboxActiveOnly, value => AkronModule.Settings.HitboxActiveOnly = value, "hitbox-active-only"),
            "hideplayer" => SetPlainToggle(action, () => AkronModule.Settings.HitboxHidePlayer, value => AkronModule.Settings.HitboxHidePlayer = value, "hitbox-hide-player"),
            "playerhurtbox" or "playerhazard" or "hurtbox" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowPlayerHurtbox, value => AkronModule.Settings.HitboxShowPlayerHurtbox = value, "hitbox-player-hurtbox"),
            "hazards" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowHazards, value => AkronModule.Settings.HitboxShowHazards = value, "hitbox-hazards"),
            "solids" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowSolids, value => AkronModule.Settings.HitboxShowSolids = value, "hitbox-solids"),
            "triggers" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowTriggers, value => AkronModule.Settings.HitboxShowTriggers = value, "hitbox-triggers"),
            "lastdeath" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowLastDeath, value => AkronModule.Settings.HitboxShowLastDeath = value, "hitbox-last-death"),
            "deathall" or "alldeath" or "ondeathall" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowAllOnDeath, value => AkronModule.Settings.HitboxShowAllOnDeath = value, "hitbox-death-all"),
            "deathplayer" or "playermarker" => SetPlainToggle(action, () => AkronModule.Settings.HitboxShowDeathPlayerMarker, value => AkronModule.Settings.HitboxShowDeathPlayerMarker = value, "hitbox-death-player-marker"),
            _ => false
        };
        if (!handled) {
            Log("unknown hitbox filter: " + filter);
            return;
        }

        Log("hitbox-active-only: " + AkronModule.Settings.HitboxActiveOnly.ToString().ToLowerInvariant());
        Log("hitbox-hide-player: " + AkronModule.Settings.HitboxHidePlayer.ToString().ToLowerInvariant());
        Log("hitbox-player-hurtbox: " + AkronModule.Settings.HitboxShowPlayerHurtbox.ToString().ToLowerInvariant());
        Log("hitbox-hazards: " + AkronModule.Settings.HitboxShowHazards.ToString().ToLowerInvariant());
        Log("hitbox-solids: " + AkronModule.Settings.HitboxShowSolids.ToString().ToLowerInvariant());
        Log("hitbox-triggers: " + AkronModule.Settings.HitboxShowTriggers.ToString().ToLowerInvariant());
        Log("hitbox-last-death: " + AkronModule.Settings.HitboxShowLastDeath.ToString().ToLowerInvariant());
        Log("hitbox-death-all: " + AkronModule.Settings.HitboxShowAllOnDeath.ToString().ToLowerInvariant());
        Log("hitbox-death-player-marker: " + AkronModule.Settings.HitboxShowDeathPlayerMarker.ToString().ToLowerInvariant());
    }

    [Command("akron_hitbox_style", "control hitbox style: status|reset|trail <on|off>|length <1-240>|trail-opacity <0-100>|thickness <1-8>|outline <on|off>|fill <0-100>|color <target> <RRGGBB>")]
    public static void HitboxStyle(string action = "status", string target = "", string value = "") {
        string normalizedAction = NormalizeToken(action);
        switch (normalizedAction) {
            case "":
            case "status":
                break;
            case "reset":
                AkronModule.Settings.ResetHitboxStyle();
                break;
            case "trail":
            case "showhitboxtrail":
                if (!SetPlainToggle(target, () => AkronModule.Settings.ShowHitboxTrail, enabled => AkronModule.Settings.ShowHitboxTrail = enabled, "show-hitbox-trail")) {
                    return;
                }
                break;
            case "length":
            case "traillength":
                if (!int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trailLength)) {
                    Log("invalid hitbox trail length: " + target);
                    return;
                }
                AkronModule.Settings.HitboxTrailLength = AkronModuleSettings.ClampHitboxTrailLength(trailLength);
                break;
            case "trailopacity":
                if (!int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trailOpacity)) {
                    Log("invalid hitbox trail opacity: " + target);
                    return;
                }
                AkronModule.Settings.HitboxTrailOpacity = AkronModuleSettings.ClampOpacity(trailOpacity);
                break;
            case "thickness":
            case "line":
            case "linewidth":
                if (!float.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out float thickness)) {
                    Log("invalid hitbox thickness: " + target);
                    return;
                }
                AkronModule.Settings.HitboxLineThickness = AkronModuleSettings.ClampHitboxLineThickness(thickness);
                break;
            case "fill":
            case "opacity":
            case "fillopacity":
                if (!int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid hitbox fill opacity: " + target);
                    return;
                }
                AkronModule.Settings.HitboxFillOpacity = AkronModuleSettings.ClampHitboxFillOpacity(opacity);
                break;
            case "outline":
            case "blackoutline":
            case "contrast":
                if (!SetPlainToggle(target, () => AkronModule.Settings.HitboxBlackOutline, enabled => AkronModule.Settings.HitboxBlackOutline = enabled, "hitbox-black-outline")) {
                    return;
                }
                break;
            case "color":
                if (!TrySetHitboxColor(target, value)) {
                    return;
                }
                break;
            default:
                Log("unknown hitbox style action: " + action);
                return;
        }

        LogHitboxStyleSettings();
    }

    private static bool TrySetHitboxColor(string target, string value) {
        if (!TryParseRgb(value, out int rgb)) {
            Log("invalid hitbox color: " + value);
            return false;
        }

        switch (NormalizeToken(target)) {
            case "player":
            case "madeline":
                AkronModule.Settings.HitboxPlayerColor = rgb;
                return true;
            case "playerhurtbox":
            case "playerhazard":
            case "hurtbox":
                AkronModule.Settings.HitboxPlayerHurtboxColor = rgb;
                return true;
            case "solid":
            case "solids":
                AkronModule.Settings.HitboxSolidColor = rgb;
                return true;
            case "hazard":
            case "hazards":
            case "danger":
                AkronModule.Settings.HitboxHazardColor = rgb;
                return true;
            case "trigger":
            case "triggers":
                AkronModule.Settings.HitboxTriggerColor = rgb;
                return true;
            case "other":
            case "entity":
            case "entities":
                AkronModule.Settings.HitboxOtherColor = rgb;
                return true;
            case "death":
            case "deathobject":
                AkronModule.Settings.HitboxDeathColor = rgb;
                return true;
            case "deathplayer":
            case "playermarker":
            case "marker":
                AkronModule.Settings.HitboxDeathPlayerColor = rgb;
                return true;
            default:
                Log("unknown hitbox color target: " + target);
                return false;
        }
    }

    private static void LogHitboxStyleSettings() {
        Log("show-hitbox-trail: " + AkronModule.Settings.ShowHitboxTrail.ToString().ToLowerInvariant());
        Log("hitbox-trail-length: " + AkronModuleSettings.ClampHitboxTrailLength(AkronModule.Settings.HitboxTrailLength).ToString(CultureInfo.InvariantCulture));
        Log("hitbox-trail-opacity: " + AkronModuleSettings.ClampOpacity(AkronModule.Settings.HitboxTrailOpacity).ToString(CultureInfo.InvariantCulture));
        Log("hitbox-line-thickness: " + AkronModuleSettings.ClampHitboxLineThickness(AkronModule.Settings.HitboxLineThickness).ToString("0.#", CultureInfo.InvariantCulture));
        Log("hitbox-fill-opacity: " + AkronModuleSettings.ClampHitboxFillOpacity(AkronModule.Settings.HitboxFillOpacity).ToString(CultureInfo.InvariantCulture));
        Log("hitbox-black-outline: " + AkronModule.Settings.HitboxBlackOutline.ToString().ToLowerInvariant());
        Log("hitbox-color-player: " + FormatRgb(AkronModule.Settings.HitboxPlayerColor));
        Log("hitbox-color-player-hurtbox: " + FormatRgb(AkronModule.Settings.HitboxPlayerHurtboxColor));
        Log("hitbox-color-solid: " + FormatRgb(AkronModule.Settings.HitboxSolidColor));
        Log("hitbox-color-hazard: " + FormatRgb(AkronModule.Settings.HitboxHazardColor));
        Log("hitbox-color-trigger: " + FormatRgb(AkronModule.Settings.HitboxTriggerColor));
        Log("hitbox-color-other: " + FormatRgb(AkronModule.Settings.HitboxOtherColor));
        Log("hitbox-color-death: " + FormatRgb(AkronModule.Settings.HitboxDeathColor));
        Log("hitbox-color-death-player: " + FormatRgb(AkronModule.Settings.HitboxDeathPlayerColor));
    }
}
