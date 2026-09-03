using System;
using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    // One walk over the top-left label stack, shared by the renderer and the obstruction
    // planner so the two cannot disagree about order or contents. Rows draw in the order the
    // player arranged on the Labels tab (Settings.LabelRowOrder), which is also the order the
    // tab shows and setup packs carry. Rows that are not part of this stack (Toasts, Cheat
    // Indicator, No Short Numbers) are skipped; custom labels are one block, placed where the
    // first custom row sits in the order and drawn in definition order inside it.
    private interface ILabelStackSink {
        void Text(string text, Color color, AkronHudLabelStyleSettings style, ref float y);
        void InputsPerSecond(AkronModuleSettings settings, ref float y);
        void StartPosLabel(ref float y);
        void CustomLabels(ref float y);
    }

    // The renderer passes AkronModule.TryUse so every drawn frame records; the planner passes
    // a CanUse check so measuring a label never records a use.
    private delegate bool LabelUseGate(AkronFeatureKind kind);

    private static void WalkLabelStack(Level level, Player player, AkronModuleSettings settings, LabelUseGate use, ILabelStackSink sink, ref float y) {
        List<string> order = settings.LabelRowOrder ?? AkronModuleSettings.BuildDefaultLabelRowOrder();
        bool customLabelsPlaced = false;
        foreach (string key in order) {
            if (key != null && key.StartsWith(AkronModuleSettings.CustomLabelRowPrefix, StringComparison.OrdinalIgnoreCase)) {
                if (!customLabelsPlaced) {
                    customLabelsPlaced = true;
                    sink.CustomLabels(ref y);
                }

                continue;
            }

            WalkLabelRow(key, level, player, settings, use, sink, ref y);
        }

        // Writers normalize the order before storing it, so every built-in key is normally
        // present. Should one be missing, it still draws rather than vanishing from the HUD.
        foreach (string key in AkronModuleSettings.BuildDefaultLabelRowOrder()) {
            if (!order.Contains(key, StringComparer.OrdinalIgnoreCase)) {
                WalkLabelRow(key, level, player, settings, use, sink, ref y);
            }
        }

        if (!customLabelsPlaced) {
            sink.CustomLabels(ref y);
        }
    }

    // Every row checks its setting before the gate: the renderer's gate records a feature use,
    // so it must not run for a label that is switched off.
    private static void WalkLabelRow(string key, Level level, Player player, AkronModuleSettings settings, LabelUseGate use, ILabelStackSink sink, ref float y) {
        switch (key) {
            case "Room":
                if (settings.RoomLabels && use(AkronFeatureKind.RoomLabelOverlay)) {
                    sink.Text("Room: " + level.Session.Level, ColorFromRgb(settings.RoomLabelColor), settings.RoomLabelStyle, ref y);
                }
                break;
            case "Input History":
                if (settings.InputViewer && use(AkronFeatureKind.InputViewer)) {
                    sink.Text("Inputs: " + AkronInputHistory.FormatCurrentChord(), ColorFromRgb(settings.InputHistoryTextColor), settings.InputHistoryLabelStyle, ref y);
                }
                break;
            case "Inputs per second":
                if (settings.InputsPerSecondCounter && use(AkronFeatureKind.InputsPerSecondCounter)) {
                    sink.InputsPerSecond(settings, ref y);
                }
                break;
            case "Room Timer":
                if (settings.RoomTimerWidget && use(AkronFeatureKind.RoomTimer)) {
                    Color timerColor = ColorFromRgb(settings.RoomTimerColor);
                    sink.Text("Map Time: " + FormatHudTicks(AkronPracticeStats.GetCurrentMapTime(level)), timerColor, settings.RoomTimerLabelStyle, ref y);
                    sink.Text("Room Time: " + FormatHudTicks(AkronPracticeStats.GetCurrentRoomTime(level)), timerColor, settings.RoomTimerLabelStyle, ref y);
                    long? bestRoom = AkronPracticeStats.GetBestRoomTime(level);
                    if (bestRoom.HasValue) {
                        sink.Text("Room PB: " + FormatHudTicks(bestRoom.Value), timerColor, settings.RoomTimerLabelStyle, ref y);
                    }
                }
                break;
            case "Room Stat Tracker":
                if (settings.RoomStatTracker && ShouldRenderRoomStatTracker(level) && use(AkronFeatureKind.RoomStatTracker)) {
                    foreach (string line in FormatRoomStatTracker(level)) {
                        sink.Text(line, ColorFromRgb(settings.RoomStatTrackerColor), settings.RoomTimerLabelStyle, ref y);
                    }
                }
                break;
            case "Death Stats":
                if (settings.DeathStatsWidget) {
                    string deathStats = FormatCurrentDeathStats(level);
                    if (!string.IsNullOrWhiteSpace(deathStats) && ShouldShowDeathStats(level) && use(AkronFeatureKind.DeathStats)) {
                        sink.Text(deathStats, ColorFromRgb(settings.DeathStatsColor), settings.DeathStatsLabelStyle, ref y);
                    }
                }
                break;
            case "Attempts":
                if (settings.TotalAttemptsWidget && use(AkronFeatureKind.AttemptsLabel)) {
                    sink.Text("Attempts: " + FormatHudNumber(GetCurrentMapDeathTotal(level) + 1), ColorFromRgb(settings.TotalAttemptsColor), settings.TotalAttemptsLabelStyle, ref y);
                }
                break;
            case "Status":
                if (settings.StatusLabelsWidget && use(AkronFeatureKind.StatusLabels)) {
                    Color statusColor = ColorFromRgb(settings.StatusLabelsColor);
                    sink.Text("Overlays: " + settings.DescribePresentationOverlays(), statusColor, settings.StatusLabelsLabelStyle, ref y);
                    sink.Text("Attempt: " + AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus), statusColor, settings.StatusLabelsLabelStyle, ref y);
                }
                break;
            case "Dash Stats":
                if (settings.DashCountStats && settings.DashCountStatsMode != AkronCounterDisplayMode.Off && use(AkronFeatureKind.PracticeCounters)) {
                    sink.Text(AkronPracticeCounters.FormatDashCount(level), ColorFromRgb(settings.StatusLabelsColor), settings.StatusLabelsLabelStyle, ref y);
                }
                break;
            case "Jump Stats":
                if (settings.JumpCount && settings.JumpCountMode != AkronCounterDisplayMode.Off && use(AkronFeatureKind.PracticeCounters)) {
                    sink.Text(AkronPracticeCounters.FormatJumpCount(), ColorFromRgb(settings.StatusLabelsColor), settings.StatusLabelsLabelStyle, ref y);
                }
                break;
            case "StartPos HUD":
                if (settings.StartPosShowLabel && use(AkronFeatureKind.StartPosTools)) {
                    sink.StartPosLabel(ref y);
                }
                break;
            case "Stamina Widget":
                if (player != null && settings.StaminaWidget && use(AkronFeatureKind.StaminaWidget)) {
                    sink.Text("Stamina: " + player.Stamina.ToString("0"), Color.White, null, ref y);
                }
                break;
            case "Speed Widget":
                if (player != null && settings.SpeedWidget && use(AkronFeatureKind.SpeedWidget)) {
                    sink.Text("Speed: " + player.Speed.Length().ToString("0.0"), Color.White, null, ref y);
                }
                break;
            case "Dash Widget":
                if (player != null && settings.DashWidget && use(AkronFeatureKind.DashWidget)) {
                    sink.Text("Dashes: " + player.Dashes, Color.White, null, ref y);
                }
                break;
        }
    }

    private sealed class DrawLabelStackSink : ILabelStackSink {
        private readonly Level level;
        private readonly Player player;

        public DrawLabelStackSink(Level level, Player player) {
            this.level = level;
            this.player = player;
        }

        public void Text(string text, Color color, AkronHudLabelStyleSettings style, ref float y) {
            DrawText(text, HudEdgePadding, ref y, color, style);
        }

        public void InputsPerSecond(AkronModuleSettings settings, ref float y) {
            RenderInputsPerSecondCounter(ref y);
        }

        public void StartPosLabel(ref float y) {
            RenderStartPosLabel(AkronActions.GetActiveStartPos(), HudEdgePadding, ref y);
        }

        public void CustomLabels(ref float y) {
            AkronCustomHudLabels.Render(level, player, ref y, anyHudLabelObstructed: currentAnyHudLabelObstructed);
        }
    }

    private sealed class PlanLabelStackSink : ILabelStackSink {
        private readonly Level level;
        private readonly Player player;

        public PlanLabelStackSink(Level level, Player player) {
            this.level = level;
            this.player = player;
        }

        public List<HudLabelObstructionPlan> Plans { get; } = new List<HudLabelObstructionPlan>();

        // Custom labels lay themselves out, so they report overlap directly instead of as plans.
        public bool CustomLabelIntersectsPlayer { get; private set; }

        public void Text(string text, Color color, AkronHudLabelStyleSettings style, ref float y) {
            Plans.Add(BuildTextPlan(text, HudEdgePadding, ref y, style));
        }

        public void InputsPerSecond(AkronModuleSettings settings, ref float y) {
            HudLabelObstructionPlan plan = BuildInputsPerSecondPlan(settings, ref y);
            if (plan != null) {
                Plans.Add(plan);
            }
        }

        public void StartPosLabel(ref float y) {
            Plans.AddRange(BuildStartPosLabelPlans(AkronActions.GetActiveStartPos(), HudEdgePadding, ref y));
        }

        public void CustomLabels(ref float y) {
            CustomLabelIntersectsPlayer |= AkronCustomHudLabels.AnyRenderedLabelIntersectsPlayer(level, player, y);
            y = AkronCustomHudLabels.CalculateRenderedBottomY(level, player, y);
        }
    }
}
