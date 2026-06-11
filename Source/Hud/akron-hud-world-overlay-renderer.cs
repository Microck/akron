using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static bool renderingAutomationAreasToGameplayBuffer;

    private static void RenderAutoKillArea(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        bool shouldShowLive = settings.AutoKill &&
                              settings.AutoKillArea &&
                              settings.AutoKillShowArea &&
                              !HasVisibleRecordedAutoKillDeath(settings);
        bool shouldShowPreview = AkronModule.TryGetPracticeAreaSelectionPreview(level, isAutoDeafen: false, out Rectangle preview, out bool hasAnchor);
        if (!shouldShowLive && !shouldShowPreview) {
            return;
        }

        int lineThickness = AutomationAreaGamePixelThickness();
        foreach (Rectangle area in AkronModule.GetAutoKillAreas()) {
            if (area.Width > 0 && area.Height > 0) {
                DrawWorldRect(level, area, Color.OrangeRed, 0.14f, lineThickness);
            }
        }

        if (shouldShowPreview) {
            if (hasAnchor) {
                DrawWorldRect(level, preview, Color.OrangeRed, 0f, lineThickness);
            } else {
                DrawWorldPixelMarker(level, preview, Color.OrangeRed);
            }
        }
    }

    private static void RenderAutoDeafenArea(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (AkronCapture.IsCapturingGameFrame || level == null) {
            return;
        }

        bool shouldShowLive = settings.AutoDeafen && settings.AutoDeafenArea && settings.AutoDeafenShowArea;
        bool shouldShowPreview = AkronModule.TryGetPracticeAreaSelectionPreview(level, isAutoDeafen: true, out Rectangle preview, out bool hasAnchor);
        if (!shouldShowLive && !shouldShowPreview) {
            return;
        }

        int lineThickness = AutomationAreaGamePixelThickness();
        if (shouldShowLive) {
            foreach (Rectangle area in AkronModule.GetAutoDeafenAreas()) {
                if (area.Width > 0 && area.Height > 0) {
                    DrawWorldRect(level, area, Color.DeepSkyBlue, 0.14f, lineThickness);
                }
            }
        }

        if (shouldShowPreview) {
            if (hasAnchor) {
                DrawWorldRect(level, preview, Color.DeepSkyBlue, 0f, lineThickness);
            } else {
                DrawWorldPixelMarker(level, preview, Color.DeepSkyBlue);
            }
        }
    }

    public static void RenderAutomationAreasToGameplayBuffer(Level level) {
        if (AkronCapture.IsCapturingGameFrame ||
            level == null ||
            AkronModule.ShouldHideAkronRenderSurfacesBehindDeathWipe()) {
            return;
        }

        renderingAutomationAreasToGameplayBuffer = true;
        try {
            RenderAutoKillArea(level);
            RenderAutoDeafenArea(level);
        } finally {
            renderingAutomationAreasToGameplayBuffer = false;
        }
    }

    private static void RenderRefillClarity(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (AkronCapture.IsCapturingGameFrame ||
            level == null ||
            !settings.RefillClarity ||
            !AkronPolicy.CanUse(AkronFeatureKind.RefillClarity).Allowed) {
            return;
        }

        Color color = ColorFromRgb(settings.RefillClarityColor);
        float opacity = AkronModuleSettings.ClampOpacity(settings.RefillClarityOpacity) / 100f;
        if (!level.Tracker.Entities.TryGetValue(typeof(Refill), out List<Entity> refillEntities)) {
            return;
        }

        foreach (Refill refill in refillEntities.OfType<Refill>()) {
            if (refill == null || !refill.Visible || !refill.Collidable || !refill.oneUse) {
                continue;
            }

            Rectangle bounds = refill.Collider != null
                ? ColliderWorldBounds(refill.Collider)
                : new Rectangle((int) Math.Floor(refill.X - 6f), (int) Math.Floor(refill.Y - 6f), 12, 12);
            if (!level.Bounds.Intersects(bounds)) {
                continue;
            }

            DrawWorldRect(level, bounds, color, 0.06f * opacity, 2);
        }
    }

    private static void DrawWorldRect(Level level, Rectangle worldBounds, Color color, float fillAlpha, int thickness) {
        AkronHudRect rect = WorldToAutomationAreaSurfaceRect(level, worldBounds);
        float left = (float) Math.Floor(rect.X);
        float top = (float) Math.Floor(rect.Y);
        float right = (float) Math.Floor(rect.X + rect.Width);
        float bottom = (float) Math.Floor(rect.Y + rect.Height);
        float width = Math.Max(1f, right - left);
        float height = Math.Max(1f, bottom - top);
        float lineThickness = Math.Max(1f, Math.Min(thickness, Math.Min(width, height)));

        if (fillAlpha > 0f) {
            Draw.Rect(left, top, width, height, color * fillAlpha);
        }

        // Automation areas are authored in Celeste world pixels. In the gameplay
        // buffer pass, one surface unit is one game pixel, which keeps selection,
        // live-area display, and death hitboxes on the same grid.
        Draw.Rect(left, top, width, lineThickness, color * 0.95f);
        Draw.Rect(left, bottom - lineThickness, width, lineThickness, color * 0.95f);
        Draw.Rect(left, top, lineThickness, height, color * 0.95f);
        Draw.Rect(right - lineThickness, top, lineThickness, height, color * 0.95f);
    }

    private static void DrawWorldPixelMarker(Level level, Rectangle worldBounds, Color color) {
        AkronHudRect rect = WorldToAutomationAreaSurfaceRect(level, worldBounds);
        float x = (float) Math.Floor(rect.X);
        float y = (float) Math.Floor(rect.Y);
        float width = Math.Max(1f, (float) Math.Round(rect.Width));
        float height = Math.Max(1f, (float) Math.Round(rect.Height));

        Draw.Rect(x, y, width, height, color * 0.95f);
    }

    private static AkronHudRect WorldToAutomationAreaSurfaceRect(Level level, Rectangle worldBounds) {
        if (renderingAutomationAreasToGameplayBuffer) {
            Vector2 topLeft = level.Camera.CameraToScreen(new Vector2(worldBounds.X, worldBounds.Y));
            return new AkronHudRect(topLeft.X, topLeft.Y, worldBounds.Width, worldBounds.Height);
        }

        return AkronScreenProjection.WorldToHudRect(level, worldBounds);
    }

    private static int AutomationAreaGamePixelThickness() {
        if (renderingAutomationAreasToGameplayBuffer) {
            return 1;
        }

        return Math.Max(1, (int) Math.Round(AkronScreenProjection.CurrentViewportScale()));
    }

    private static bool HasVisibleRecordedAutoKillDeath(AkronModuleSettings settings) {
        return settings.HitboxShowLastDeath &&
               AkronEntityInspector.HasVisibleLastDeathHitbox() &&
               string.Equals(AkronModule.Session?.LastDeathEntityType, "AutoKillArea", StringComparison.Ordinal);
    }
}
