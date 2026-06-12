using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private const float PlayerGravity = 900f;
    private const float PlayerMaxRun = 90f;
    private const float PlayerRunAccel = 1000f;
    private const float PlayerRunReduce = 400f;
    private const float PlayerAirMultiplier = 0.65f;
    private const float PlayerFastMaxFall = 240f;
    private const float PlayerJumpSpeed = -105f;
    private const float PlayerVarJumpTime = 0.2f;
    private const float PlayerDefaultHitboxWidth = 8f;
    private const float PlayerDefaultHitboxHeight = 11f;
    private const float HudEdgePadding = 5f;
    private static readonly Color IndicatorGoldberryHardlistCleanColor = ColorFromRgb(AkronPolicy.GoldberryHardlistCleanColorRgb);
    private static readonly Color IndicatorCleanColor = ColorFromRgb(AkronPolicy.RegularCleanColorRgb);
    private static readonly Color IndicatorCheatColor = ColorFromRgb(AkronPolicy.CheatColorRgb);
    private static AkronHudRect? currentLabelPlayerHudRect;
    private static bool currentAnyHudLabelObstructed;

    public static void Render(Level level, bool ignoreDeathWipeSuppression = false) {
        AkronModuleSettings settings = AkronModule.Settings;
        Player player = level.Tracker.GetEntity<Player>();

        if (AkronCapture.IsCapturingGameFrame) {
            if (AkronCapture.IsCapturingScannerOverlays) {
                RenderScannerExportOverlays(level, player);
            }

            return;
        }

        if (AkronModule.ShouldHideAkronRenderSurfacesAfterStateTransition() ||
            !ignoreDeathWipeSuppression && AkronModule.ShouldHideAkronRenderSurfacesBehindDeathWipe()) {
            return;
        }

        RenderRefillClarity(level);
        RenderTriggerViewer(level);
        RenderTrajectory(level, player);
        RenderStartPosMousePreview(level, player);
        RenderPauseCountdown();

        // Modal Akron prompts should suppress the rest of Akron's HUD layer so
        // the prompt reads like a single focused surface rather than stacked UI.
        if (AkronPromptMenu.IsOpen) {
            return;
        }

        AkronHudRect? previousLabelPlayerHudRect = currentLabelPlayerHudRect;
        bool previousAnyHudLabelObstructed = currentAnyHudLabelObstructed;
        currentLabelPlayerHudRect = ResolvePlayerHudRect(level, player);
        float labelStartY = HudEdgePadding + TopLeftIndicatorReservedHeight();
        currentAnyHudLabelObstructed = CalculateAnyHudLabelObstructed(level, player, settings, labelStartY);
        try {
        if (ShouldShowIndicator()) {
            RenderIndicator(level);
        }

        RenderPresentationOverlayChips(level);

        if (settings.HideAkronHud) {
            return;
        }

        float y = labelStartY;
        bool labelsVisible = settings.LabelSystemVisible;
        if (labelsVisible && settings.RoomLabels) {
            DrawText("Room: " + level.Session.Level, HudEdgePadding, ref y, ColorFromRgb(settings.RoomLabelColor), settings.RoomLabelStyle);
        }

        if (player != null && labelsVisible && settings.StaminaWidget) {
            DrawText("Stamina: " + player.Stamina.ToString("0"), HudEdgePadding, ref y, Color.White);
        }

        if (player != null && labelsVisible && settings.SpeedWidget) {
            DrawText("Speed: " + player.Speed.Length().ToString("0.0"), HudEdgePadding, ref y, Color.White);
        }

        if (player != null && labelsVisible && settings.DashWidget) {
            DrawText("Dashes: " + player.Dashes, HudEdgePadding, ref y, Color.White);
        }

        if (player != null && (settings.StaminaBar || settings.ResourceBars && settings.ResourceStaminaBar)) {
            RenderStaminaBars(level, player, HudEdgePadding, ref y);
        }

        if (player != null && (settings.DashBar || settings.ResourceBars && settings.ResourceDashPips)) {
            RenderDashBar(level, player, HudEdgePadding, ref y);
        }

        if (player != null && ShouldRenderDashNumber(settings, AkronModule.TryUse(AkronFeatureKind.ResourceBars))) {
            RenderDashNumber(level, player);
        }

        if (player != null && ShouldRenderSpeedNumber(settings, AkronModule.TryUse(AkronFeatureKind.SpeedNumber))) {
            RenderSpeedNumber(level, player);
        }

        if (labelsVisible && settings.InputViewer) {
            DrawText("Inputs: " + AkronInputHistory.FormatCurrentChord(), HudEdgePadding, ref y, ColorFromRgb(settings.InputHistoryTextColor), settings.InputHistoryLabelStyle);
        }

        if (settings.ShowTaps && AkronModule.TryUse(AkronFeatureKind.ShowTaps)) {
            RenderTapDisplay(ref y);
        }

        if (labelsVisible && settings.InputsPerSecondCounter && AkronModule.TryUse(AkronFeatureKind.InputsPerSecondCounter)) {
            RenderInputsPerSecondCounter(ref y);
        }

        if (labelsVisible && settings.RoomTimerWidget) {
            long mapTime = AkronPracticeStats.GetCurrentMapTime(level);
            long roomTime = AkronPracticeStats.GetCurrentRoomTime(level);
            Color timerColor = ColorFromRgb(settings.RoomTimerColor);
            DrawText("Map Time: " + FormatHudTicks(mapTime), HudEdgePadding, ref y, timerColor, settings.RoomTimerLabelStyle);
            DrawText("Room Time: " + FormatHudTicks(roomTime), HudEdgePadding, ref y, timerColor, settings.RoomTimerLabelStyle);
            long? bestRoom = AkronPracticeStats.GetBestRoomTime(level);
            if (bestRoom.HasValue) {
                DrawText("Room PB: " + FormatHudTicks(bestRoom.Value), HudEdgePadding, ref y, timerColor, settings.RoomTimerLabelStyle);
            }
        }

        if (labelsVisible && settings.RoomStatTracker && ShouldRenderRoomStatTracker(level)) {
            foreach (string line in FormatRoomStatTracker(level)) {
                DrawText(line, HudEdgePadding, ref y, ColorFromRgb(settings.RoomStatTrackerColor), settings.RoomTimerLabelStyle);
            }
        }

        if (labelsVisible && settings.DeathStatsWidget) {
            string deathStats = FormatCurrentDeathStats(level);
            if (!string.IsNullOrWhiteSpace(deathStats) && ShouldShowDeathStats(level)) {
                DrawText(deathStats, HudEdgePadding, ref y, ColorFromRgb(settings.DeathStatsColor), settings.DeathStatsLabelStyle);
            }
        }

        if (labelsVisible && settings.TotalAttemptsWidget) {
            DrawText("Attempts: " + FormatHudNumber(GetCurrentMapDeathTotal(level) + 1), HudEdgePadding, ref y, ColorFromRgb(settings.TotalAttemptsColor), settings.TotalAttemptsLabelStyle);
        }

        if (labelsVisible && settings.StatusLabelsWidget) {
            Color statusColor = ColorFromRgb(settings.StatusLabelsColor);
            DrawText("Overlays: " + settings.DescribePresentationOverlays(), HudEdgePadding, ref y, statusColor, settings.StatusLabelsLabelStyle);
            DrawText(
                "Attempt: " + AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus),
                HudEdgePadding,
                ref y,
                statusColor,
                settings.StatusLabelsLabelStyle);
        }

        if (labelsVisible && settings.DashCountStats && settings.DashCountStatsMode != AkronCounterDisplayMode.Off) {
            DrawText(AkronPracticeCounters.FormatDashCount(level), HudEdgePadding, ref y, ColorFromRgb(settings.StatusLabelsColor), settings.StatusLabelsLabelStyle);
        }

        if (labelsVisible && settings.JumpCount && settings.JumpCountMode != AkronCounterDisplayMode.Off) {
            DrawText(AkronPracticeCounters.FormatJumpCount(), HudEdgePadding, ref y, ColorFromRgb(settings.StatusLabelsColor), settings.StatusLabelsLabelStyle);
        }

        if (labelsVisible && AkronSaveLoadService.HasSlot(settings.ActiveSavestateSlot)) {
            DrawText("Slot " + settings.ActiveSavestateSlot + ": saved", HudEdgePadding, ref y, Color.White);
        }

        if (labelsVisible && settings.StartPosShowLabel) {
            RenderStartPosLabel(AkronActions.GetActiveStartPos(), HudEdgePadding, ref y);
        }

        if (labelsVisible && settings.EntityInspector) {
            DrawText("Entity: " + AkronEntityInspector.Describe(level), HudEdgePadding, ref y, Color.White);
        }

        if (labelsVisible) {
            AkronCustomHudLabels.Render(level, player, ref y, anyHudLabelObstructed: currentAnyHudLabelObstructed);
        }

        if (labelsVisible &&
            (settings.InputHistoryPanel || settings.InputHistoryShowOnDeath && AkronInputHistory.DeathPinned) &&
            AkronModule.TryUse(AkronFeatureKind.InputHistory)) {
            RenderInputHistory(ref y);
        }
        } finally {
            currentLabelPlayerHudRect = previousLabelPlayerHudRect;
            currentAnyHudLabelObstructed = previousAnyHudLabelObstructed;
        }
    }

    private static bool ShouldRenderRoomStatTracker(Level level) {
        if (level == null || !AkronModule.TryUse(AkronFeatureKind.RoomTimer)) {
            return false;
        }

        return !AkronModule.Settings.RoomStatHideIfGolden || !level.Session.GrabbedGolden;
    }

    private static IEnumerable<string> FormatRoomStatTracker(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        List<string> parts = new List<string>();
        if (settings.RoomStatShowRoomName) {
            parts.Add("Room " + level.Session.Level);
        }

        if (settings.RoomStatShowDeaths) {
            parts.Add("Deaths " + FormatHudNumber(AkronModule.Session.DeathsSinceRoomTransition));
        }

        if (settings.RoomStatShowInGameTime) {
            parts.Add("Time " + FormatHudTicks(AkronPracticeStats.GetCurrentRoomStatTime(level)));
        }

        if (settings.RoomStatShowStrawberries) {
            parts.Add("Berries " + FormatHudNumber(AkronModule.Session.RoomStatStrawberries));
        }

        if (settings.RoomStatShowAliveTime) {
            parts.Add("Alive " + FormatHudTicks(AkronPracticeStats.GetCurrentRoomStatAliveTime(level)));
        }

        if (parts.Count == 0) {
            yield break;
        }

        const int maxPartsPerLine = 3;
        for (int index = 0; index < parts.Count; index += maxPartsPerLine) {
            yield return string.Join("  ", parts.Skip(index).Take(maxPartsPerLine));
        }
    }

    private static void RenderTriggerViewer(Level level) {
        if (level == null ||
            !AkronModule.Settings.ShowTriggers ||
            !AkronModule.TryUse(AkronFeatureKind.TriggerViewer)) {
            return;
        }

        foreach (Trigger trigger in level.Tracker.GetEntities<Trigger>()) {
            if (trigger.Collider != null) {
                DrawWorldRect(level, ColliderWorldBounds(trigger.Collider), ColorFromRgb(AkronModule.Settings.HitboxTriggerColor), 0.08f, 2);
            }
        }
    }

    private static void RenderPauseCountdown() {
        if (!AkronModule.IsPauseCountdownActive) {
            return;
        }

        string text = Math.Max(1, (int) Math.Ceiling(AkronModule.PauseCountdownRemaining)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        float scale = 1.1f;
        Vector2 size = ActiveFont.Measure(text) * scale;
        Vector2 position = new Vector2(960f - size.X / 2f, 540f - size.Y / 2f);
        Draw.Rect(position.X - 34f, position.Y - 26f, size.X + 68f, size.Y + 52f, Color.Black * 0.50f);
        ActiveFont.Draw(text, position, Vector2.Zero, Vector2.One * scale, Color.White);
    }

    private static Rectangle ColliderWorldBounds(Collider collider) {
        return new Rectangle(
            (int) Math.Floor(collider.AbsoluteX),
            (int) Math.Floor(collider.AbsoluteY),
            (int) Math.Ceiling(collider.Width),
            (int) Math.Ceiling(collider.Height));
    }

    private static bool ShouldShowIndicator() {
        AkronModuleSettings settings = AkronModule.Settings;
        if (settings == null ||
            !settings.LabelSystemVisible ||
            settings.HideAkronHud ||
            !settings.HudCheatIndicator) {
            return false;
        }

        if (settings.HudCheatIndicatorOnlyFlagged) {
            if (settings.HudCheatIndicatorStyle == AkronHudCheatIndicatorStyle.Dot) {
                return AkronPolicy.IsMegaHackStyleCheatIndicatorFlagged(AkronModule.Session.AttemptStatus);
            }

            return AkronModule.Session.AttemptStatus != AkronStatus.Unclassified &&
                   AkronModule.Session.AttemptStatus != AkronStatus.GoldberryHardlistClean;
        }

        return true;
    }

    private static void RenderIndicator(Level level) {
        bool safeModeRedactsCleanStatus = AkronModule.Settings.SafeMode && AkronModule.Session.AttemptStatus == AkronStatus.GoldberryHardlistClean;
        string text = AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus);
        Color color = GetIndicatorColor(AkronModule.Session.AttemptStatus, safeModeRedactsCleanStatus);

        float configuredScale = AkronModuleSettings.ClampPercent(AkronModule.Settings.HudCheatIndicatorScale, 50, 250) / 100f;
        float scale = configuredScale * 0.45f;
        float opacity = CalculateHudCheatIndicatorOpacity(AkronModule.Settings);
        if (AkronModule.Settings.HudCheatIndicatorStyle == AkronHudCheatIndicatorStyle.Dot) {
            RenderIndicatorDot(color, opacity, configuredScale);
            return;
        }

        Vector2 textSize = ActiveFont.Measure(text) * scale;
        Vector2 padding = new Vector2(12f, 6f);
        Vector2 boxSize = textSize + padding * 2f;
        Vector2 boxPosition = AnchorBoxPosition(AkronModule.Settings.HudCheatIndicatorAnchor, boxSize);
        TryApplyHudElementPlayerOverlap(boxSize, ref boxPosition, ref opacity);
        Vector2 textPosition = boxPosition + padding;
        Draw.Rect(boxPosition, boxSize.X, boxSize.Y, Color.Black * (0.62f * opacity));
        ActiveFont.DrawOutline(text, textPosition, Vector2.Zero, Vector2.One * scale, color * opacity, 2f, Color.Black * opacity);
    }

    private static Color GetIndicatorColor(AkronStatus status, bool safeModeRedactsCleanStatus) {
        if (AkronModule.Settings.HudCheatIndicatorStyle == AkronHudCheatIndicatorStyle.Dot) {
            return GetMegaHackStyleDotColor(status);
        }

        return ColorFromRgb(AkronPolicy.GetStatusColorRgb(status, safeModeRedactsCleanStatus));
    }

    private static Color GetMegaHackStyleDotColor(AkronStatus status) {
        if (status == AkronStatus.GoldberryHardlistClean) {
            return IndicatorGoldberryHardlistCleanColor;
        }

        if (status == AkronStatus.RegularClean) {
            return IndicatorCleanColor;
        }

        return IndicatorCheatColor;
    }

    private static void RenderIndicatorDot(Color color, float opacity, float configuredScale) {
        float radius = MathHelper.Clamp(5f * configuredScale, 3f, 14f);
        Vector2 diameter = Vector2.One * (radius * 2f);
        Vector2 position = AnchorBoxPosition(AkronModule.Settings.HudCheatIndicatorAnchor, diameter);
        TryApplyHudElementPlayerOverlap(diameter, ref position, ref opacity);
        Vector2 center = position + new Vector2(radius, radius);
        DrawFilledHudCircle(center, radius + 2f, Color.Black * (0.75f * opacity));
        DrawFilledHudCircle(center, radius, color * opacity);
    }

    private static void DrawFilledHudCircle(Vector2 center, float radius, Color color) {
        int pixelRadius = Math.Max(1, (int) Math.Ceiling(radius));
        for (int y = -pixelRadius; y <= pixelRadius; y++) {
            float halfWidth = (float) Math.Sqrt(Math.Max(0f, radius * radius - y * y));
            Draw.Rect(
                (float) Math.Floor(center.X - halfWidth),
                (float) Math.Floor(center.Y + y),
                (float) Math.Ceiling(halfWidth * 2f),
                1f,
                color);
        }
    }

    private static float TopLeftIndicatorReservedHeight() {
        if (AkronModule.Settings.HudCheatIndicatorAnchor != AkronHudAnchor.TopLeft || !ShouldShowIndicator()) {
            return 0f;
        }

        float configuredScale = AkronModuleSettings.ClampPercent(AkronModule.Settings.HudCheatIndicatorScale, 50, 250) / 100f;
        if (AkronModule.Settings.HudCheatIndicatorStyle == AkronHudCheatIndicatorStyle.Dot) {
            float radius = MathHelper.Clamp(5f * configuredScale, 3f, 14f);
            return radius * 2f + 8f;
        }

        string text = AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus);
        float scale = configuredScale * 0.45f;
        Vector2 textSize = ActiveFont.Measure(text) * scale;
        return textSize.Y + 20f;
    }

    private static void RenderPresentationOverlayChips(Level level) {
        List<(string Label, Color Color)> chips = new List<(string Label, Color Color)>();
        if (AkronModule.Settings.ProofModeOverlay) {
            chips.Add(("Proof-mode", Color.CornflowerBlue));
        }

        if (AkronModule.Settings.IsLowDistractionActive()) {
            chips.Add(("Low-distraction", Color.SlateBlue));
        }

        if (chips.Count == 0) {
            return;
        }

        bool rightAligned = AkronModule.Settings.IndicatorCorner is IndicatorCorner.TopRight or IndicatorCorner.BottomRight;
        bool bottomAligned = AkronModule.Settings.IndicatorCorner is IndicatorCorner.BottomLeft or IndicatorCorner.BottomRight;
        float y = bottomAligned ? 944f : 72f;

        foreach ((string label, Color color) in chips) {
            float scale = 0.34f;
            float width = ActiveFont.Measure(label).X * scale + 24f;
            float x = rightAligned ? 1920f - width - 48f : 48f;
            Draw.Rect(x, y, width, 30f, Color.Black * 0.58f);
            Draw.HollowRect(x, y, width, 30f, color * 0.8f);
            ActiveFont.Draw(label, new Vector2(x + 12f, y + 4f), Vector2.Zero, Vector2.One * scale, color);
            y += bottomAligned ? -38f : 38f;
        }
    }

    private static Vector2 AnchorBoxPosition(AkronHudAnchor anchor, Vector2 size) {
        Vector2 position = anchor switch {
            AkronHudAnchor.TopLeft => new Vector2(HudEdgePadding, HudEdgePadding),
            AkronHudAnchor.TopCenter => new Vector2(960f - size.X / 2f, HudEdgePadding),
            AkronHudAnchor.TopRight => new Vector2(1920f - HudEdgePadding - size.X, HudEdgePadding),
            AkronHudAnchor.MiddleLeft => new Vector2(HudEdgePadding, 540f - size.Y / 2f),
            AkronHudAnchor.Center => new Vector2(960f - size.X / 2f, 540f - size.Y / 2f),
            AkronHudAnchor.MiddleRight => new Vector2(1920f - HudEdgePadding - size.X, 540f - size.Y / 2f),
            AkronHudAnchor.BottomLeft => new Vector2(HudEdgePadding, 1080f - HudEdgePadding - size.Y),
            AkronHudAnchor.BottomCenter => new Vector2(960f - size.X / 2f, 1080f - HudEdgePadding - size.Y),
            AkronHudAnchor.BottomRight => new Vector2(1920f - HudEdgePadding - size.X, 1080f - HudEdgePadding - size.Y),
            _ => new Vector2(1920f - HudEdgePadding - size.X, HudEdgePadding)
        };

        return position + new Vector2(AkronModule.Settings.IndicatorOffsetX, AkronModule.Settings.IndicatorOffsetY);
    }

    private static void DrawText(string text, float x, ref float y, Color color) {
        DrawText(text, x, ref y, color, null);
    }

    private static void DrawText(string text, float x, ref float y, Color color, AkronHudLabelStyleSettings style) {
        style = AkronModuleSettings.CloneLabelStyle(style);
        Vector2 position = new Vector2(x + style.OffsetX, y + style.OffsetY);
        DrawTextAt(text, position, color, style);
        y += 34f * (style.Scale / 100f) * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
    }

    private static void DrawTextAt(string text, Vector2 position, Color color, AkronHudLabelStyleSettings style) {
        style = AkronModuleSettings.CloneLabelStyle(style);
        float scale = 0.42f * (style.Scale / 100f);
        ApplyLabelPlayerOverlap(text, scale, ref position, ref style);
        float opacity = AkronModuleSettings.ClampOpacity(style.Opacity) / 100f;
        Color textColor = color * opacity;
        if (style.Shadow) {
            Color shadow = ColorFromRgb(style.ShadowColor) * (AkronModuleSettings.ClampOpacity(style.ShadowOpacity) / 100f * opacity);
            ActiveFont.Draw(text, position + new Vector2(style.ShadowOffsetX, style.ShadowOffsetY), Vector2.Zero, Vector2.One * scale, shadow);
            ActiveFont.DrawOutline(text, position, Vector2.Zero, Vector2.One * scale, textColor, 2f, shadow);
        } else {
            ActiveFont.Draw(text, position, Vector2.Zero, Vector2.One * scale, textColor);
        }
    }

    internal static float CalculateHudCheatIndicatorOpacity(AkronModuleSettings settings) {
        if (settings == null) {
            return 1f;
        }

        int indicatorOpacity = AkronModuleSettings.ClampOpacity(settings.HudCheatIndicatorOpacity);
        int labelOpacity = AkronModuleSettings.ClampOpacity(settings.LabelBulkStyle?.Opacity ?? 100);
        return indicatorOpacity / 100f * (labelOpacity / 100f);
    }

    private static Color ColorFromRgb(int rgb) {
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static string FormatHudTicks(long ticks) {
        return System.TimeSpan.FromTicks(ticks).ToString(@"mm\:ss\.fff");
    }

    public static string FormatHudNumber(int value) {
        if (AkronModule.Settings.NoShortNumbers || Math.Abs(value) < 10000) {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (Math.Abs(value) >= 1000000) {
            return (value / 1000000f).ToString("0.0M", System.Globalization.CultureInfo.InvariantCulture);
        }

        return (value / 1000f).ToString("0.0K", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool ShouldShowDeathStats(Level level) {
        AkronDeathStatsVisibility visibility = AkronModule.Settings.DeathStatsVisibility;
        return visibility switch {
            AkronDeathStatsVisibility.Always => true,
            AkronDeathStatsVisibility.AfterDeath => AkronModule.Session.DeathStatsAfterDeathTimer > 0f,
            AkronDeathStatsVisibility.InMenu => level.Paused && level.PauseMainMenuOpen,
            AkronDeathStatsVisibility.AfterDeathAndInMenu => AkronModule.Session.DeathStatsAfterDeathTimer > 0f || level.Paused && level.PauseMainMenuOpen,
            _ => false
        };
    }

    private static string FormatCurrentDeathStats(Level level) {
        if (level == null) {
            return string.Empty;
        }

        int mode = (int) level.Session.Area.Mode;
        string bestDeaths = "-";
        if (level.Session.OldStats != null &&
            mode >= 0 &&
            mode < level.Session.OldStats.Modes.Length &&
            level.Session.OldStats.Modes[mode].SingleRunCompleted) {
            bestDeaths = level.Session.OldStats.Modes[mode].BestDeaths.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        int currentMapDeaths = GetCurrentMapDeathTotal(level);
        return FormatDeathStatsText(
            AkronModule.Settings.DeathStatsFormat,
            currentMapDeaths,
            bestDeaths,
            currentMapDeaths,
            SaveData.Instance?.TotalDeaths ?? 0,
            AkronModule.Session.DeathsSinceLevelLoad,
            AkronModule.Session.DeathsSinceRoomTransition);
    }

    public static string FormatDeathStatsText(string format, int currentDeaths, string bestDeaths, int areaDeaths, int totalDeaths, int deathsSinceLevelLoad, int deathsSinceRoomTransition) {
        string normalized = AkronModuleSettings.NormalizeDeathStatsFormat(format);
        return normalized
            .Replace("$C", FormatDeathTokenNumber(currentDeaths))
            .Replace("$B", string.IsNullOrWhiteSpace(bestDeaths) ? "-" : bestDeaths)
            .Replace("$A", FormatDeathTokenNumber(areaDeaths))
            .Replace("$T", FormatDeathTokenNumber(totalDeaths))
            .Replace("$L", FormatDeathTokenNumber(deathsSinceLevelLoad))
            .Replace("$S", FormatDeathTokenNumber(deathsSinceRoomTransition));
    }

    private static string FormatDeathTokenNumber(int value) {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static int GetCurrentMapDeathTotal(Level level) {
        if (level == null) {
            return 0;
        }

        int sessionTotal = GetSessionMapDeathTotal(level);
        SaveData saveData = SaveData.Instance;
        AreaKey area = level.Session.Area;
        if (saveData == null ||
            area.ID < 0 ||
            area.ID >= saveData.Areas_Safe.Count ||
            (int) area.Mode < 0 ||
            (int) area.Mode >= saveData.Areas_Safe[area.ID].Modes.Length) {
            return sessionTotal;
        }

        int mapTotal = saveData.Areas_Safe[area.ID].Modes[(int) area.Mode].Deaths;
        return System.Math.Max(mapTotal, sessionTotal);
    }

    public static int GetSessionMapDeathTotal(Level level) {
        if (level == null) {
            return 0;
        }

        return GetSessionMapDeathTotal(
            level.Session.Deaths,
            level.Session.DeathsInCurrentLevel,
            IsDeathTransitionActive(level));
    }

    public static int GetSessionMapDeathTotal(int sessionDeaths, int roomDeaths, bool deathTransitionActive) {
        int safeSessionDeaths = System.Math.Max(0, sessionDeaths);
        int safeRoomDeaths = System.Math.Max(0, roomDeaths);
        if (deathTransitionActive) {
            // During the death-body / wipe handoff Celeste has already applied the
            // current death to Session.Deaths, while DeathsInCurrentLevel can still
            // carry the same death. Adding both makes the pre-wipe label briefly
            // show +2, then settle after respawn. Once the transition ends, the
            // normal session + current-room composition is still used.
            return System.Math.Max(safeSessionDeaths, safeRoomDeaths);
        }

        return safeSessionDeaths + safeRoomDeaths;
    }

    private static bool IsDeathTransitionActive(Level level) {
        if (level == null) {
            return false;
        }

        Player player = level.Tracker.GetEntity<Player>();
        return player?.Dead == true || level.Entities.OfType<PlayerDeadBody>().Any();
    }
}
