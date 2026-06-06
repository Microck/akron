using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
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
            DrawWorldRect(level, preview, Color.OrangeRed, hasAnchor ? 0.18f : 0.10f, hasAnchor ? 2 : 1);
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
            DrawWorldRect(level, preview, Color.DeepSkyBlue, hasAnchor ? 0.18f : 0.10f, hasAnchor ? 2 : 1);
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
        AkronHudRect rect = AkronScreenProjection.WorldToHudRect(level, worldBounds);
        Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, color * fillAlpha);
        for (int index = 0; index < thickness; index++) {
            Draw.HollowRect(rect.X - index, rect.Y - index, rect.Width + index * 2f, rect.Height + index * 2f, color * 0.95f);
        }
    }
}
