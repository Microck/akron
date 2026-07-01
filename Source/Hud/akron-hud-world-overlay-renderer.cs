using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static readonly string[] RefillClarityOneUseMemberNames = {
        "oneUse",
        "OneUse",
        "oneuse",
        "_oneUse",
        "_OneUse",
        "_oneuse",
        "onlyOnce"
    };
    private static readonly Dictionary<Type, Func<Entity, bool?>> RefillClarityOneUseReaders = new Dictionary<Type, Func<Entity, bool?>>();
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
        int selectedAreaIndex = AkronModule.GetSelectedAutoKillAreaIndex();
        List<Rectangle> autoKillAreas = AkronModule.GetAutoKillAreas();
        for (int index = 0; index < autoKillAreas.Count; index++) {
            Rectangle area = autoKillAreas[index];
            if (area.Width > 0 && area.Height > 0) {
                bool selected = index == selectedAreaIndex;
                Color color = selected ? Color.Lerp(Color.OrangeRed, Color.White, 0.35f) : Color.OrangeRed;
                float fillAlpha = selected ? 0.28f : 0.14f;
                DrawWorldRect(level, area, color, fillAlpha, lineThickness);
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
            AkronModule.ShouldHideAkronRenderSurfaces()) {
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

    internal static bool ShouldRenderRefillClarityOutline(Entity entity) {
        return entity != null &&
               entity.Visible &&
               entity.Collidable &&
               IsRefillClarityCandidate(entity) &&
               TryGetRefillClarityOneUse(entity, out bool oneUse) &&
               oneUse;
    }

    internal static bool TryGetRefillClarityBounds(Entity entity, out Rectangle bounds) {
        bounds = default;
        if (!ShouldRenderRefillClarityOutline(entity)) {
            return false;
        }

        bounds = entity.Collider != null
            ? ColliderWorldBounds(entity.Collider)
            : new Rectangle((int) Math.Floor(entity.X - 6f), (int) Math.Floor(entity.Y - 6f), 12, 12);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool IsRefillClarityCandidate(Entity entity) {
        if (entity is Refill) {
            return true;
        }

        Type type = entity.GetType();
        string typeName = type.FullName ?? type.Name;
        if (typeName.IndexOf("Refill", StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        string sourceName = GetRefillClaritySourceName(entity);
        return sourceName.IndexOf("refill", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetRefillClaritySourceName(Entity entity) {
        try {
            return entity.SourceData?.Name ?? string.Empty;
        } catch (Exception) {
            // SourceData is only a fallback for generated/custom entities; some
            // runtime-only objects expose a publicized getter that is unsafe here.
            return string.Empty;
        }
    }

    private static bool TryGetRefillClarityOneUse(Entity entity, out bool oneUse) {
        if (entity is Refill refill) {
            oneUse = refill.oneUse;
            return true;
        }

        Func<Entity, bool?> reader = GetRefillClarityOneUseReader(entity.GetType());
        bool? reflectedValue = reader(entity);
        oneUse = reflectedValue == true;
        return reflectedValue.HasValue;
    }

    private static Func<Entity, bool?> GetRefillClarityOneUseReader(Type type) {
        if (RefillClarityOneUseReaders.TryGetValue(type, out Func<Entity, bool?> cached)) {
            return cached;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string memberName in RefillClarityOneUseMemberNames) {
            FieldInfo field = FindBooleanField(type, memberName, flags);
            if (field != null) {
                Func<Entity, bool?> reader = entity => (bool) field.GetValue(entity);
                RefillClarityOneUseReaders[type] = reader;
                return reader;
            }

            PropertyInfo property = FindBooleanProperty(type, memberName, flags);
            if (property != null) {
                Func<Entity, bool?> reader = entity => (bool) property.GetValue(entity);
                RefillClarityOneUseReaders[type] = reader;
                return reader;
            }
        }

        Func<Entity, bool?> missing = _ => null;
        RefillClarityOneUseReaders[type] = missing;
        return missing;
    }

    private static FieldInfo FindBooleanField(Type type, string memberName, BindingFlags flags) {
        for (Type current = type; current != null; current = current.BaseType) {
            FieldInfo field = current.GetField(memberName, flags);
            if (field?.FieldType == typeof(bool)) {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo FindBooleanProperty(Type type, string memberName, BindingFlags flags) {
        for (Type current = type; current != null; current = current.BaseType) {
            PropertyInfo property = current.GetProperty(memberName, flags);
            if (property?.PropertyType == typeof(bool) &&
                property.GetIndexParameters().Length == 0 &&
                property.GetMethod != null) {
                return property;
            }
        }

        return null;
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
