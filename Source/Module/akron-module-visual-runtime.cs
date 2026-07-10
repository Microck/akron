using System;
using System.Collections.Generic;
using System.Globalization;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    internal static bool ShouldUseCursorToolsHold(bool cursorTools, bool cursorToolsHoldBindingHeld, bool overlayVisible) {
        return cursorTools && cursorToolsHoldBindingHeld && !overlayVisible;
    }

    internal static bool IsCursorToolsHoldActive() {
        return ShouldUseCursorToolsHold(
            Settings.CursorTools,
            Settings.CursorToolsHold?.Check ?? false,
            IsOverlayVisible);
    }

    internal static bool IsClickTeleportEffectiveEnabled() {
        return IsCursorToolEffectiveEnabled(
            Settings.ClickTeleport,
            IsCursorToolsHoldActive(),
            AkronModuleSettings.NormalizeCursorToolsClickAction(Settings.CursorToolsClickAction) == AkronCursorToolsClickAction.ClickTeleport);
    }

    internal static bool IsClickTeleportCursorActive() {
        return IsClickTeleportCursorActive(
            Settings.ClickTeleport,
            Settings.ClickTeleportCursor?.Check ?? false,
            IsCursorToolsHoldActive(),
            AkronModuleSettings.NormalizeCursorToolsClickAction(Settings.CursorToolsClickAction) == AkronCursorToolsClickAction.ClickTeleport);
    }

    internal static bool IsCursorToolsInspectorPinActive() {
        return IsCursorToolsHoldActive() &&
               AkronModuleSettings.NormalizeCursorToolsClickAction(Settings.CursorToolsClickAction) == AkronCursorToolsClickAction.InspectorPin;
    }

    internal static bool IsCursorZoomEffectiveEnabled() {
        return IsCursorToolEffectiveEnabled(Settings.CursorZoom, IsCursorToolsHoldActive(), Settings.CursorToolsCursorZoom);
    }

    internal static bool IsFreeCameraEffectiveEnabled() {
        return IsCursorToolEffectiveEnabled(Settings.FreeCamera, IsCursorToolsHoldActive(), Settings.CursorToolsFreeCamera);
    }

    internal static bool IsFreeCameraMouseControlEffectiveEnabled() {
        return IsFreeCameraMouseControlEffectiveEnabled(
            Settings.FreeCameraMouseControl,
            IsCursorToolsHoldActive(),
            Settings.CursorToolsFreeCamera);
    }

    internal static bool IsCursorToolEffectiveEnabled(bool savedToolEnabled, bool cursorToolsHoldActive, bool cursorToolsOptionEnabled) {
        return savedToolEnabled || cursorToolsHoldActive && cursorToolsOptionEnabled;
    }

    internal static bool IsClickTeleportCursorActive(bool clickTeleportEnabled, bool clickTeleportHoldActive, bool cursorToolsHoldActive, bool cursorToolsClickTeleportEnabled) {
        return clickTeleportEnabled && clickTeleportHoldActive ||
               cursorToolsHoldActive && cursorToolsClickTeleportEnabled;
    }

    internal static bool IsFreeCameraMouseControlEffectiveEnabled(bool freeCameraMouseControlEnabled, bool cursorToolsHoldActive, bool cursorToolsFreeCameraEnabled) {
        return freeCameraMouseControlEnabled ||
               cursorToolsHoldActive &&
               cursorToolsFreeCameraEnabled;
    }

    internal static bool IsCursorToolsFreezeGameplayEffectiveEnabled() {
        return IsCursorToolsHoldActive() &&
               Settings.CursorToolsFreeCamera &&
               Settings.CursorToolsFreezeGameplay;
    }

    private static void ApplyStartPosMousePlacement(Level level, Player player) {
        if (!Settings.StartPosMousePlacement ||
            Overlay?.IsStartPosPlacementActive == true ||
            player == null ||
            player.Dead ||
            IsOverlayVisible ||
            !AkronPolicy.CanUse(AkronFeatureKind.StartPosTools).Allowed) {
            startPosPlacementLastLeftDown = false;
            return;
        }

        MouseState mouse = Mouse.GetState();
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool pressed = leftDown && !startPosPlacementLastLeftDown;
        startPosPlacementLastLeftDown = leftDown;
        if (!pressed) {
            return;
        }

        Vector2 target = AkronScreenProjection.MouseScreenToWorld(level, new Vector2(mouse.X, mouse.Y));
        AkronActions.SetStartPosAtMouse(level, target);
    }

    private static void CaptureClickTeleportTargetBeforeCameraMovement(Level level, Player player) {
        pendingClickTeleportTarget = null;
        if (!IsClickTeleportEffectiveEnabled() ||
            Settings.StartPosMousePlacement ||
            player == null ||
            player.Dead ||
            IsOverlayVisible ||
            !ShouldShowClickTeleportCursor()) {
            clickTeleportLastLeftDown = false;
            return;
        }

        MouseState mouse = Mouse.GetState();
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool pressed = leftDown && !clickTeleportLastLeftDown;
        clickTeleportLastLeftDown = leftDown;
        if (!pressed) {
            return;
        }

        Vector2 target = MouseScreenToWorldForClickTeleport(level, new Vector2(mouse.X, mouse.Y));
        target.X = Calc.Clamp(target.X, level.Bounds.Left, level.Bounds.Right);
        target.Y = Calc.Clamp(target.Y, level.Bounds.Top, level.Bounds.Bottom);
        pendingClickTeleportTarget = target;
    }

    private static void ApplyClickTeleport(Level level, Player player) {
        if (!IsClickTeleportEffectiveEnabled() ||
            Settings.StartPosMousePlacement ||
            player == null ||
            player.Dead ||
            IsOverlayVisible ||
            !ShouldShowClickTeleportCursor()) {
            pendingClickTeleportTarget = null;
            return;
        }

        Vector2? capturedTarget = pendingClickTeleportTarget;
        pendingClickTeleportTarget = null;
        if (!capturedTarget.HasValue || !TryUse(AkronFeatureKind.ClickTeleport)) {
            return;
        }

        Vector2 target = capturedTarget.Value;
        ApplyClickTeleportTarget(level, player, target);
    }

    internal static bool ApplyClickTeleportForQa(Level level, Vector2 mouseScreenPosition, out Vector2 target) {
        target = Vector2.Zero;
        Player player = level?.Tracker.GetEntity<Player>();
        if (!IsClickTeleportEffectiveEnabled() ||
            Settings.StartPosMousePlacement ||
            player == null ||
            player.Dead ||
            IsOverlayVisible ||
            !TryUse(AkronFeatureKind.ClickTeleport)) {
            return false;
        }

        target = MouseScreenToWorldForClickTeleport(level, mouseScreenPosition);
        target.X = Calc.Clamp(target.X, level.Bounds.Left, level.Bounds.Right);
        target.Y = Calc.Clamp(target.Y, level.Bounds.Top, level.Bounds.Bottom);
        ApplyClickTeleportTarget(level, player, target);
        return true;
    }

    private static void ApplyClickTeleportTarget(Level level, Player player, Vector2 target) {
        Vector2 positionBefore = player.Position;
        Vector2 requestedDelta = new Vector2 {
            X = target.X - positionBefore.X,
            Y = target.Y - positionBefore.Y
        };
        player.NaiveMove(requestedDelta);
        MoveHairForTeleport(player, new Vector2 {
            X = player.Position.X - positionBefore.X,
            Y = player.Position.Y - positionBefore.Y
        });
        player.Speed = Vector2.Zero;
        if (ShouldClickTeleportMoveCamera(AkronRuntimeOptions.IsFreeCameraActive(level), IsLevelZoomActive(level))) {
            level.Camera.Position = ClampCameraToRoom(level, player.CameraTarget);
        }
        Engine.Scene?.Add(new AkronToast("Teleported to cursor."));
    }

    internal static bool ShouldClickTeleportMoveCamera(bool freeCameraActive, bool levelZoomActive) {
        return !freeCameraActive && !levelZoomActive;
    }

    internal static bool IsLevelZoomActive(Level level) {
        return level != null && Math.Abs(level.Zoom - 1f) >= 0.001f;
    }

    internal static bool ShouldSuppressFreeCameraMovementForClickTeleport() {
        return pendingClickTeleportTarget.HasValue;
    }

    internal static Vector2 MouseScreenToWorldForClickTeleport(Level level, Vector2 mouseScreenPosition) {
        Vector2 mouseGamePosition = AkronScreenProjection.MouseScreenToGame(mouseScreenPosition);
        float zoom = CurrentClickTeleportLevelZoom(level);
        Vector2 focus = CurrentClickTeleportZoomFocus(level, mouseGamePosition, zoom);
        return AkronScreenProjection.ScreenGameToWorld(
            level,
            AkronScreenProjection.RemoveLevelZoom(mouseGamePosition, zoom, focus));
    }

    private static float CurrentClickTeleportLevelZoom(Level level) {
        if (ShouldShowCursorZoomCursor()) {
            return ClampNativeCursorZoom(Settings.CursorZoomPercent / 100f);
        }

        return level == null || level.Zoom <= 0f ? 1f : level.Zoom;
    }

    private static Vector2 CurrentClickTeleportZoomFocus(Level level, Vector2 mouseGamePosition, float zoom) {
        if (ShouldShowCursorZoomCursor()) {
            return ClampCursorZoomFocus(mouseGamePosition, zoom);
        }

        return level == null ? new Vector2(160f, 90f) : level.ZoomFocusPoint;
    }

    internal static void MoveHairForTeleport(Player player, Vector2 delta) {
        if (player?.Hair == null) {
            return;
        }

        player.Hair.MoveHairBy(delta);
    }

    private static void UpdateCursorZoom(Level level) {
        bool cursorToolsHoldActive = IsCursorToolsHoldActive();
        bool cursorZoomEnabled = IsCursorZoomEffectiveEnabled();
        if (!cursorZoomEnabled || IsOverlayVisible || !AkronPolicy.CanUse(AkronFeatureKind.CursorZoom).Allowed) {
            cursorZoomHadScrollSample = false;
            cursorZoomLastBindDown = false;
            if (!cursorZoomEnabled) {
                cursorZoomToggleActive = false;
                DeactivateCursorZoom(level);
            }
            return;
        }

        bool bindDown = Settings.CursorZoomHold?.Check ?? false;
        bool bindPressed = Settings.CursorZoom && bindDown && !cursorZoomLastBindDown;
        cursorZoomLastBindDown = Settings.CursorZoom ? bindDown : false;

        AkronCursorZoomActivationMode activationMode = AkronModuleSettings.NormalizeCursorZoomActivationMode(Settings.CursorZoomActivationMode);
        bool active = cursorToolsHoldActive || bindDown;
        if (activationMode == AkronCursorZoomActivationMode.Toggle) {
            if (bindPressed) {
                cursorZoomToggleActive = !cursorZoomToggleActive;
                cursorZoomHadScrollSample = false;
            }
            active = cursorToolsHoldActive || cursorZoomToggleActive;
        }

        if (!active) {
            DeactivateCursorZoom(level);
            return;
        }

        if (!TryUse(AkronFeatureKind.CursorZoom)) {
            return;
        }

        MouseState mouse = Mouse.GetState();
        if (cursorZoomHadScrollSample) {
            int scrollDelta = mouse.ScrollWheelValue - cursorZoomLastScrollValue;
            if (scrollDelta != 0) {
                int steps = Math.Sign(scrollDelta) * Math.Max(1, Math.Abs(scrollDelta) / 120);
                Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(
                    Settings.CursorZoomPercent + steps * AkronModuleSettings.ClampCursorZoomStepPercent(Settings.CursorZoomStepPercent),
                    Settings.CursorZoomAllowZoomOut);
            }
        }

        cursorZoomLastScrollValue = mouse.ScrollWheelValue;
        cursorZoomHadScrollSample = true;
        Vector2 mouseScreenPosition = new Vector2(mouse.X, mouse.Y);
        ApplyCursorZoomFrame(level, mouseScreenPosition);
    }

    internal static void ApplyCursorZoomFrame(Level level, Vector2 mouseScreenPosition) {
        if (level == null) {
            return;
        }

        Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(Settings.CursorZoomPercent, Settings.CursorZoomAllowZoomOut);
        float zoom = Settings.CursorZoomPercent / 100f;
        cursorZoomFocusGamePosition = ClampCursorZoomFocus(AkronScreenProjection.MouseScreenToGame(mouseScreenPosition), zoom);
        if (ShouldUseExtendedCameraCursorZoom(zoom, Settings.CursorZoomAllowZoomOut, AkronInterop.ExtendedCameraDynamicsInteropAvailable)) {
            Vector2 worldCenter = CalculateExtendedCameraCursorZoomOutCenter(
                level.Camera.Position,
                level.Zoom,
                level.Tracker.GetEntity<Player>()?.Center);
            if (AkronInterop.TryForceExtendedCameraFocus(level, worldCenter, zoom)) {
                cursorZoomApplied = true;
                cursorZoomOwnedByExtendedCamera = true;
                return;
            }
        }

        zoom = ClampNativeCursorZoom(zoom);
        cursorZoomFocusGamePosition = ClampCursorZoomFocus(AkronScreenProjection.MouseScreenToGame(mouseScreenPosition), zoom);
        level.Zoom = zoom;
        level.ZoomTarget = zoom;
        level.ZoomFocusPoint = cursorZoomFocusGamePosition;
        cursorZoomApplied = true;
        cursorZoomOwnedByExtendedCamera = false;
    }

    private static void DeactivateCursorZoom(Level level) {
        cursorZoomHadScrollSample = false;
        if (!cursorZoomApplied) {
            return;
        }

        if (Settings.CursorZoomResetOnDeactivate) {
            Settings.CursorZoomPercent = 100;
        }

        if (ShouldResetCursorZoomLevelState(cursorZoomApplied, cursorZoomOwnedByExtendedCamera)) {
            ResetCursorZoomLevelState(level);
        } else if (cursorZoomOwnedByExtendedCamera) {
            AkronInterop.TryRestoreExtendedCameraAutomaticZooming();
        }

        cursorZoomApplied = false;
        cursorZoomOwnedByExtendedCamera = false;
    }

    private static Vector2 ClampCursorZoomFocus(Vector2 focus, float zoom) {
        if (zoom <= 1f) {
            return new Vector2(160f, 90f);
        }

        float halfVisibleWidth = 160f / zoom;
        float halfVisibleHeight = 90f / zoom;
        return new Vector2(
            Calc.Clamp(focus.X, halfVisibleWidth, 320f - halfVisibleWidth),
            Calc.Clamp(focus.Y, halfVisibleHeight, 180f - halfVisibleHeight));
    }

    internal static Vector2 CalculateExtendedCameraCursorZoomOutCenter(Vector2 cameraPosition, float currentZoom, Vector2? playerCenter) {
        if (playerCenter.HasValue) {
            return playerCenter.Value;
        }

        float safeZoom = Math.Max(0.001f, currentZoom);
        Vector2 center = default;
        center.X = cameraPosition.X + 160f / safeZoom;
        center.Y = cameraPosition.Y + 90f / safeZoom;
        return center;
    }

    internal static bool ShouldResetCursorZoomLevelState(bool zoomApplied, bool extendedCameraOwnsZoom) {
        return zoomApplied && !extendedCameraOwnsZoom;
    }

    internal static bool ShouldUseExtendedCameraCursorZoom(float zoom, bool allowZoomOut, bool extendedCameraAvailable) {
        return allowZoomOut && zoom < 1f && extendedCameraAvailable;
    }

    internal static float ClampNativeCursorZoom(float zoom) {
        return Math.Max(1f, zoom);
    }

    private static void ResetCursorZoomLevelState(Level level) {
        if (level == null) {
            return;
        }

        level.Zoom = 1f;
        level.ZoomTarget = 1f;
        level.ZoomFocusPoint = new Vector2(160f, 90f);
    }

    internal static void ResetCursorZoom(Level level) {
        cursorZoomHadScrollSample = false;
        bool restoreExtendedCameraZoom = cursorZoomOwnedByExtendedCamera;
        cursorZoomApplied = false;
        cursorZoomOwnedByExtendedCamera = false;
        cursorZoomToggleActive = false;
        cursorZoomLastBindDown = false;
        if (restoreExtendedCameraZoom) {
            AkronInterop.TryRestoreExtendedCameraAutomaticZooming();
            return;
        }

        ResetCursorZoomLevelState(level);
    }

    internal static string DescribeCursorZoom(Level level) {
        string hold = (Settings.CursorZoomHold?.Check ?? false).ToString().ToLowerInvariant();
        string cursorToolsHold = IsCursorToolsHoldActive().ToString().ToLowerInvariant();
        string focus = cursorZoomApplied
            ? cursorZoomFocusGamePosition.X.ToString("0.#", CultureInfo.InvariantCulture) + "," + cursorZoomFocusGamePosition.Y.ToString("0.#", CultureInfo.InvariantCulture)
            : "unset";
        string liveZoom = level == null ? "no-level" : level.Zoom.ToString("0.###", CultureInfo.InvariantCulture);
        string targetZoom = level == null ? "no-level" : level.ZoomTarget.ToString("0.###", CultureInfo.InvariantCulture);
        return (Settings.CursorZoom ? "on" : "off") +
               ";percent=" + AkronModuleSettings.ClampCursorZoomPercent(Settings.CursorZoomPercent, Settings.CursorZoomAllowZoomOut).ToString(CultureInfo.InvariantCulture) +
               ";step=" + AkronModuleSettings.ClampCursorZoomStepPercent(Settings.CursorZoomStepPercent).ToString(CultureInfo.InvariantCulture) +
               ";allow-zoom-out=" + Settings.CursorZoomAllowZoomOut.ToString().ToLowerInvariant() +
               ";reset-on-deactivate=" + Settings.CursorZoomResetOnDeactivate.ToString().ToLowerInvariant() +
               ";mode=" + AkronModuleSettings.NormalizeCursorZoomActivationMode(Settings.CursorZoomActivationMode).ToString().ToLowerInvariant() +
               ";active=" + (cursorZoomApplied || cursorZoomToggleActive).ToString().ToLowerInvariant() +
               ";owner=" + DescribeCursorZoomOwner() +
               ";hold=" + hold +
               ";cursor-tools-hold=" + cursorToolsHold +
               ";zoom=" + liveZoom +
               ";zoom-target=" + targetZoom +
               ";focus=" + focus;
    }

    internal static string DescribeCursorZoomOwner() {
        if (!cursorZoomApplied) {
            return "none";
        }

        return cursorZoomOwnedByExtendedCamera ? "ecd" : "akron";
    }

    private static void ApplyTransitionSpeed(Level level) {
        float multiplier = AkronModuleSettings.ClampTransitionSpeedMultiplier(Settings.TransitionSpeedMultiplier);
        if (multiplier == 1f) {
            return;
        }

        if (!TryUse(AkronFeatureKind.TransitionSpeed)) {
            return;
        }

        level.NextTransitionDuration = TransitionDurationForSpeedMultiplier(multiplier);
    }

    internal static float TransitionDurationForSpeedMultiplier(float multiplier) {
        return 0.65f / AkronModuleSettings.ClampTransitionSpeedMultiplier(multiplier);
    }

    private static void ApplyVisualPlayerOverrides(Player player) {
        if (player == null) {
            return;
        }

        if (!Settings.MadelineColors && player.OverrideHairColor.HasValue) {
            player.OverrideHairColor = null;
        }

        ApplyPlayerVisibilityOverride(player);

        if (Settings.TrailVisibility == AkronTrailVisibility.Hidden) {
            forcedTrailFrame = 0;
            TrailManager.Clear();
        } else if (Settings.TrailVisibility == AkronTrailVisibility.Always || Settings.CustomTrail && TryUse(AkronFeatureKind.CustomTrail)) {
            int cuttingRate = AkronModuleSettings.ClampTrailCuttingRate(Settings.TrailCuttingRate);
            forcedTrailFrame = (forcedTrailFrame + 1) % cuttingRate;
            if (forcedTrailFrame == 0) {
                Color color = ResolveForcedTrailColor(player);
                float opacity = Settings.CustomTrail ? AkronModuleSettings.ClampOpacity(Settings.CustomTrailOpacity) / 100f : 0.45f;
                TrailManager.Add(player, color, opacity);
            }
        } else {
            forcedTrailFrame = 0;
        }
    }

    private static Vector2 ClampCameraToRoom(Level level, Vector2 position) {
        Rectangle bounds = level.Bounds;
        float maxX = Math.Max(bounds.Left, bounds.Right - 320f);
        float maxY = Math.Max(bounds.Top, bounds.Bottom - 180f);
        return new Vector2(
            Calc.Clamp(position.X, bounds.Left, maxX),
            Calc.Clamp(position.Y, bounds.Top, maxY));
    }

    private static void EnsureRespawnCameraContainsPlayer(Level level, Player player) {
        if (level == null || player == null) {
            return;
        }

        // Vanilla respawn can use an explicit Session.RespawnPoint that is outside the
        // current camera, especially after StartPos restores. If the camera stays on
        // the old room slice, the respawned player can immediately fall or die before
        // the camera catches up.
        Vector2 camera = level.Camera.Position;
        Vector2 center = player.Center;
        const float margin = 16f;
        bool outsideCamera =
            center.X < camera.X - margin ||
            center.X > camera.X + 320f + margin ||
            center.Y < camera.Y - margin ||
            center.Y > camera.Y + 180f + margin;
        if (outsideCamera) {
            level.Camera.Position = ClampCameraToRoom(level, player.CameraTarget);
        }
    }

    private static Color ResolveCustomTrailColor(Player player) {
        Color color = ColorFromRgb(Settings.CustomTrailColor);
        if (Settings.CustomTrailMode == AkronCustomTrailMode.Rainbow) {
            float speed = AkronModuleSettings.ClampCustomTrailRainbowSpeed(Settings.CustomTrailRainbowSpeed);
            float hue = ((player.Scene?.TimeActive ?? 0f) * speed) % 1f;
            color = Calc.HsvToColor(hue, 0.85f, 1f);
        }

        if (Settings.CustomTrailPulse) {
            float pulse = 0.55f + 0.45f * (float) Math.Sin((player.Scene?.TimeActive ?? 0f) * MathHelper.TwoPi * 2f);
            color = Color.Lerp(color * 0.55f, color, pulse);
        }

        return color;
    }

    private static Color ColorFromRgb(int rgb) {
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    private static void PlayerOnUpdateHair(On.Celeste.Player.orig_UpdateHair orig, Player self, bool applyGravity) {
        orig(self, applyGravity);
        ApplyMadelineVisualOverrides(self);
    }

    public static bool IsMadelineNoDashColorState(int dashes, int maxDashes) {
        return dashes == 0 && dashes < maxDashes;
    }
}
