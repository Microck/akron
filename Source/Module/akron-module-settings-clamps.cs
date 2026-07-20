using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    public static int ClampNoclipSpeed(int speed) {
        return speed <= 0 ? 240 : ClampValue(speed, 20, 900);
    }

    public static int ClampNoclipFloatSpeed(int speed) {
        return speed <= 0 ? 90 : ClampValue(speed, 10, 450);
    }

    public static int ClampNoclipAccuracyInvalidLimit(int limit) {
        return ClampValue(limit, 0, 999);
    }

    public static int ClampNoclipAccuracyTintDurationMs(int durationMs) {
        return durationMs < 0 ? 0 : ClampValue(durationMs, 0, 5000);
    }

    public static bool ShouldCountHazardAccuracySample(bool invalidContact, bool playerMoved) {
        return invalidContact || playerMoved;
    }

    public static AkronDashRedirectDirection NormalizeDashRedirectDirections(AkronDashRedirectDirection directions) {
        AkronDashRedirectDirection normalized = directions & AkronDashRedirectDirection.All;
        return normalized == AkronDashRedirectDirection.None
            ? AkronDashRedirectDirection.Down
            : normalized;
    }

    public void ResetHazardAccuracyDefaults() {
        NoclipAccuracyInvalidLimit = DefaultHazardAccuracyInvalidLimit;
        NoclipAccuracyTint = DefaultHazardAccuracyTint;
        NoclipAccuracyTintMode = DefaultHazardAccuracyTintMode;
        NoclipAccuracyTintColor = DefaultHazardAccuracyTintColor;
        NoclipAccuracyTintOpacity = DefaultHazardAccuracyTintOpacity;
        NoclipAccuracyTintDurationMs = DefaultHazardAccuracyTintDurationMs;
    }

    public static string NormalizeDeathStatsFormat(string format) {
        string normalized = string.IsNullOrWhiteSpace(format) ? DefaultDeathStatsFormat : format.Trim();
        if (normalized.Contains("{0}") || normalized.Contains("{1}")) {
            try {
                normalized = string.Format(normalized, "$C", "$B");
            }
            catch {
                normalized = DefaultDeathStatsFormat;
            }
        }

        return normalized.Length > 48 ? normalized.Substring(0, 48) : normalized;
    }

    public static float ClampHitboxLineThickness(float thickness) {
        return thickness <= 0f ? 1f : ClampValue(thickness, 1f, 8f);
    }

    public static int ClampHitboxFillOpacity(int opacity) {
        return ClampValue(opacity, 0, 100);
    }

    public static int ClampHitboxTrailLength(int length) {
        return ClampValue(length <= 0 ? 30 : length, 1, 240);
    }

    public static int ClampOverlayOpacity(int opacity) {
        return ClampValue(opacity, 55, 100);
    }

    public static int ClampOverlayScale(int scale) {
        return ClampValue(scale <= 0 ? 100 : scale, 75, 150);
    }

    public static int ClampOverlayBlur(int blur) {
        return ClampValue(blur, 0, 100);
    }

    public static int ClampOverlayAnimationMs(int milliseconds) {
        return ClampValue(milliseconds, 0, 500);
    }

    public static AkronLoggingLevel NormalizeLoggingLevel(AkronLoggingLevel level) {
        return Enum.IsDefined(typeof(AkronLoggingLevel), level) ? level : AkronLoggingLevel.Diagnostic;
    }

    public static AkronInvincibilityMode NormalizeInvincibilityMode(AkronInvincibilityMode mode) {
        return mode == AkronInvincibilityMode.Native
            ? AkronInvincibilityMode.Native
            : AkronInvincibilityMode.Akron;
    }

    public static int ClampLoggingMaxFileSizeMb(int sizeMb) {
        return ClampValue(sizeMb <= 0 ? 5 : sizeMb, 1, 100);
    }

    public static int ClampLoggingRetainedFiles(int files) {
        return ClampValue(files < 0 ? 5 : files, 0, 20);
    }

    public static void ClearOneShotRuntimeActions(AkronModuleSettings settings) {
        if (settings == null) {
            return;
        }

        // Deload simulation mutates timers immediately. It must not survive a
        // settings reload, otherwise opening a room after restart replays the
        // simulation and corrupts journal time.
        settings.DeloadSpinners = false;
    }

    public static int ClampPercent(int value, int minimum = 10, int maximum = 300) {
        return ClampValue(value <= 0 ? 100 : value, minimum, maximum);
    }

    public static int ClampOpacity(int opacity) {
        return ClampValue(opacity, 0, 100);
    }

    public static int ClampScreenshotScale(int scale) {
        return ClampValue(scale <= 0 ? 1 : scale, 1, 16);
    }

    public static int ClampFastLookoutMultiplier(int multiplier) {
        return ClampValue(multiplier <= 0 ? 3 : multiplier, 1, 10);
    }

    public static int ClampCameraOffset(int offset) {
        return ClampValue(offset, -20, 20);
    }

    public static int ClampCursorZoomPercent(int percent) {
        return ClampCursorZoomPercent(percent, allowZoomOut: false);
    }

    public static int ClampCursorZoomPercent(int percent, bool allowZoomOut) {
        return ClampValue(percent <= 0 ? 100 : percent, allowZoomOut ? 25 : 100, 32000);
    }

    public static int ClampCursorZoomStepPercent(int percent) {
        return ClampValue(percent <= 0 ? 10 : percent, 1, 100);
    }

    public static AkronCursorZoomActivationMode NormalizeCursorZoomActivationMode(AkronCursorZoomActivationMode mode) {
        return mode == AkronCursorZoomActivationMode.Toggle
            ? AkronCursorZoomActivationMode.Toggle
            : AkronCursorZoomActivationMode.Hold;
    }

    public static AkronCursorToolsClickAction NormalizeCursorToolsClickAction(AkronCursorToolsClickAction action) {
        return action == AkronCursorToolsClickAction.InspectorPin
            ? AkronCursorToolsClickAction.InspectorPin
            : AkronCursorToolsClickAction.ClickTeleport;
    }

    public static int ClampGoldenTransparencyOpacity(int opacity) {
        return ClampOpacity(opacity <= 0 ? 55 : opacity);
    }

    public static int ClampLagPauserThresholdMs(int thresholdMs) {
        return ClampValue(thresholdMs <= 0 ? 250 : thresholdMs, 50, 5000);
    }

    public static int ClampLagPauserWindowMs(int windowMs) {
        return ClampValue(windowMs, 0, 5000);
    }

    public static int ClampScreenshotScannerWaitFrames(int frames) {
        return ClampValue(frames, 0, 240);
    }

    public static int ClampScreenshotScannerOffsetTiles(int tiles) {
        return ClampValue(tiles, 1, 80);
    }

    public static AkronScreenshotImageFormat NormalizeScreenshotScannerImageFormat(AkronScreenshotImageFormat format) {
        return Enum.IsDefined(typeof(AkronScreenshotImageFormat), format)
            ? format
            : AkronScreenshotImageFormat.Png;
    }

    public static int ClampAutosaveIntervalSeconds(int seconds) {
        return ClampValue(seconds, 1, 600);
    }

    public static int ClampAutosaveMinimumDelaySeconds(int seconds) {
        return ClampValue(seconds, 0, 3600);
    }

    public static int ClampSoundVolumePercent(int volume) {
        return ClampValue(volume, 0, 200);
    }

    public static string NormalizeScreenshotScannerExportPath(string path) {
        path = (path ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(path) ? DefaultScreenshotScannerExportPath : path;
    }

    public static int ClampRgb(int rgb) {
        return ClampValue(rgb, 0, 0xFFFFFF);
    }

    public static float ClampAudioMultiplier(float multiplier) {
        return multiplier <= 0f ? 1f : ClampValue(multiplier, 0.1f, 4f);
    }

    public static int ClampFpsTarget(int target) {
        return ClampValue(target <= 0 ? 120 : target, 60, 480);
    }

    public static int ClampTpsTarget(int target) {
        return ClampValue(target <= 0 ? 60 : target, 30, 480);
    }

    public static AkronFrameBypassRates ResolveFrameBypassRates(bool fpsBypass, int fpsTarget, bool tpsBypass, int tpsTarget, AkronFrameIncreaseMethod method) {
        if (!fpsBypass && !tpsBypass) {
            return new AkronFrameBypassRates(false, 60, 60, 60);
        }

        int updateRate = tpsBypass ? ClampTpsTarget(tpsTarget) : 60;
        int requestedDrawRate = fpsBypass ? ClampFpsTarget(fpsTarget) : updateRate;
        int drawRate = System.Math.Max(requestedDrawRate, updateRate);
        if (method == AkronFrameIncreaseMethod.Interval && drawRate % updateRate != 0) {
            int roundedDrawRate = ((drawRate + updateRate - 1) / updateRate) * updateRate;
            drawRate = roundedDrawRate > 480 ? updateRate : roundedDrawRate;
        }

        return new AkronFrameBypassRates(true, updateRate, drawRate, requestedDrawRate);
    }

    public static int ClampFreeCameraSpeed(int speed) {
        return ClampValue(speed <= 0 ? 240 : speed, 20, 2000);
    }

    public static int ClampCustomLabelIndex(int index, int count) {
        return ClampValue(index, 0, System.Math.Max(0, count - 1));
    }

    public static int ClampCustomLabelPadding(int padding) {
        return ClampValue(padding < 0 ? 5 : padding, 0, 240);
    }

    public static int ClampCustomLabelGap(int gap) {
        return ClampValue(gap < 0 ? 8 : gap, 0, 120);
    }

    public static int ClampCustomLabelLineSpacing(int lineSpacing) {
        return ClampValue(lineSpacing <= 0 ? 100 : lineSpacing, 50, 250);
    }

    public static int ClampCustomLabelObstructionPaddingPixels(int pixels) {
        return ClampValue(pixels, 0, 400);
    }

    public static AkronLabelObstructionMode NormalizeCustomLabelObstructionMode(AkronLabelObstructionMode mode) {
        return mode == AkronLabelObstructionMode.Move
            ? AkronLabelObstructionMode.Move
            : AkronLabelObstructionMode.Fade;
    }

    public static AkronHudAnchor NormalizeCustomLabelObstructedAnchor(AkronHudAnchor anchor) {
        return anchor == AkronHudAnchor.Absolute ? AkronHudAnchor.BottomRight : anchor;
    }

    public static int ClampStaminaHudOffset(int offset) {
        return ClampValue(offset, -600, 600);
    }

    public static int ClampDashHudOffset(int offset) {
        return ClampValue(offset, -600, 600);
    }

    public static int ClampResourcePlayerOffset(int offset) {
        return ClampValue(offset, -300, 300);
    }

    public static int ClampResourcePlayerScale(int scale) {
        return scale <= 0 ? 100 : ClampValue(scale, 50, 300);
    }

    public static int ClampDashCountOverride(int dashes) {
        return ClampValue(dashes, 0, 5);
    }

    public static int ClampSetInventoryDashes(int dashes) {
        return ClampValue(dashes, 0, 5);
    }

    public static int ClampSetInventoryJumps(int jumps) {
        return ClampValue(jumps, 0, 99);
    }

    public static AkronCoreModeOverride NormalizeCoreModeOverride(AkronCoreModeOverride mode) {
        return Enum.IsDefined(typeof(AkronCoreModeOverride), mode)
            ? mode
            : AkronCoreModeOverride.Hot;
    }

    public static AkronCoreModeClickBehavior NormalizeCoreModeClickBehavior(AkronCoreModeClickBehavior behavior) {
        return Enum.IsDefined(typeof(AkronCoreModeClickBehavior), behavior)
            ? behavior
            : AkronCoreModeClickBehavior.Toggle;
    }

    public static int ClampDashNumberOffsetY(int offset) {
        return ClampValue(offset, -96, 96);
    }

    public static int ClampSpeedNumberOffsetY(int offset) {
        return ClampValue(offset, -128, 128);
    }

    public static int ClampAutoKillSeconds(int seconds) {
        return ClampValue(seconds, 1, 60 * 60);
    }

    public static int ClampAutoKillAreaSize(int size) {
        return ClampValue(size, 0, 10000);
    }

    public static int ClampAutoKillSpeed(int speed) {
        return ClampValue(speed, 0, 5000);
    }

    public static int ClampAutoKillDashCount(int dashes) {
        return ClampValue(dashes, 0, 5);
    }

    public static int ClampAutoKillPlayerState(int state) {
        return ClampValue(state, 0, 99);
    }

    public static AkronAutoKillGroundCondition NormalizeAutoKillGroundCondition(AkronAutoKillGroundCondition condition) {
        return Enum.IsDefined(typeof(AkronAutoKillGroundCondition), condition)
            ? condition
            : AkronAutoKillGroundCondition.Any;
    }

    public static AkronAutoKillAxisCondition NormalizeAutoKillAxisCondition(AkronAutoKillAxisCondition condition) {
        return Enum.IsDefined(typeof(AkronAutoKillAxisCondition), condition)
            ? condition
            : AkronAutoKillAxisCondition.Any;
    }

    public static int ClampJumpHackExtraJumps(int jumps) {
        return ClampValue(jumps, 1, 99);
    }

    public static float ClampRespawnTimeSeconds(float seconds) {
        return seconds <= 0f ? 0.1f : ClampValue(seconds, 0.1f, 10f);
    }

    public static float ClampPauseCountdownSeconds(float seconds) {
        return seconds <= 0f ? 3f : ClampValue(seconds, 0.1f, 15f);
    }

    public static int ClampShowTrajectoryFrames(int frames) {
        return frames <= 0 ? 300 : ClampValue(frames, 1, 1000);
    }

    public static int ClampShowTrajectoryLineThickness(int thickness) {
        return thickness <= 0 ? 5 : ClampValue(thickness, 1, 12);
    }

    public static int ClampShowTrajectoryFrameHitboxInterval(int interval) {
        return interval <= 0 ? 6 : ClampValue(interval, 1, 60);
    }

    public static float ClampLowVolumeLevel(float volume) {
        return ClampValue(volume, 0f, 10f);
    }

    public static float ClampMadelineGradientSpeed(float speed) {
        return speed <= 0f ? 1f : ClampValue(speed, 0.1f, 10f);
    }

    public static int ClampMadelineHairLength(int length) {
        return length <= 0 ? 4 : ClampValue(length, 1, 100);
    }

    public static AkronMadelineEffectSyncMode NormalizeMadelineEffectSyncMode(AkronMadelineEffectSyncMode mode) {
        return Enum.IsDefined(typeof(AkronMadelineEffectSyncMode), mode)
            ? mode
            : AkronMadelineEffectSyncMode.Off;
    }

    public static AkronDeathParticleColorMode NormalizeDeathParticleColorMode(AkronDeathParticleColorMode mode) {
        return Enum.IsDefined(typeof(AkronDeathParticleColorMode), mode)
            ? mode
            : AkronDeathParticleColorMode.Hair;
    }

    public static AkronDeathParticleShape NormalizeDeathParticleShape(AkronDeathParticleShape shape) {
        return Enum.IsDefined(typeof(AkronDeathParticleShape), shape)
            ? shape
            : AkronDeathParticleShape.Vanilla;
    }

    public static float ClampDeathParticleDurationSeconds(float seconds) {
        return seconds <= 0f ? 0.834f : ClampValue(seconds, 0.1f, 3f);
    }

    public static string NormalizeDeathParticleCustomShape(string shape) {
        if (string.IsNullOrWhiteSpace(shape)) {
            return DefaultDeathParticleCustomShape;
        }

        string normalized = new string(shape.Where(character => character == '0' || character == '1').ToArray());
        if (normalized.Length < 64) {
            normalized = normalized.PadRight(64, '0');
        } else if (normalized.Length > 64) {
            normalized = normalized.Substring(0, 64);
        }

        return normalized.Contains('1') ? normalized : DefaultDeathParticleCustomShape;
    }

    public static float ClampTransitionSpeedMultiplier(float multiplier) {
        return multiplier <= 0f ? 1f : ClampValue(multiplier, 0.1f, 3f);
    }

    public static int ClampTrailCuttingRate(int rate) {
        return rate <= 0 ? 1 : ClampValue(rate, 1, 12);
    }

    public static int ClampScreenshakeIntensity(int intensity) {
        return ClampValue(intensity, 0, 100);
    }

    public static int ClampLightLevelPercent(int percent) {
        return ClampValue(percent, 0, 100);
    }

    public static int ClampBloomLevelPercent(int percent) {
        return ClampValue(percent, 0, 300);
    }

    public static int ClampStartPosDashes(int dashes) {
        return ClampValue(dashes, -1, 5);
    }

    public static int ClampStartPosStaminaPercent(int percent) {
        return ClampValue(percent, -1, 100);
    }

    public static int ClampStartPosSlotCount(int count) {
        return ClampValue(count, 1, 99);
    }

    public static int ClampStartPosSelectableSlotCount(int count) {
        return ClampStartPosSlotCount(Math.Max(count, MinimumStartPosSelectableSlots));
    }

    public static float ClampCustomTrailRainbowSpeed(float speed) {
        return speed <= 0f ? 1f : ClampValue(speed, 0.1f, 10f);
    }

    private static int ClampValue(int value, int minimum, int maximum) {
        if (value < minimum) {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static float ClampValue(float value, float minimum, float maximum) {
        if (value < minimum) {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static List<AkronAutoKillAreaData> CopyAutoKillAreas(IEnumerable<AkronAutoKillAreaData> areas) {
        return (areas ?? Enumerable.Empty<AkronAutoKillAreaData>())
            .Where(area => area != null && area.Width > 0 && area.Height > 0)
            .Select(CopyAutoKillArea)
            .ToList();
    }

    private static AkronAutoKillAreaData CopyAutoKillArea(AkronAutoKillAreaData area) {
        int minSpeed = ClampAutoKillSpeed(area.MinSpeed);
        int maxSpeed = ClampAutoKillSpeed(area.MaxSpeed);
        int minHorizontalSpeed = ClampAutoKillSpeed(area.MinHorizontalSpeed);
        int maxHorizontalSpeed = ClampAutoKillSpeed(area.MaxHorizontalSpeed);
        int minVerticalSpeed = ClampAutoKillSpeed(area.MinVerticalSpeed);
        int maxVerticalSpeed = ClampAutoKillSpeed(area.MaxVerticalSpeed);
        return new AkronAutoKillAreaData {
            X = area.X,
            Y = area.Y,
            Width = ClampAutoKillAreaSize(area.Width),
            Height = ClampAutoKillAreaSize(area.Height),
            SpeedCondition = area.SpeedCondition,
            MinSpeed = minSpeed,
            MaxSpeed = Math.Max(minSpeed, maxSpeed),
            HorizontalSpeedCondition = area.HorizontalSpeedCondition,
            MinHorizontalSpeed = minHorizontalSpeed,
            MaxHorizontalSpeed = Math.Max(minHorizontalSpeed, maxHorizontalSpeed),
            VerticalSpeedCondition = area.VerticalSpeedCondition,
            MinVerticalSpeed = minVerticalSpeed,
            MaxVerticalSpeed = Math.Max(minVerticalSpeed, maxVerticalSpeed),
            DashCountCondition = area.DashCountCondition,
            DashCount = ClampAutoKillDashCount(area.DashCount),
            GroundCondition = NormalizeAutoKillGroundCondition(area.GroundCondition),
            HorizontalDirection = NormalizeAutoKillAxisCondition(area.HorizontalDirection),
            VerticalDirection = NormalizeAutoKillAxisCondition(area.VerticalDirection),
            PlayerStateCondition = area.PlayerStateCondition,
            PlayerState = ClampAutoKillPlayerState(area.PlayerState),
            InvertConditions = area.InvertConditions
        };
    }

    private static List<AkronRectangleData> CopyAutoAreasWithLatest(IEnumerable<AkronRectangleData> areas, int x, int y, int width, int height) {
        List<AkronRectangleData> copied = (areas ?? Enumerable.Empty<AkronRectangleData>())
            .Where(area => area != null && area.Width > 0 && area.Height > 0)
            .Select(area => new AkronRectangleData {
                X = area.X,
                Y = area.Y,
                Width = ClampAutoKillAreaSize(area.Width),
                Height = ClampAutoKillAreaSize(area.Height)
            })
            .ToList();
        // The latest rectangle fields are updated with every GUI/command area
        // selection. Keep them authoritative enough to restore one usable area
        // if a setup snapshot has scalar area data but an empty list.
        if (copied.Count == 0 && width > 0 && height > 0) {
            copied.Add(new AkronRectangleData {
                X = x,
                Y = y,
                Width = ClampAutoKillAreaSize(width),
                Height = ClampAutoKillAreaSize(height)
            });
        }

        return copied;
    }
}
