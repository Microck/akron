using System.Collections.Generic;
using System.Globalization;
using Celeste;
using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawInputsPerSecondPopupControls(string popupId) {
        if (ImGui.Button("Placement: " + AkronModule.Settings.InputsPerSecondPlacement + "##" + popupId)) {
            AkronModule.Settings.InputsPerSecondPlacement =
                AkronModule.Settings.InputsPerSecondPlacement == AkronHudPlacement.Left ? AkronHudPlacement.Right : AkronHudPlacement.Left;
        }
        DrawPopupTooltip("Choose the screen side for the inputs-per-second counter.");

        DrawIntStepperRow("Scale", () => AkronModule.Settings.InputsPerSecondScale, value => AkronModule.Settings.InputsPerSecondScale = AkronModuleSettings.ClampPercent(value, 50, 250), -5, 5, 50, 250, popupId, "Counter scale percentage.");
        DrawIntStepperRow("Opacity", () => AkronModule.Settings.InputsPerSecondOpacity, value => AkronModule.Settings.InputsPerSecondOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Counter opacity percentage.");

        bool total = AkronModule.Settings.InputsPerSecondShowTotal;
        if (ImGui.Checkbox("Show total##" + popupId, ref total)) {
            AkronModule.Settings.InputsPerSecondShowTotal = total;
        }
        DrawPopupTooltip("Show total counted input presses since level entry or last death.");

        bool max = AkronModule.Settings.InputsPerSecondShowMax;
        if (ImGui.Checkbox("Show max##" + popupId, ref max)) {
            AkronModule.Settings.InputsPerSecondShowMax = max;
        }
        DrawPopupTooltip("Show the highest rolling inputs-per-second value since level entry or last death.");

        bool movement = AkronModule.Settings.InputsPerSecondCountMovement;
        if (ImGui.Checkbox("Count movement##" + popupId, ref movement)) {
            AkronModule.Settings.InputsPerSecondCountMovement = movement;
        }
        DrawPopupTooltip("Count left, right, up, and down rising-edge presses.");

        bool actions = AkronModule.Settings.InputsPerSecondCountActions;
        if (ImGui.Checkbox("Count actions##" + popupId, ref actions)) {
            AkronModule.Settings.InputsPerSecondCountActions = actions;
        }
        DrawPopupTooltip("Count jump, dash, grab, crouch dash, and talk rising-edge presses.");

        bool menu = AkronModule.Settings.InputsPerSecondCountMenu;
        if (ImGui.Checkbox("Count menu##" + popupId, ref menu)) {
            AkronModule.Settings.InputsPerSecondCountMenu = menu;
        }
        DrawPopupTooltip("Also count confirm, cancel, and pause/menu rising-edge presses.");

        if (ImGui.Button("Reset counter##" + popupId)) {
            AkronInputHistory.ResetInputsPerSecond();
        }
        DrawPopupTooltip("Clear current, total, and max input counts.");

        DrawHitboxColorRow("Text", () => AkronModule.Settings.InputsPerSecondTextColor, value => AkronModule.Settings.InputsPerSecondTextColor = value, popupId, "Counter text color.");
        DrawLabelStyleRows(AkronModule.Settings.InputsPerSecondLabelStyle, popupId, "ips-label-style", null, "Position, line spacing, and shadow for this counter.", false);
    }

    private void DrawDeathStatsPopupControls(string popupId) {
        string format = AkronModule.Settings.DeathStatsFormat ?? AkronModuleSettings.DefaultDeathStatsFormat;
        if (DrawPopupInputText("Format", ref format, 48, popupId, 180f)) {
            AkronModule.Settings.DeathStatsFormat = AkronModuleSettings.NormalizeDeathStatsFormat(format);
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        DrawPopupTooltip("Tokens: $C current map deaths, $B best deaths, $A map deaths, $T file deaths, $L level-load deaths, $S room deaths.");

        DrawDeathStatsVisibilityChoice("Disabled", AkronDeathStatsVisibility.Disabled, popupId);
        DrawDeathStatsVisibilityChoice("After death", AkronDeathStatsVisibility.AfterDeath, popupId);
        DrawDeathStatsVisibilityChoice("Pause menu", AkronDeathStatsVisibility.InMenu, popupId);
        DrawDeathStatsVisibilityChoice("Death + menu", AkronDeathStatsVisibility.AfterDeathAndInMenu, popupId);
        DrawDeathStatsVisibilityChoice("Always", AkronDeathStatsVisibility.Always, popupId);

        ImGui.Separator();
        DrawPopupCheckbox("PB loss prompt", () => AkronModule.Settings.DeathPbLossPrompt, value => AkronModule.Settings.DeathPbLossPrompt = value, popupId, "Prompt to restart once current deaths exceed the saved best clear deaths.");

        ImGui.Separator();
        ImGui.TextUnformatted("Preview: " + FormatDeathStatsMenuPreview(Scene as Level));
        DrawPopupTooltip("Preview uses current level counters when a level is loaded.");
        ImGui.TextUnformatted("Current: " + FormatDeathStatsMenuCounters(Scene as Level));
        DrawPopupTooltip("Token values available to the death-stats format.");

        if (ImGui.Button("Reset##death-stats-format-" + popupId)) {
            AkronModule.Settings.DeathStatsFormat = AkronModuleSettings.DefaultDeathStatsFormat;
            AkronModule.Settings.DeathStatsVisibility = AkronDeathStatsVisibility.AfterDeathAndInMenu;
        }
        DrawPopupTooltip("Restore the default death display format and visibility.");
    }

    private static string FormatDeathStatsMenuPreview(Level level) {
        if (level == null || level.Session == null || AkronModule.Session == null) {
            return "No level loaded";
        }

        int mode = (int) level.Session.Area.Mode;
        string bestDeaths = "-";
        if (level.Session.OldStats != null &&
            mode >= 0 &&
            mode < level.Session.OldStats.Modes.Length &&
            level.Session.OldStats.Modes[mode].SingleRunCompleted) {
            bestDeaths = level.Session.OldStats.Modes[mode].BestDeaths.ToString(CultureInfo.InvariantCulture);
        }

        int currentMapDeaths = AkronHudRenderer.GetCurrentMapDeathTotal(level);
        return AkronHudRenderer.FormatDeathStatsText(
            AkronModule.Settings.DeathStatsFormat,
            currentMapDeaths,
            bestDeaths,
            currentMapDeaths,
            SaveData.Instance?.TotalDeaths ?? 0,
            AkronModule.Session.DeathsSinceLevelLoad,
            AkronModule.Session.DeathsSinceRoomTransition);
    }

    private static string FormatDeathStatsMenuCounters(Level level) {
        if (level == null || level.Session == null || AkronModule.Session == null) {
            return "$C 0  $B -  $A 0  $T 0  $L 0  $S 0";
        }

        int mode = (int) level.Session.Area.Mode;
        string bestDeaths = "-";
        if (level.Session.OldStats != null &&
            mode >= 0 &&
            mode < level.Session.OldStats.Modes.Length &&
            level.Session.OldStats.Modes[mode].SingleRunCompleted) {
            bestDeaths = level.Session.OldStats.Modes[mode].BestDeaths.ToString(CultureInfo.InvariantCulture);
        }

        int currentMapDeaths = AkronHudRenderer.GetCurrentMapDeathTotal(level);
        return "$C " + currentMapDeaths.ToString(CultureInfo.InvariantCulture) +
               "  $B " + bestDeaths +
               "  $A " + currentMapDeaths.ToString(CultureInfo.InvariantCulture) +
               "  $T " + (SaveData.Instance?.TotalDeaths ?? 0).ToString(CultureInfo.InvariantCulture) +
               "  $L " + AkronModule.Session.DeathsSinceLevelLoad.ToString(CultureInfo.InvariantCulture) +
               "  $S " + AkronModule.Session.DeathsSinceRoomTransition.ToString(CultureInfo.InvariantCulture);
    }

    private void DrawDeathStatsVisibilityChoice(string label, AkronDeathStatsVisibility visibility, string popupId) {
        bool selected = AkronModule.Settings.DeathStatsVisibility == visibility;
        if (ImGui.RadioButton(label + "##death-visibility-" + popupId, selected)) {
            AkronModule.Settings.DeathStatsVisibility = visibility;
        }
    }

    private void DrawHudCheatIndicatorPopupControls(string popupId) {
        DrawCheatIndicatorColorReason();
        ImGui.Separator();

        bool onlyFlagged = AkronModule.Settings.HudCheatIndicatorOnlyFlagged;
        if (ImGui.Checkbox("Only cheating##" + popupId, ref onlyFlagged)) {
            AkronModule.Settings.HudCheatIndicatorOnlyFlagged = onlyFlagged;
        }
        DrawPopupTooltip("Hide the indicator until the attempt status differs from the initial status.");

        if (ImGui.Button("Anchor: " + AkronModule.Settings.HudCheatIndicatorAnchor + "##" + popupId)) {
            AkronModule.Settings.HudCheatIndicatorAnchor = NextHudAnchor(AkronModule.Settings.HudCheatIndicatorAnchor);
        }
        DrawPopupTooltip("Nine-way screen alignment for the status indicator.");

        if (ImGui.Button("Style: " + AkronModule.Settings.HudCheatIndicatorStyle + "##" + popupId)) {
            AkronModule.Settings.HudCheatIndicatorStyle = NextHudCheatIndicatorStyle(AkronModule.Settings.HudCheatIndicatorStyle);
        }
        DrawPopupTooltip("Text badge or single status dot.");

        DrawIntStepperRow("Scale", () => AkronModule.Settings.HudCheatIndicatorScale, value => AkronModule.Settings.HudCheatIndicatorScale = AkronModuleSettings.ClampPercent(value, 50, 250), -5, 5, 50, 250, popupId, "Indicator scale percentage.");
        DrawIntStepperRow("Opacity", () => AkronModule.Settings.HudCheatIndicatorOpacity, value => AkronModule.Settings.HudCheatIndicatorOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Indicator opacity percentage.");
    }

    private static void DrawCheatIndicatorColorReason() {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            ImGui.TextUnformatted("Current: No save");
            ImGui.TextWrapped("The indicator color is unavailable outside an active save context.");
            return;
        }

        bool safeModeRedactsCleanStatus = AkronModule.Settings.SafeMode && session.AttemptStatus == AkronStatus.GoldberryHardlistClean;
        int rgb = AkronPolicy.GetStatusColorRgb(session.AttemptStatus, safeModeRedactsCleanStatus);
        string status = AkronPolicy.GetLegitimacySensitiveStatusLabel(session.AttemptStatus);
        string color = AkronPolicy.GetStatusColorName(session.AttemptStatus, safeModeRedactsCleanStatus) + " " + FormatRgb(rgb);

        ImGui.TextUnformatted("Current: " + status);
        ImGui.TextUnformatted("Color: ");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ToImGuiColor(rgb));
        ImGui.TextUnformatted(color);
        ImGui.PopStyleColor();
        ImGui.TextWrapped(AkronPolicy.DescribeStatusColorReason(session.AttemptStatus, session.AttemptReason, safeModeRedactsCleanStatus));

        IReadOnlyList<AkronActiveCheatContributor> contributors = AkronPolicy.GetActiveCheatContributors(AkronModule.Settings, session);
        if (contributors.Count == 0) {
            ImGui.TextWrapped(session.AttemptStatus == AkronStatus.Cheat
                ? "Active cheat toggles: none. The attempt is already red from a past action; turn off any one-shot effect if it is still active, then start a fresh attempt to clear the status."
                : "Active cheat toggles: none.");
            return;
        }

        ImGui.TextUnformatted("Active cheat toggles:");
        foreach (AkronActiveCheatContributor contributor in contributors) {
            ImGui.BulletText(contributor.Label + " - " + contributor.DisableCommand);
        }
    }
}
