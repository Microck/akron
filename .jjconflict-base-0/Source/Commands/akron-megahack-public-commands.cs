using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_megahack_public", "control MegaHack-public equivalents: overlay-opacity|pause-buffering|auto-kill-seconds|transition-speed|trail|trail-cutting-rate|jump-hack|jump-infinite|jump-extra-jumps|respawn-time|respawn-seconds|respawn-ignore-speedhack|pause-countdown-hide-tint|show-trajectory-*|show-trajectory-map-aware|hair-color|confirm-*")]
    public static void MegaHackPublic(string action = "status", string value = "") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "overlayopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid overlay opacity: " + value);
                    return;
                }
                AkronModule.Settings.OverlayOpacity = AkronModuleSettings.ClampOverlayOpacity(opacity);
                break;
            case "pausebuffering":
                if (!TryParseBoolean(value, out bool pauseBuffering)) {
                    Log("invalid pause-buffering toggle: " + value);
                    return;
                }
                AkronModule.Settings.AllowPauseBuffering = pauseBuffering;
                break;
            case "autokillseconds":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int autoKillSeconds)) {
                    Log("invalid auto-kill seconds: " + value);
                    return;
                }
                AkronModule.Settings.AutoKillSeconds = AkronModuleSettings.ClampAutoKillSeconds(autoKillSeconds);
                break;
            case "transitionspeed":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float transitionSpeed)) {
                    Log("invalid transition speed: " + value);
                    return;
                }
                AkronModule.Settings.TransitionSpeedMultiplier = AkronModuleSettings.ClampTransitionSpeedMultiplier(transitionSpeed);
                break;
            case "trail":
                if (!TryParseTrailVisibility(value, out AkronTrailVisibility trailVisibility)) {
                    Log("invalid trail visibility: " + value);
                    return;
                }
                AkronModule.Settings.TrailVisibility = trailVisibility;
                AkronModule.Settings.SetNoTrails(trailVisibility == AkronTrailVisibility.Hidden);
                break;
            case "trailcuttingrate":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trailCuttingRate)) {
                    Log("invalid trail cutting rate: " + value);
                    return;
                }
                AkronModule.Settings.TrailCuttingRate = AkronModuleSettings.ClampTrailCuttingRate(trailCuttingRate);
                break;
            case "jumphack":
                if (!TryParseBoolean(value, out bool jumpHack)) {
                    Log("invalid jump-hack toggle: " + value);
                    return;
                }
                if (jumpHack && !AkronModule.TryUse(AkronFeatureKind.MovementStatMutation)) {
                    Log("jump-hack: blocked");
                    return;
                }
                AkronModule.Settings.JumpHack = jumpHack;
                break;
            case "jumpinfinite":
                if (!TryParseBoolean(value, out bool jumpInfinite)) {
                    Log("invalid jump-infinite toggle: " + value);
                    return;
                }
                AkronModule.Settings.JumpHackInfinite = jumpInfinite;
                break;
            case "jumpextrajumps":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int extraJumps)) {
                    Log("invalid jump extra jumps: " + value);
                    return;
                }
                AkronModule.Settings.JumpHackExtraJumps = AkronModuleSettings.ClampJumpHackExtraJumps(extraJumps);
                break;
            case "respawntime":
                if (!TryParseBoolean(value, out bool respawnTime)) {
                    Log("invalid respawn-time toggle: " + value);
                    return;
                }
                if (respawnTime && !AkronModule.TryUse(AkronFeatureKind.RespawnTime)) {
                    Log("respawn-time: blocked");
                    return;
                }
                AkronModule.Settings.RespawnTimeModifier = respawnTime;
                break;
            case "respawnseconds":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float respawnSeconds)) {
                    Log("invalid respawn seconds: " + value);
                    return;
                }
                AkronModule.Settings.RespawnTimeSeconds = AkronModuleSettings.ClampRespawnTimeSeconds(respawnSeconds);
                break;
            case "respawnignorespeedhack":
                if (!TryParseBoolean(value, out bool ignoreSpeedhack)) {
                    Log("invalid respawn ignore-speedhack toggle: " + value);
                    return;
                }
                AkronModule.Settings.RespawnTimeIgnoreSpeedhack = ignoreSpeedhack;
                break;
            case "hidepausemenu":
                if (!TryParseBoolean(value, out bool hidePauseMenu)) {
                    Log("invalid hide-pause-menu toggle: " + value);
                    return;
                }
                if (hidePauseMenu && !AkronModule.TryUse(AkronFeatureKind.PauseMenuVisibility)) {
                    Log("hide-pause-menu: blocked");
                    return;
                }
                AkronModule.Settings.HidePauseMenu = hidePauseMenu;
                break;
            case "pausecountdown":
                if (!TryParseBoolean(value, out bool pauseCountdown)) {
                    Log("invalid pause-countdown toggle: " + value);
                    return;
                }
                if (pauseCountdown && !AkronModule.TryUse(AkronFeatureKind.PauseCountdown)) {
                    Log("pause-countdown: blocked");
                    return;
                }
                AkronModule.Settings.PauseCountdown = pauseCountdown;
                break;
            case "pausecountdownseconds":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pauseCountdownSeconds)) {
                    Log("invalid pause countdown seconds: " + value);
                    return;
                }
                AkronModule.Settings.PauseCountdownSeconds = AkronModuleSettings.ClampPauseCountdownSeconds(pauseCountdownSeconds);
                break;
            case "pausecountdownhidetint":
                if (!TryParseBoolean(value, out bool hideCountdownTint)) {
                    Log("invalid pause countdown hide-tint toggle: " + value);
                    return;
                }
                AkronModule.Settings.PauseCountdownHidePauseTint = hideCountdownTint;
                break;
            case "showtrajectory":
                if (!TryParseBoolean(value, out bool showTrajectory)) {
                    Log("invalid show-trajectory toggle: " + value);
                    return;
                }
                if (showTrajectory && !AkronModule.TryUse(AkronFeatureKind.ShowTrajectory)) {
                    Log("show-trajectory: blocked");
                    return;
                }
                AkronModule.Settings.ShowTrajectory = showTrajectory;
                break;
            case "showtrajectoryframes":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trajectoryFrames)) {
                    Log("invalid show trajectory frames: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryFrames = AkronModuleSettings.ClampShowTrajectoryFrames(trajectoryFrames);
                break;
            case "showtrajectorypresscolor":
                if (!TryParseRgb(value, out int trajectoryPressColor)) {
                    Log("invalid show trajectory press color: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryPressColor = trajectoryPressColor;
                break;
            case "showtrajectoryreleasecolor":
                if (!TryParseRgb(value, out int trajectoryReleaseColor)) {
                    Log("invalid show trajectory release color: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryReleaseColor = trajectoryReleaseColor;
                break;
            case "showtrajectoryendcolor":
                if (!TryParseRgb(value, out int trajectoryEndColor)) {
                    Log("invalid show trajectory end color: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryEndMarkerColor = trajectoryEndColor;
                AkronModule.Settings.ShowTrajectoryUseHitboxColor = false;
                break;
            case "showtrajectoryusehitboxcolor":
                if (!TryParseBoolean(value, out bool useHitboxColor)) {
                    Log("invalid show trajectory use-hitbox-color toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryUseHitboxColor = useHitboxColor;
                break;
            case "showtrajectorylines":
                if (!TryParseBoolean(value, out bool trajectoryLines)) {
                    Log("invalid show trajectory lines toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryLines = trajectoryLines;
                break;
            case "showtrajectorylineshadow":
                if (!TryParseBoolean(value, out bool trajectoryLineShadow)) {
                    Log("invalid show trajectory line-shadow toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryLineShadow = trajectoryLineShadow;
                break;
            case "showtrajectorypointmarkers":
                if (!TryParseBoolean(value, out bool trajectoryPointMarkers)) {
                    Log("invalid show trajectory point-markers toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryPointMarkers = trajectoryPointMarkers;
                break;
            case "showtrajectorystartmarker":
                if (!TryParseBoolean(value, out bool trajectoryStartMarker)) {
                    Log("invalid show trajectory start-marker toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryStartMarker = trajectoryStartMarker;
                break;
            case "showtrajectoryendmarkers":
                if (!TryParseBoolean(value, out bool trajectoryEndMarkers)) {
                    Log("invalid show trajectory end-markers toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryEndMarkers = trajectoryEndMarkers;
                break;
            case "showtrajectoryframehitboxes":
                if (!TryParseBoolean(value, out bool trajectoryFrameHitboxes)) {
                    Log("invalid show trajectory frame-hitboxes toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryFrameHitboxes = trajectoryFrameHitboxes;
                break;
            case "showtrajectoryframehitboxinterval":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trajectoryFrameHitboxInterval)) {
                    Log("invalid show trajectory frame-hitbox interval: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryFrameHitboxInterval = AkronModuleSettings.ClampShowTrajectoryFrameHitboxInterval(trajectoryFrameHitboxInterval);
                break;
            case "showtrajectoryhitboxoutlines":
                if (!TryParseBoolean(value, out bool trajectoryHitboxOutlines)) {
                    Log("invalid show trajectory hitbox-outlines toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryHitboxOutlines = trajectoryHitboxOutlines;
                break;
            case "showtrajectoryhitboxfill":
                if (!TryParseBoolean(value, out bool trajectoryHitboxFill)) {
                    Log("invalid show trajectory hitbox-fill toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryHitboxFill = trajectoryHitboxFill;
                break;
            case "showtrajectoryopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trajectoryOpacity)) {
                    Log("invalid show trajectory opacity: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryOpacity = AkronModuleSettings.ClampOpacity(trajectoryOpacity);
                break;
            case "showtrajectorythickness":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trajectoryThickness)) {
                    Log("invalid show trajectory thickness: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryLineThickness = AkronModuleSettings.ClampShowTrajectoryLineThickness(trajectoryThickness);
                break;
            case "showtrajectorymapaware":
                if (!TryParseBoolean(value, out bool trajectoryMapAware)) {
                    Log("invalid show trajectory map-aware toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryMapAware = trajectoryMapAware;
                break;
            case "showtrajectorystoponsolids":
                if (!TryParseBoolean(value, out bool trajectoryStopOnSolids)) {
                    Log("invalid show trajectory stop-on-solids toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryStopOnSolids = trajectoryStopOnSolids;
                break;
            case "showtrajectorystoponhazards":
                if (!TryParseBoolean(value, out bool trajectoryStopOnHazards)) {
                    Log("invalid show trajectory stop-on-hazards toggle: " + value);
                    return;
                }
                AkronModule.Settings.ShowTrajectoryStopOnHazards = trajectoryStopOnHazards;
                break;
            case "haircolor":
                if (!TryParseRgb(value, out int hairColor)) {
                    Log("invalid hair color: " + value);
                    return;
                }
                AkronModule.Settings.MadelineColors = true;
                AkronModule.Settings.MadelineColorOneDash = true;
                AkronModule.Settings.MadelineOneDashColor = hairColor;
                break;
            case "confirmretry":
                if (!TryParseBoolean(value, out bool confirmRetry)) {
                    Log("invalid confirm-retry toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmRetry = confirmRetry;
                break;
            case "confirmrestart":
                if (!TryParseBoolean(value, out bool confirmRestart)) {
                    Log("invalid confirm-restart toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmRestart = confirmRestart;
                break;
            case "confirmreloadroom":
                if (!TryParseBoolean(value, out bool confirmReloadRoom)) {
                    Log("invalid confirm-reload-room toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmReloadRoom = confirmReloadRoom;
                break;
            case "confirmreloadchapter":
                if (!TryParseBoolean(value, out bool confirmReloadChapter)) {
                    Log("invalid confirm-reload-chapter toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmReloadChapter = confirmReloadChapter;
                break;
            case "confirmfullreset":
                if (!TryParseBoolean(value, out bool confirmFullReset)) {
                    Log("invalid confirm-full-reset toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmFullReset = confirmFullReset;
                break;
            case "confirmloadstate":
                if (!TryParseBoolean(value, out bool confirmLoadState)) {
                    Log("invalid confirm-load-state toggle: " + value);
                    return;
                }
                AkronModule.Settings.ConfirmLoadState = confirmLoadState;
                break;
            default:
                Log("unknown megahack-public action: " + action);
                return;
        }

        Log("overlay-opacity: " + AkronModule.Settings.OverlayOpacity.ToString(CultureInfo.InvariantCulture));
        Log("allow-pause-buffering: " + AkronModule.Settings.AllowPauseBuffering.ToString().ToLowerInvariant());
        Log("auto-kill-seconds: " + AkronModule.Settings.AutoKillSeconds.ToString(CultureInfo.InvariantCulture));
        Log("transition-speed: " + AkronModule.Settings.TransitionSpeedMultiplier.ToString("0.0x", CultureInfo.InvariantCulture));
        Log("trail-visibility: " + AkronModule.Settings.TrailVisibility);
        Log("trail-cutting-rate: " + AkronModule.Settings.TrailCuttingRate.ToString(CultureInfo.InvariantCulture));
        Log("jump-hack: " + AkronModule.Settings.JumpHack.ToString().ToLowerInvariant());
        Log("jump-hack-infinite: " + AkronModule.Settings.JumpHackInfinite.ToString().ToLowerInvariant());
        Log("jump-hack-extra-jumps: " + AkronModule.Settings.JumpHackExtraJumps.ToString(CultureInfo.InvariantCulture));
        Log("respawn-time: " + AkronModule.Settings.RespawnTimeModifier.ToString().ToLowerInvariant());
        Log("respawn-time-seconds: " + AkronModule.Settings.RespawnTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture));
        Log("respawn-time-ignore-speedhack: " + AkronModule.Settings.RespawnTimeIgnoreSpeedhack.ToString().ToLowerInvariant());
        Log("hide-pause-menu: " + AkronModule.Settings.HidePauseMenu.ToString().ToLowerInvariant());
        Log("pause-countdown: " + AkronModule.Settings.PauseCountdown.ToString().ToLowerInvariant());
        Log("pause-countdown-seconds: " + AkronModule.Settings.PauseCountdownSeconds.ToString("0.0", CultureInfo.InvariantCulture));
        Log("pause-countdown-hide-tint: " + AkronModule.Settings.PauseCountdownHidePauseTint.ToString().ToLowerInvariant());
        Log("show-trajectory: " + AkronModule.Settings.ShowTrajectory.ToString().ToLowerInvariant());
        Log("show-trajectory-frames: " + AkronModule.Settings.ShowTrajectoryFrames.ToString(CultureInfo.InvariantCulture));
        Log("show-trajectory-press-color: " + FormatRgb(AkronModule.Settings.ShowTrajectoryPressColor));
        Log("show-trajectory-release-color: " + FormatRgb(AkronModule.Settings.ShowTrajectoryReleaseColor));
        Log("show-trajectory-end-color: " + FormatRgb(AkronModule.Settings.ShowTrajectoryEndMarkerColor));
        Log("show-trajectory-use-hitbox-color: " + AkronModule.Settings.ShowTrajectoryUseHitboxColor.ToString().ToLowerInvariant());
        Log("show-trajectory-lines: " + AkronModule.Settings.ShowTrajectoryLines.ToString().ToLowerInvariant());
        Log("show-trajectory-line-shadow: " + AkronModule.Settings.ShowTrajectoryLineShadow.ToString().ToLowerInvariant());
        Log("show-trajectory-point-markers: " + AkronModule.Settings.ShowTrajectoryPointMarkers.ToString().ToLowerInvariant());
        Log("show-trajectory-start-marker: " + AkronModule.Settings.ShowTrajectoryStartMarker.ToString().ToLowerInvariant());
        Log("show-trajectory-end-markers: " + AkronModule.Settings.ShowTrajectoryEndMarkers.ToString().ToLowerInvariant());
        Log("show-trajectory-frame-hitboxes: " + AkronModule.Settings.ShowTrajectoryFrameHitboxes.ToString().ToLowerInvariant());
        Log("show-trajectory-frame-hitbox-interval: " + AkronModule.Settings.ShowTrajectoryFrameHitboxInterval.ToString(CultureInfo.InvariantCulture));
        Log("show-trajectory-hitbox-outlines: " + AkronModule.Settings.ShowTrajectoryHitboxOutlines.ToString().ToLowerInvariant());
        Log("show-trajectory-hitbox-fill: " + AkronModule.Settings.ShowTrajectoryHitboxFill.ToString().ToLowerInvariant());
        Log("show-trajectory-opacity: " + AkronModule.Settings.ShowTrajectoryOpacity.ToString(CultureInfo.InvariantCulture));
        Log("show-trajectory-thickness: " + AkronModule.Settings.ShowTrajectoryLineThickness.ToString(CultureInfo.InvariantCulture));
        Log("show-trajectory-map-aware: " + AkronModule.Settings.ShowTrajectoryMapAware.ToString().ToLowerInvariant());
        Log("show-trajectory-stop-on-solids: " + AkronModule.Settings.ShowTrajectoryStopOnSolids.ToString().ToLowerInvariant());
        Log("show-trajectory-stop-on-hazards: " + AkronModule.Settings.ShowTrajectoryStopOnHazards.ToString().ToLowerInvariant());
        Log("madeline-colors: " + AkronModule.Settings.MadelineColors.ToString().ToLowerInvariant());
        Log("madeline-one-dash-color: " + FormatRgb(AkronModule.Settings.MadelineOneDashColor));
        Log("confirm-retry: " + AkronModule.Settings.ConfirmRetry.ToString().ToLowerInvariant());
        Log("confirm-restart: " + (AkronModule.Settings.ConfirmRestart || AkronModule.Settings.ConfirmRetry).ToString().ToLowerInvariant());
        Log("confirm-reload-room: " + AkronModule.Settings.ConfirmReloadRoom.ToString().ToLowerInvariant());
        Log("confirm-reload-chapter: " + AkronModule.Settings.ConfirmReloadChapter.ToString().ToLowerInvariant());
        Log("confirm-full-reset: " + (AkronModule.Settings.ConfirmFullReset || AkronModule.Settings.ConfirmReloadChapter).ToString().ToLowerInvariant());
        Log("confirm-load-state: " + AkronModule.Settings.ConfirmLoadState.ToString().ToLowerInvariant());
    }
}
