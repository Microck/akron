using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static void ApplyLabelPlayerOverlap(string text, float scale, ref Vector2 position, ref AkronHudLabelStyleSettings style) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (settings == null ||
            !settings.CustomHudLabelObstructionEnabled ||
            !currentLabelPlayerHudRect.HasValue) {
            return;
        }

        Vector2 size = ActiveFont.Measure(text ?? string.Empty) * scale;
        float opacity = AkronModuleSettings.ClampOpacity(style.Opacity) / 100f;
        if (TryApplyHudElementPlayerOverlap(size, ref position, ref opacity)) {
            style.Opacity = (int) Math.Round(opacity * 100f);
        }
    }

    private static bool TryApplyHudElementPlayerOverlap(Vector2 size, ref Vector2 position, ref float opacity) {
        return TryApplyHudElementPlayerOverlap(
            AkronModule.Settings,
            currentLabelPlayerHudRect,
            currentAnyHudLabelObstructed,
            size,
            ref position,
            ref opacity);
    }

    internal static bool TryApplyHudElementPlayerOverlap(
        AkronModuleSettings settings,
        AkronHudRect? playerHudRect,
        bool anyHudLabelObstructed,
        Vector2 size,
        ref Vector2 position,
        ref float opacity) {
        if (settings == null ||
            !settings.CustomHudLabelObstructionEnabled ||
            !playerHudRect.HasValue ||
            size.X <= 0f ||
            size.Y <= 0f) {
            return false;
        }

        bool labelObstructed = anyHudLabelObstructed && !settings.CustomHudLabelObstructionOnlyOverlappedLabel;
        if (!labelObstructed && !HudRectIntersectsPlayer(settings, playerHudRect.Value, position, size)) {
            return false;
        }

        if (settings.CustomHudLabelObstructionMode == AkronLabelObstructionMode.Move) {
            Vector2 anchoredPosition = PositionForOverlapAnchor(settings, AkronModuleSettings.NormalizeCustomLabelObstructedAnchor(settings.CustomHudLabelObstructedAnchor), size);
            anchoredPosition.X += settings.CustomHudLabelObstructedOffsetX;
            anchoredPosition.Y += settings.CustomHudLabelObstructedOffsetY;
            position = anchoredPosition;
            return true;
        }

        opacity = Math.Min(opacity, AkronModuleSettings.ClampOpacity(settings.CustomHudLabelObstructedOpacity) / 100f);
        return true;
    }

    private static bool CalculateAnyHudLabelObstructed(Level level, Player player, AkronModuleSettings settings, float labelStartY) {
        if (settings == null ||
            !settings.CustomHudLabelObstructionEnabled ||
            !settings.LabelSystemVisible ||
            currentLabelPlayerHudRect == null) {
            return false;
        }

        float y = labelStartY;
        HudLabelObstructionPlan indicatorPlan = BuildIndicatorObstructionPlan(settings);
        if (indicatorPlan != null && LabelPlanIntersectsPlayer(indicatorPlan)) {
            return true;
        }

        PlanLabelStackSink sink = new PlanLabelStackSink(level, player);
        foreach (HudLabelObstructionPlan plan in BuildHudLabelObstructionPlans(level, player, settings, sink, ref y)) {
            if (LabelPlanIntersectsPlayer(plan)) {
                return true;
            }
        }

        if (sink.CustomLabelIntersectsPlayer) {
            return true;
        }

        HudLabelObstructionPlan inputHistoryPlan = BuildInputHistoryPlan(settings, ref y);
        return inputHistoryPlan != null && LabelPlanIntersectsPlayer(inputHistoryPlan);
    }

    private static HudLabelObstructionPlan BuildIndicatorObstructionPlan(AkronModuleSettings settings) {
        if (!ShouldShowIndicator()) {
            return null;
        }

        float configuredScale = AkronModuleSettings.ClampPercent(settings.HudCheatIndicatorScale, 50, 250) / 100f;
        if (settings.HudCheatIndicatorStyle == AkronHudCheatIndicatorStyle.Dot) {
            float radius = MathHelper.Clamp(5f * configuredScale, 3f, 14f);
            Vector2 diameter = Vector2.One * (radius * 2f);
            return new HudLabelObstructionPlan(AnchorBoxPosition(settings.HudCheatIndicatorAnchor, diameter), diameter);
        }

        string text = AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus);
        float scale = configuredScale * 0.45f;
        Vector2 textSize = ActiveFont.Measure(text) * scale;
        Vector2 boxSize = textSize + new Vector2(24f, 12f);
        return new HudLabelObstructionPlan(AnchorBoxPosition(settings.HudCheatIndicatorAnchor, boxSize), boxSize);
    }

    // Mirrors AkronHudRenderer.Render: the ordered row stack through the shared walk, then
    // the fixed trailing lines. Measuring must not record a use, so the
    // walk gets a CanUse gate here.
    private static List<HudLabelObstructionPlan> BuildHudLabelObstructionPlans(Level level, Player player, AkronModuleSettings settings, PlanLabelStackSink sink, ref float y) {
        List<HudLabelObstructionPlan> plans = sink.Plans;
        WalkLabelStack(level, player, settings, kind => AkronPolicy.CanUse(kind).Allowed, sink, ref y);

        if (AkronSaveLoadService.HasSlot(settings.ActiveSavestateSlot)) {
            plans.Add(BuildTextPlan("SRT slot " + settings.ActiveSavestateSlot + ": saved", HudEdgePadding, ref y, null));
        }

        if (settings.EntityInspector) {
            plans.Add(BuildTextPlan("Entity: " + AkronEntityInspector.Describe(level), HudEdgePadding, ref y, null));
        }

        return plans;
    }

    private static HudLabelObstructionPlan BuildTextPlan(string text, float x, ref float y, AkronHudLabelStyleSettings style) {
        style = AkronModuleSettings.CloneLabelStyle(style);
        float scale = 0.42f * (style.Scale / 100f);
        Vector2 position = new Vector2(x + style.OffsetX, y + style.OffsetY);
        Vector2 size = ActiveFont.Measure(text ?? string.Empty) * scale;
        y += 34f * (style.Scale / 100f) * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
        return new HudLabelObstructionPlan(position, size);
    }

    private static HudLabelObstructionPlan BuildTextAtPlan(string text, Vector2 position, AkronHudLabelStyleSettings style) {
        style = AkronModuleSettings.CloneLabelStyle(style);
        float scale = 0.42f * (style.Scale / 100f);
        return new HudLabelObstructionPlan(position, ActiveFont.Measure(text ?? string.Empty) * scale);
    }

    private static HudLabelObstructionPlan BuildInputsPerSecondPlan(AkronModuleSettings settings, ref float leftColumnY) {
        AkronInputsPerSecondSnapshot snapshot = AkronInputHistory.GetInputsPerSecondSnapshot();
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(settings.InputsPerSecondLabelStyle);
        float scale = AkronModuleSettings.ClampPercent(settings.InputsPerSecondScale, 50, 250) / 100f;
        float textScale = 0.42f * scale;
        string text = FormatInputsPerSecondHudText(snapshot, settings);
        Vector2 textSize = ActiveFont.Measure(text) * textScale;
        float screenWidth = ResolveHudViewportSize().X;
        float x = (settings.InputsPerSecondPlacement == AkronHudPlacement.Right ? screenWidth - HudEdgePadding - textSize.X : HudEdgePadding) + style.OffsetX;
        float y = (settings.InputsPerSecondPlacement == AkronHudPlacement.Right ? 72f : leftColumnY) + style.OffsetY;
        if (settings.InputsPerSecondPlacement == AkronHudPlacement.Left) {
            leftColumnY = y + 34f * scale * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
        }

        return new HudLabelObstructionPlan(new Vector2(x, y), textSize);
    }

    private static HudLabelObstructionPlan BuildInputHistoryPlan(AkronModuleSettings settings, ref float leftColumnY) {
        IReadOnlyList<AkronInputHistoryEntry> entries = AkronInputHistory.Current;
        if (entries.Count == 0 ||
            !(settings.InputHistoryPanel || settings.InputHistoryShowOnDeath && AkronInputHistory.DeathPinned) ||
            !AkronPolicy.CanUse(AkronFeatureKind.InputHistory).Allowed) {
            return null;
        }

        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(settings.InputHistoryLabelStyle);
        float styleScale = style.Scale / 100f;
        float rowHeight = (settings.InputHistoryCompact ? 23f : 29f) * styleScale * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
        float width = (settings.InputHistoryCompact ? 126f : 156f) * styleScale;
        float screenWidth = ResolveHudViewportSize().X;
        float x = (settings.InputHistoryPlacement == AkronHudPlacement.Right ? screenWidth - width - HudEdgePadding : HudEdgePadding + 8f) + style.OffsetX;
        float y = (settings.InputHistoryPlacement == AkronHudPlacement.Right ? 72f : leftColumnY) + style.OffsetY;
        Vector2 boxPosition = new Vector2(x - 8f, y - 5f);
        Vector2 boxSize = new Vector2(width, entries.Count * rowHeight + 10f);
        if (settings.InputHistoryPlacement == AkronHudPlacement.Left) {
            leftColumnY = y + entries.Count * rowHeight + 5f;
        }

        return new HudLabelObstructionPlan(boxPosition, boxSize);
    }

    private static List<HudLabelObstructionPlan> BuildStartPosLabelPlans(AkronStartPos startPos, float x, ref float y) {
        List<HudLabelObstructionPlan> plans = new List<HudLabelObstructionPlan>();
        string index = AkronActions.DescribeStartPosIndex(Engine.Scene as Level);
        string text = FormatStartPosLabel(index);
        if (startPos == null) {
            text += AkronModule.Settings.StartPosLabelFormat == AkronStartPosLabelFormat.CountOnly ? " unset" : " (unset)";
        }

        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(AkronModule.Settings.StartPosLabelStyle);
        if (AkronModule.Settings.StartPosLabelAnchor == AkronHudAnchor.TopLeft) {
            plans.Add(BuildTextPlan(text, x, ref y, style));
            return plans;
        }

        float scale = 0.42f * (style.Scale / 100f);
        Vector2 size = ActiveFont.Measure(text) * scale;
        Vector2 position = AnchorBoxPosition(AkronModule.Settings.StartPosLabelAnchor, size) + new Vector2(style.OffsetX, style.OffsetY);
        plans.Add(BuildTextAtPlan(text, position, style));
        return plans;
    }

    private static bool LabelPlanIntersectsPlayer(HudLabelObstructionPlan plan) {
        return plan != null && HudRectIntersectsPlayer(plan.Position, plan.Size);
    }

    private static bool HudRectIntersectsPlayer(Vector2 position, Vector2 size) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (settings == null || !currentLabelPlayerHudRect.HasValue) {
            return false;
        }

        return HudRectIntersectsPlayer(settings, currentLabelPlayerHudRect.Value, position, size);
    }

    private static bool HudRectIntersectsPlayer(AkronModuleSettings settings, AkronHudRect player, Vector2 position, Vector2 size) {
        return AkronCustomHudLabels.PlayerIntersectsLabelResponseArea(
            position.X,
            position.Y,
            size.X,
            size.Y,
            player.X,
            player.Y,
            player.Width,
            player.Height,
            settings.CustomHudLabelObstructionPaddingPixels);
    }

    private static Vector2 PositionForOverlapAnchor(AkronModuleSettings settings, AkronHudAnchor anchor, Vector2 size) {
        int padding = AkronModuleSettings.ClampCustomLabelPadding(settings?.CustomHudLabelPadding ?? 5);
        return anchor switch {
            AkronHudAnchor.TopCenter => HudVector(960f - size.X / 2f, padding),
            AkronHudAnchor.TopRight => HudVector(1920f - padding - size.X, padding),
            AkronHudAnchor.MiddleLeft => HudVector(padding, 540f - size.Y / 2f),
            AkronHudAnchor.Center => HudVector(960f - size.X / 2f, 540f - size.Y / 2f),
            AkronHudAnchor.MiddleRight => HudVector(1920f - padding - size.X, 540f - size.Y / 2f),
            AkronHudAnchor.BottomLeft => HudVector(padding, 1080f - padding - size.Y),
            AkronHudAnchor.BottomCenter => HudVector(960f - size.X / 2f, 1080f - padding - size.Y),
            AkronHudAnchor.BottomRight => HudVector(1920f - padding - size.X, 1080f - padding - size.Y),
            _ => HudVector(padding, padding)
        };
    }

    private static Vector2 HudVector(float x, float y) {
        Vector2 vector = default;
        vector.X = x;
        vector.Y = y;
        return vector;
    }

    private static AkronHudRect? ResolvePlayerHudRect(Level level, Player player) {
        return ResolvePlayerHudRectForLabels(level, player);
    }

    internal static AkronHudRect? ResolvePlayerHudRectForLabels(Level level, Player player) {
        if (level == null || player == null) {
            return null;
        }

        Rectangle playerBounds = new Rectangle(
            (int) Math.Floor(player.Position.X - 4f),
            (int) Math.Floor(player.Position.Y - 11f),
            (int) PlayerDefaultHitboxWidth,
            (int) PlayerDefaultHitboxHeight);
        return AkronScreenProjection.WorldToHudRect(level, playerBounds);
    }

    private sealed class HudLabelObstructionPlan {
        public HudLabelObstructionPlan(Vector2 position, Vector2 size) {
            Position = position;
            Size = size;
        }

        public Vector2 Position { get; }
        public Vector2 Size { get; }
    }
}
