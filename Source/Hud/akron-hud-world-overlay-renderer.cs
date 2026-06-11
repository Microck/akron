using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static bool renderingAutomationAreasToGameplayBuffer;

    private static void RenderAutoKillArea(Level level, bool deathHitboxPass) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        bool shouldShowLive = settings.AutoKill && settings.AutoKillArea && settings.AutoKillShowArea;
        bool shouldShowOnDeath = deathHitboxPass &&
                                 settings.AutoKillShowAreaOnDeath &&
                                 settings.HitboxShowLastDeath &&
                                 AkronEntityInspector.HasVisibleLastDeathHitbox();
        bool shouldShowPreview = AkronModule.TryGetPracticeAreaSelectionPreview(level, isAutoDeafen: false, out Rectangle preview, out bool hasAnchor);
        if (!shouldShowLive && !shouldShowOnDeath && !shouldShowPreview) {
            return;
        }

        foreach (Rectangle area in AkronModule.GetAutoKillAreas()) {
            if (area.Width > 0 && area.Height > 0) {
                DrawWorldRect(level, area, Color.OrangeRed, shouldShowOnDeath ? 0.20f : 0.14f, 2);
            }
        }

        if (shouldShowPreview) {
            if (hasAnchor) {
                DrawWorldRect(level, preview, Color.OrangeRed, 0.18f, 2);
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

        if (shouldShowLive) {
            foreach (Rectangle area in AkronModule.GetAutoDeafenAreas()) {
                if (area.Width > 0 && area.Height > 0) {
                    DrawWorldRect(level, area, Color.DeepSkyBlue, 0.14f, 2);
                }
            }
        }

        if (shouldShowPreview) {
            if (hasAnchor) {
                DrawWorldRect(level, preview, Color.DeepSkyBlue, 0.18f, 2);
            } else {
                DrawWorldPixelMarker(level, preview, Color.DeepSkyBlue);
            }
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
        Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, color * fillAlpha);
        for (int index = 0; index < thickness; index++) {
            Draw.HollowRect(rect.X - index, rect.Y - index, rect.Width + index * 2f, rect.Height + index * 2f, color * 0.95f);
        }
    }

    private static void DrawWorldPixelMarker(Level level, Rectangle worldBounds, Color color) {
        AkronHudRect rect = WorldToAutomationAreaSurfaceRect(level, worldBounds);
        float x = (float) Math.Floor(rect.X);
        float y = (float) Math.Floor(rect.Y);
        float width = Math.Max(1f, (float) Math.Round(rect.Width));
        float height = Math.Max(1f, (float) Math.Round(rect.Height));

        Draw.Rect(x, y, width, height, color * 0.85f);
    }

    private static AkronHudRect WorldToAutomationAreaSurfaceRect(Level level, Rectangle worldBounds) {
        if (!renderingAutomationAreasToGameplayBuffer) {
            return AkronScreenProjection.WorldToHudRect(level, worldBounds);
        }

        Vector2 topLeft = level.Camera.CameraToScreen(new Vector2(worldBounds.X, worldBounds.Y));
        return new AkronHudRect(topLeft.X, topLeft.Y, worldBounds.Width, worldBounds.Height);
    }
}
