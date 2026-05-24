using Celeste;
using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawInputDisplayPopupControls(string popupId) {
        bool current = AkronModule.Settings.InputViewer;
        if (ImGui.Checkbox("Current inputs##" + popupId, ref current)) {
            AkronModule.Settings.InputViewer = current;
        }
        DrawPopupTooltip("Show the current movement, jump, dash, and grab input chord.");

        bool history = AkronModule.Settings.InputHistoryPanel;
        if (ImGui.Checkbox("Input history##" + popupId, ref history)) {
            AkronModule.Settings.InputHistoryPanel = history;
        }
        DrawPopupTooltip("Show a rolling list of recent input chords and held-frame counts.", "Input history");

        DrawIntStepperRow(
            "Rows",
            () => AkronModule.Settings.InputHistoryLength,
            value => AkronModule.Settings.InputHistoryLength = Calc.Clamp(value, 1, 20),
            -1,
            1,
            1,
            20,
            popupId,
            "How many input-history rows to keep visible.");

        if (ImGui.Button("Placement: " + AkronModule.Settings.InputHistoryPlacement + "##" + popupId)) {
            AkronModule.Settings.InputHistoryPlacement =
                AkronModule.Settings.InputHistoryPlacement == AkronHudPlacement.Left ? AkronHudPlacement.Right : AkronHudPlacement.Left;
        }
        DrawPopupTooltip("Move input history between the left HUD column and right side.");

        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.InputHistoryOpacity,
            value => AkronModule.Settings.InputHistoryOpacity = Calc.Clamp(value, 30, 100),
            -5,
            5,
            30,
            100,
            popupId,
            "Input-history panel opacity percentage.");

        bool compact = AkronModule.Settings.InputHistoryCompact;
        if (ImGui.Checkbox("Compact rows##" + popupId, ref compact)) {
            AkronModule.Settings.InputHistoryCompact = compact;
        }
        DrawPopupTooltip("Use tighter input-history rows.");

        bool pinOnDeath = AkronModule.Settings.InputHistoryPinOnDeath;
        if (ImGui.Checkbox("Pin on death##" + popupId, ref pinOnDeath)) {
            AkronModule.Settings.InputHistoryPinOnDeath = pinOnDeath;
        }
        DrawPopupTooltip("Freeze the latest input rows after death until the next fresh input.", "Pin on death");

        bool showOnDeath = AkronModule.Settings.InputHistoryShowOnDeath;
        if (ImGui.Checkbox("Show on death##" + popupId, ref showOnDeath)) {
            AkronModule.Settings.InputHistoryShowOnDeath = showOnDeath;
        }
        DrawPopupTooltip("Temporarily show the input history after death even when the panel is off.", "Show on death");

        bool transitions = AkronModule.Settings.InputHistoryShowTransitions;
        if (ImGui.Checkbox("Transition rows##" + popupId, ref transitions)) {
            AkronModule.Settings.InputHistoryShowTransitions = transitions;
        }
        DrawPopupTooltip("Insert a marker row when moving between rooms.");

        DrawHitboxColorRow("Text color", () => AkronModule.Settings.InputHistoryTextColor, value => AkronModule.Settings.InputHistoryTextColor = value, popupId, "Input history text color.");
        DrawHitboxColorRow("Event color", () => AkronModule.Settings.InputHistoryEventColor, value => AkronModule.Settings.InputHistoryEventColor = value, popupId, "Input history event marker color.");
        DrawLabelStyleRows(AkronModule.Settings.InputHistoryLabelStyle, popupId, "input-history-label-style", null, "Style for the input history label and panel.");
    }

    private void DrawStaminaBarPopupControls(string popupId) {
        bool playerBar = AkronModule.Settings.StaminaBarPlayer;
        if (ImGui.Checkbox("Player bar##" + popupId, ref playerBar)) {
            AkronModule.Settings.StaminaBarPlayer = playerBar;
        }
        DrawPopupTooltip("Attach the small stamina bar to Madeline, matching Stamina Meter's small meter.");

        if (ImGui.Button("Player: " + AkronModule.Settings.StaminaBarPlayerPosition + "##" + popupId)) {
            AkronModule.Settings.StaminaBarPlayerPosition =
                AkronModule.Settings.StaminaBarPlayerPosition == AkronStaminaPlayerBarPosition.Above
                    ? AkronStaminaPlayerBarPosition.Below
                    : AkronStaminaPlayerBarPosition.Above;
        }
        DrawPopupTooltip("Small meter placement relative to Madeline.");

        DrawIntStepperRow(
            "Player X",
            () => AkronModule.Settings.StaminaPlayerOffsetX,
            value => AkronModule.Settings.StaminaPlayerOffsetX = AkronModuleSettings.ClampResourcePlayerOffset(value),
            -5,
            5,
            -300,
            300,
            popupId,
            "Horizontal offset for the player-attached stamina bar.");

        DrawIntStepperRow(
            "Player Y",
            () => AkronModule.Settings.StaminaPlayerOffsetY,
            value => AkronModule.Settings.StaminaPlayerOffsetY = AkronModuleSettings.ClampResourcePlayerOffset(value),
            -5,
            5,
            -300,
            300,
            popupId,
            "Vertical offset for the player-attached stamina bar.");

        DrawIntStepperRow(
            "Player %",
            () => AkronModule.Settings.StaminaPlayerScale,
            value => AkronModule.Settings.StaminaPlayerScale = AkronModuleSettings.ClampResourcePlayerScale(value),
            -5,
            5,
            50,
            300,
            popupId,
            "Scale percentage for the player-attached stamina bar.");

        bool hudBar = AkronModule.Settings.StaminaBarHud;
        if (ImGui.Checkbox("HUD bar##" + popupId, ref hudBar)) {
            AkronModule.Settings.StaminaBarHud = hudBar;
        }
        DrawPopupTooltip("Show the large fixed-position stamina bar.");

        if (ImGui.Button("HUD: " + FormatStaminaHudPosition(AkronModule.Settings.StaminaBarHudPosition) + "##" + popupId)) {
            AkronModule.Settings.StaminaBarHudPosition = NextStaminaHudPosition(AkronModule.Settings.StaminaBarHudPosition);
        }
        DrawPopupTooltip("Large meter screen position.");

        if (ImGui.Button("Style: " + AkronModule.Settings.StaminaBarStyle + "##" + popupId)) {
            AkronModule.Settings.StaminaBarStyle = AkronModule.Settings.StaminaBarStyle == AkronStaminaBarStyle.Bar
                ? AkronStaminaBarStyle.Ring
                : AkronStaminaBarStyle.Bar;
        }
        DrawPopupTooltip("Ring style follows oatmealine/StaminaBar's circular display idea; Bar style is Akron's compact rectangular meter.");

        DrawIntStepperRow(
            "HUD X",
            () => AkronModule.Settings.StaminaHudOffsetX,
            value => AkronModule.Settings.StaminaHudOffsetX = AkronModuleSettings.ClampStaminaHudOffset(value),
            -10,
            10,
            -600,
            600,
            popupId,
            "Horizontal offset for the fixed HUD stamina bar.");

        DrawIntStepperRow(
            "HUD Y",
            () => AkronModule.Settings.StaminaHudOffsetY,
            value => AkronModule.Settings.StaminaHudOffsetY = AkronModuleSettings.ClampStaminaHudOffset(value),
            -10,
            10,
            -600,
            600,
            popupId,
            "Vertical offset for the fixed HUD stamina bar.");

        DrawIntStepperRow(
            "Low STA",
            () => AkronModule.Settings.LowStaminaThreshold,
            value => AkronModule.Settings.LowStaminaThreshold = Calc.Clamp(value, 1, 100),
            -1,
            1,
            1,
            100,
            popupId,
            "Stamina value where the bar switches to low-stamina color.");

        bool alwaysVisible = AkronModule.Settings.StaminaAlwaysVisible;
        if (ImGui.Checkbox("Always visible##" + popupId, ref alwaysVisible)) {
            AkronModule.Settings.StaminaAlwaysVisible = alwaysVisible;
        }
        DrawPopupTooltip("Keep the stamina bar visible even when stamina is full.");

        bool dangerMarker = AkronModule.Settings.StaminaShowDangerMarker;
        if (ImGui.Checkbox("Danger marker##" + popupId, ref dangerMarker)) {
            AkronModule.Settings.StaminaShowDangerMarker = dangerMarker;
        }
        DrawPopupTooltip("Mark Celeste's native tired threshold at 20 stamina.");

        bool changePulse = AkronModule.Settings.StaminaShowChangePulse;
        if (ImGui.Checkbox("Loss/refund pulse##" + popupId, ref changePulse)) {
            AkronModule.Settings.StaminaShowChangePulse = changePulse;
        }
        DrawPopupTooltip("Show a trailing fill segment when stamina changes.");

        bool overflow = AkronModule.Settings.StaminaShowOverflow;
        if (ImGui.Checkbox("Show overflow##" + popupId, ref overflow)) {
            AkronModule.Settings.StaminaShowOverflow = overflow;
        }
        DrawPopupTooltip("Let stamina above the vanilla maximum overflow the meter instead of clamping.");

        bool hidePaused = AkronModule.Settings.StaminaHideWhilePaused;
        if (ImGui.Checkbox("Hide while paused##" + popupId, ref hidePaused)) {
            AkronModule.Settings.StaminaHideWhilePaused = hidePaused;
        }
        DrawPopupTooltip("Hide stamina bars while Celeste is paused.");

        DrawHitboxColorRow("Normal", () => AkronModule.Settings.StaminaNormalColor, value => AkronModule.Settings.StaminaNormalColor = value, popupId, "Normal stamina color.");
        DrawHitboxColorRow("Low", () => AkronModule.Settings.StaminaLowColor, value => AkronModule.Settings.StaminaLowColor = value, popupId, "Low stamina color.");
        DrawHitboxColorRow("Fill", () => AkronModule.Settings.StaminaFillColor, value => AkronModule.Settings.StaminaFillColor = value, popupId, "Stamina meter background fill color.");
        DrawHitboxColorRow("Line", () => AkronModule.Settings.StaminaLineColor, value => AkronModule.Settings.StaminaLineColor = value, popupId, "Stamina meter outline and low-threshold marker color.");
        DrawHitboxColorRow("Overflow", () => AkronModule.Settings.StaminaOverflowColor, value => AkronModule.Settings.StaminaOverflowColor = value, popupId, "Color for stamina above the vanilla maximum.");
    }

    private void DrawDashBarPopupControls(string popupId) {
        bool playerBar = AkronModule.Settings.DashBarPlayer;
        if (ImGui.Checkbox("Player bar##" + popupId, ref playerBar)) {
            AkronModule.Settings.DashBarPlayer = playerBar;
        }
        DrawPopupTooltip("Attach the dash bar near Madeline.");

        if (ImGui.Button("Player: " + AkronModule.Settings.DashBarPlayerPosition + "##" + popupId)) {
            AkronModule.Settings.DashBarPlayerPosition =
                AkronModule.Settings.DashBarPlayerPosition == AkronStaminaPlayerBarPosition.Above
                    ? AkronStaminaPlayerBarPosition.Below
                    : AkronStaminaPlayerBarPosition.Above;
        }
        DrawPopupTooltip("Player-attached dash-bar placement relative to Madeline.");

        DrawIntStepperRow(
            "Player X",
            () => AkronModule.Settings.DashBarPlayerOffsetX,
            value => AkronModule.Settings.DashBarPlayerOffsetX = AkronModuleSettings.ClampResourcePlayerOffset(value),
            -5,
            5,
            -300,
            300,
            popupId,
            "Horizontal offset for the player-attached dash bar.");

        DrawIntStepperRow(
            "Player Y",
            () => AkronModule.Settings.DashBarPlayerOffsetY,
            value => AkronModule.Settings.DashBarPlayerOffsetY = AkronModuleSettings.ClampResourcePlayerOffset(value),
            -5,
            5,
            -300,
            300,
            popupId,
            "Vertical offset for the player-attached dash bar.");

        DrawIntStepperRow(
            "Player %",
            () => AkronModule.Settings.DashBarPlayerScale,
            value => AkronModule.Settings.DashBarPlayerScale = AkronModuleSettings.ClampResourcePlayerScale(value),
            -5,
            5,
            50,
            300,
            popupId,
            "Scale percentage for the player-attached dash bar.");

        bool hudBar = AkronModule.Settings.DashBarHud;
        if (ImGui.Checkbox("HUD bar##" + popupId, ref hudBar)) {
            AkronModule.Settings.DashBarHud = hudBar;
        }
        DrawPopupTooltip("Show a fixed-position dash resource display.");

        if (ImGui.Button("HUD: " + FormatStaminaHudPosition(AkronModule.Settings.DashBarHudPosition) + "##" + popupId)) {
            AkronModule.Settings.DashBarHudPosition = NextStaminaHudPosition(AkronModule.Settings.DashBarHudPosition);
        }
        DrawPopupTooltip("Fixed dash display screen position.");

        if (ImGui.Button("Style: " + AkronModule.Settings.DashBarStyle + "##" + popupId)) {
            AkronModule.Settings.DashBarStyle = AkronModule.Settings.DashBarStyle == AkronDashBarStyle.Pips
                ? AkronDashBarStyle.Bar
                : AkronDashBarStyle.Pips;
        }
        DrawPopupTooltip("Pips show discrete dash charges; Bar uses a segmented meter.");

        DrawIntStepperRow("HUD X", () => AkronModule.Settings.DashBarHudOffsetX, value => AkronModule.Settings.DashBarHudOffsetX = AkronModuleSettings.ClampDashHudOffset(value), -10, 10, -600, 600, popupId, "Horizontal offset for the fixed dash bar.");
        DrawIntStepperRow("HUD Y", () => AkronModule.Settings.DashBarHudOffsetY, value => AkronModule.Settings.DashBarHudOffsetY = AkronModuleSettings.ClampDashHudOffset(value), -10, 10, -600, 600, popupId, "Vertical offset for the fixed dash bar.");

        bool alwaysVisible = AkronModule.Settings.DashBarAlwaysVisible;
        if (ImGui.Checkbox("Always visible##" + popupId, ref alwaysVisible)) {
            AkronModule.Settings.DashBarAlwaysVisible = alwaysVisible;
        }
        DrawPopupTooltip("Keep the dash display visible even when dashes are full.");

        bool showText = AkronModule.Settings.DashBarShowText;
        if (ImGui.Checkbox("Show label##" + popupId, ref showText)) {
            AkronModule.Settings.DashBarShowText = showText;
        }
        DrawPopupTooltip("Show the DASH label next to pips.");

        bool emptyPips = AkronModule.Settings.DashBarShowEmptyPips;
        if (ImGui.Checkbox("Empty pips##" + popupId, ref emptyPips)) {
            AkronModule.Settings.DashBarShowEmptyPips = emptyPips;
        }
        DrawPopupTooltip("Show depleted dash slots instead of only filled charges.");

        bool hidePaused = AkronModule.Settings.DashBarHideWhilePaused;
        if (ImGui.Checkbox("Hide while paused##" + popupId, ref hidePaused)) {
            AkronModule.Settings.DashBarHideWhilePaused = hidePaused;
        }
        DrawPopupTooltip("Hide dash bars while Celeste is paused.");

        DrawHitboxColorRow("Available", () => AkronModule.Settings.DashBarAvailableColor, value => AkronModule.Settings.DashBarAvailableColor = value, popupId, "Dash-available color.");
        DrawHitboxColorRow("Empty", () => AkronModule.Settings.DashBarEmptyColor, value => AkronModule.Settings.DashBarEmptyColor = value, popupId, "Depleted dash slot color.");
        DrawHitboxColorRow("Low", () => AkronModule.Settings.DashBarLowColor, value => AkronModule.Settings.DashBarLowColor = value, popupId, "No-dash warning color.");
        DrawHitboxColorRow("Fill", () => AkronModule.Settings.DashBarFillColor, value => AkronModule.Settings.DashBarFillColor = value, popupId, "Dash meter background fill color.");
        DrawHitboxColorRow("Line", () => AkronModule.Settings.DashBarLineColor, value => AkronModule.Settings.DashBarLineColor = value, popupId, "Dash meter outline and separator color.");
    }

    private void DrawDashCountPopupControls(string popupId) {
        DrawIntStepperRow(
            "Max",
            () => AkronModule.Settings.DashCountOverrideValue,
            value => AkronModule.Settings.DashCountOverrideValue = AkronModuleSettings.ClampDashCountOverride(value),
            -1,
            1,
            0,
            5,
            popupId,
            "Maximum dash count used by Akron's dash-count override.");

        bool roomEntry = AkronModule.Settings.DashCountRefillOnRoomEntry;
        if (ImGui.Checkbox("Refill on room entry##" + popupId, ref roomEntry)) {
            AkronModule.Settings.DashCountRefillOnRoomEntry = roomEntry;
        }
        DrawPopupTooltip("Set current dashes to the configured maximum when Madeline spawns in a room.");

        bool transition = AkronModule.Settings.DashCountRefillOnTransition;
        if (ImGui.Checkbox("Refill on transition##" + popupId, ref transition)) {
            AkronModule.Settings.DashCountRefillOnTransition = transition;
        }
        DrawPopupTooltip("Set current dashes to the configured maximum after room transitions.");
    }

    private void DrawDashNumberPopupControls(string popupId) {
        DrawIntStepperRow(
            "Offset Y",
            () => AkronModule.Settings.DashNumberOffsetY,
            value => AkronModule.Settings.DashNumberOffsetY = AkronModuleSettings.ClampDashNumberOffsetY(value),
            -1,
            1,
            -96,
            96,
            popupId,
            "Vertical offset from Madeline's center.");

        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.DashNumberOpacity,
            value => AkronModule.Settings.DashNumberOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Dash number opacity percentage.");

        DrawHitboxColorRow("Text", () => AkronModule.Settings.DashNumberColor, value => AkronModule.Settings.DashNumberColor = value, popupId, "Dash number text color.");
        DrawHitboxColorRow("Outline", () => AkronModule.Settings.DashNumberOutlineColor, value => AkronModule.Settings.DashNumberOutlineColor = value, popupId, "Dash number outline color.");
    }

    private void DrawSpeedNumberPopupControls(string popupId) {
        if (ImGui.Button("Mode: " + AkronModule.Settings.SpeedNumberMode + "##" + popupId)) {
            AkronModule.Settings.SpeedNumberMode = NextSpeedNumberMode(AkronModule.Settings.SpeedNumberMode);
        }
        DrawPopupTooltip("Choose whether the number uses total speed, horizontal speed, or vertical speed.");

        DrawIntStepperRow(
            "Offset Y",
            () => AkronModule.Settings.SpeedNumberOffsetY,
            value => AkronModule.Settings.SpeedNumberOffsetY = AkronModuleSettings.ClampSpeedNumberOffsetY(value),
            -1,
            1,
            -128,
            128,
            popupId,
            "Vertical offset from Madeline's center.");

        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.SpeedNumberOpacity,
            value => AkronModule.Settings.SpeedNumberOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Speed number opacity percentage.");

        DrawHitboxColorRow("Text", () => AkronModule.Settings.SpeedNumberColor, value => AkronModule.Settings.SpeedNumberColor = value, popupId, "Speed number text color.");
        DrawHitboxColorRow("Outline", () => AkronModule.Settings.SpeedNumberOutlineColor, value => AkronModule.Settings.SpeedNumberOutlineColor = value, popupId, "Speed number outline color.");
    }
}
