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

        foreach (HudLabelObstructionPlan plan in BuildHudLabelObstructionPlans(level, player, settings, ref y)) {
            if (LabelPlanIntersectsPlayer(plan)) {
                return true;
            }
        }

        if (AkronCustomHudLabels.AnyRenderedLabelIntersectsPlayer(level, player, y)) {
            return true;
        }

        y = AkronCustomHudLabels.CalculateRenderedBottomY(level, player, y);
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

    private static List<HudLabelObstructionPlan> BuildHudLabelObstructionPlans(Level level, Player player, AkronModuleSettings settings, ref float y) {
        List<HudLabelObstructionPlan> plans = new List<HudLabelObstructionPlan>();
        if (settings.RoomLabels) {
            plans.Add(BuildTextPlan("Room: " + level.Session.Level, HudEdgePadding, ref y, settings.RoomLabelStyle));
        }

        if (player != null && settings.StaminaWidget) {
            plans.Add(BuildTextPlan("Stamina: " + player.Stamina.ToString("0"), HudEdgePadding, ref y, null));
        }

        if (player != null && settings.SpeedWidget) {
            plans.Add(BuildTextPlan("Speed: " + player.Speed.Length().ToString("0.0"), HudEdgePadding, ref y, null));
        }

        if (player != null && settings.DashWidget) {
            plans.Add(BuildTextPlan("Dashes: " + player.Dashes, HudEdgePadding, ref y, null));
        }

        if (settings.InputViewer) {
            plans.Add(BuildTextPlan("Inputs: " + AkronInputHistory.FormatCurrentChord(), HudEdgePadding, ref y, settings.InputHistoryLabelStyle));
        }

        if (settings.InputsPerSecondCounter && AkronPolicy.CanUse(AkronFeatureKind.InputsPerSecondCounter).Allowed) {
            HudLabelObstructionPlan inputsPerSecondPlan = BuildInputsPerSecondPlan(settings, ref y);
            if (inputsPerSecondPlan != null) {
                plans.Add(inputsPerSecondPlan);
            }
        }

        if (settings.RoomTimerWidget) {
            long mapTime = AkronPracticeStats.GetCurrentMapTime(level);
            long roomTime = AkronPracticeStats.GetCurrentRoomTime(level);
            plans.Add(BuildTextPlan("Map Time: " + FormatHudTicks(mapTime), HudEdgePadding, ref y, settings.RoomTimerLabelStyle));
            plans.Add(BuildTextPlan("Room Time: " + FormatHudTicks(roomTime), HudEdgePadding, ref y, settings.RoomTimerLabelStyle));
            long? bestRoom = AkronPracticeStats.GetBestRoomTime(level);
            if (bestRoom.HasValue) {
                plans.Add(BuildTextPlan("Room PB: " + FormatHudTicks(bestRoom.Value), HudEdgePadding, ref y, settings.RoomTimerLabelStyle));
            }
        }

        if (settings.RoomStatTracker && ShouldRenderRoomStatTracker(level)) {
            foreach (string line in FormatRoomStatTracker(level)) {
                plans.Add(BuildTextPlan(line, HudEdgePadding, ref y, settings.RoomTimerLabelStyle));
            }
        }

        if (settings.DeathStatsWidget) {
            string deathStats = FormatCurrentDeathStats(level);
            if (!string.IsNullOrWhiteSpace(deathStats) && ShouldShowDeathStats(level)) {
                plans.Add(BuildTextPlan(deathStats, HudEdgePadding, ref y, settings.DeathStatsLabelStyle));
            }
        }

        if (settings.TotalAttemptsWidget) {
            plans.Add(BuildTextPlan("Attempts: " + FormatHudNumber(GetCurrentMapDeathTotal(level) + 1), HudEdgePadding, ref y, settings.TotalAttemptsLabelStyle));
        }

        if (settings.StatusLabelsWidget) {
            plans.Add(BuildTextPlan("Overlays: " + settings.DescribePresentationOverlays(), HudEdgePadding, ref y, settings.StatusLabelsLabelStyle));
            plans.Add(BuildTextPlan("Attempt: " + AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus), HudEdgePadding, ref y, settings.StatusLabelsLabelStyle));
        }

        if (settings.DashCountStats && settings.DashCountStatsMode != AkronCounterDisplayMode.Off) {
            plans.Add(BuildTextPlan(AkronPracticeCounters.FormatDashCount(level), HudEdgePadding, ref y, settings.StatusLabelsLabelStyle));
        }

        if (settings.JumpCount && settings.JumpCountMode != AkronCounterDisplayMode.Off) {
            plans.Add(BuildTextPlan(AkronPracticeCounters.FormatJumpCount(), HudEdgePadding, ref y, settings.StatusLabelsLabelStyle));
        }

        if (AkronSaveLoadService.HasSlot(settings.ActiveSavestateSlot)) {
            plans.Add(BuildTextPlan("Slot " + settings.ActiveSavestateSlot + ": saved", HudEdgePadding, ref y, null));
        }

        if (settings.StartPosShowLabel) {
            plans.AddRange(BuildStartPosLabelPlans(AkronActions.GetActiveStartPos(), HudEdgePadding, ref y));
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
