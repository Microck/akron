using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawAutoKillPopupControls(string popupId) {
        DrawPopupRowLabel("Method", CalculatePopupLabelWidth(96f));
        float choiceColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton(
            "Timer",
            AkronModule.Settings.AutoKillTimer && !AkronModule.Settings.AutoKillArea,
            () => {
                AkronModule.Settings.AutoKillTimer = true;
                AkronModule.Settings.AutoKillArea = false;
            },
            popupId,
            "Kill the attempt when current map time reaches the configured threshold.",
            choiceColumnX,
            false);
        DrawPopupChoiceRadioButton(
            "Area",
            AkronModule.Settings.AutoKillArea,
            () => {
                AkronModule.Settings.AutoKillArea = true;
                AkronModule.Settings.AutoKillTimer = false;
            },
            popupId,
            "Kill the attempt when Madeline enters any selected rectangle.",
            choiceColumnX,
            true);

        DrawIntStepperRow(
            "Seconds",
            () => AkronModule.Settings.AutoKillSeconds,
            value => AkronModule.Settings.AutoKillSeconds = AkronModuleSettings.ClampAutoKillSeconds(value),
            -5,
            5,
            1,
            3600,
            popupId,
            "Map-time threshold where Auto Kill kills the player.");

        bool showArea = AkronModule.Settings.AutoKillShowArea;
        if (ImGui.Checkbox("Show area##" + popupId, ref showArea)) {
            AkronModule.Settings.AutoKillShowArea = showArea;
        }
        DrawPopupTooltip("Draw the selected auto-kill rectangle below the Akron menu.");

        bool showAreaOnDeath = AkronModule.Settings.AutoKillShowAreaOnDeath;
        if (ImGui.Checkbox("Show on death hitboxes##" + popupId, ref showAreaOnDeath)) {
            AkronModule.Settings.AutoKillShowAreaOnDeath = showAreaOnDeath;
        }
        DrawPopupTooltip("Show Auto Kill rectangles during Show Hitboxes On Death.");

        if (ImGui.CollapsingHeader("Conditions: " + DescribeAutoKillConditionsSummary() + "##" + popupId)) {
            DrawAutoKillConditionControls(popupId);
        }
        DrawPopupTooltip("Optional area-only filters for speed, dashes, grounded state, movement direction, player state, and inverted matching.");

        if (ImGui.Button("Select Area##" + popupId)) {
            BeginAutoKillAreaSelection();
            CloseOptionsPopup();
            ImGui.CloseCurrentPopup();
        }
        DrawPopupTooltip("Hide Akron and freeze gameplay so multiple game-view rectangles can be selected.");

        ImGui.SameLine();
        if (ImGui.Button("Clear##" + popupId)) {
            AkronModule.ClearAutoKillArea();
            EndAutoKillAreaSelection(false);
        }
        DrawPopupTooltip("Remove all selected auto-kill rectangles.");

        ImGui.TextUnformatted(DescribeAutoKillArea());
    }

    private void DrawAutoKillConditionControls(string popupId) {
        bool speedCondition = AkronModule.Settings.AutoKillSpeedCondition;
        if (ImGui.Checkbox("Require speed range##" + popupId, ref speedCondition)) {
            AkronModule.Settings.AutoKillSpeedCondition = speedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute speed is inside the configured range.");

        DrawIntStepperRow(
            "Min speed",
            () => AkronModule.Settings.AutoKillMinSpeed,
            value => AkronModule.Settings.AutoKillMinSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max speed",
            () => AkronModule.Settings.AutoKillMaxSpeed,
            value => AkronModule.Settings.AutoKillMaxSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute speed allowed for area-based Auto Kill.");

        bool horizontalSpeedCondition = AkronModule.Settings.AutoKillHorizontalSpeedCondition;
        if (ImGui.Checkbox("Require horizontal speed##" + popupId, ref horizontalSpeedCondition)) {
            AkronModule.Settings.AutoKillHorizontalSpeedCondition = horizontalSpeedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute horizontal speed is inside the configured range.");

        DrawIntStepperRow(
            "Min H speed",
            () => AkronModule.Settings.AutoKillMinHorizontalSpeed,
            value => AkronModule.Settings.AutoKillMinHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute horizontal speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max H speed",
            () => AkronModule.Settings.AutoKillMaxHorizontalSpeed,
            value => AkronModule.Settings.AutoKillMaxHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute horizontal speed allowed for area-based Auto Kill.");

        bool verticalSpeedCondition = AkronModule.Settings.AutoKillVerticalSpeedCondition;
        if (ImGui.Checkbox("Require vertical speed##" + popupId, ref verticalSpeedCondition)) {
            AkronModule.Settings.AutoKillVerticalSpeedCondition = verticalSpeedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute vertical speed is inside the configured range.");

        DrawIntStepperRow(
            "Min V speed",
            () => AkronModule.Settings.AutoKillMinVerticalSpeed,
            value => AkronModule.Settings.AutoKillMinVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute vertical speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max V speed",
            () => AkronModule.Settings.AutoKillMaxVerticalSpeed,
            value => AkronModule.Settings.AutoKillMaxVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute vertical speed allowed for area-based Auto Kill.");

        bool dashCondition = AkronModule.Settings.AutoKillDashCountCondition;
        if (ImGui.Checkbox("Require dash count##" + popupId, ref dashCondition)) {
            AkronModule.Settings.AutoKillDashCountCondition = dashCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline has the configured current dash count.");

        DrawIntStepperRow(
            "Dash count",
            () => AkronModule.Settings.AutoKillDashCount,
            value => AkronModule.Settings.AutoKillDashCount = AkronModuleSettings.ClampAutoKillDashCount(value),
            -1,
            1,
            0,
            5,
            popupId,
            "Current dash count required for area-based Auto Kill.");

        DrawPopupRowLabel("Ground", CalculatePopupLabelWidth(120f));
        float groundColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", AkronModule.Settings.AutoKillGroundCondition == AkronAutoKillGroundCondition.Any, () => AkronModule.Settings.AutoKillGroundCondition = AkronAutoKillGroundCondition.Any, popupId, "Do not require grounded or airborne state.", groundColumnX, false);
        DrawPopupChoiceRadioButton("Grounded", AkronModule.Settings.AutoKillGroundCondition == AkronAutoKillGroundCondition.Grounded, () => AkronModule.Settings.AutoKillGroundCondition = AkronAutoKillGroundCondition.Grounded, popupId, "Only trigger area kills while Madeline is on the ground.", groundColumnX, true);
        DrawPopupChoiceRadioButton("Airborne", AkronModule.Settings.AutoKillGroundCondition == AkronAutoKillGroundCondition.Airborne, () => AkronModule.Settings.AutoKillGroundCondition = AkronAutoKillGroundCondition.Airborne, popupId, "Only trigger area kills while Madeline is airborne.", groundColumnX, true);

        DrawPopupRowLabel("Horizontal", CalculatePopupLabelWidth(120f));
        float horizontalColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", AkronModule.Settings.AutoKillHorizontalDirection == AkronAutoKillAxisCondition.Any, () => AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Any, popupId, "Do not require horizontal movement direction.", horizontalColumnX, false);
        DrawPopupChoiceRadioButton("Left", AkronModule.Settings.AutoKillHorizontalDirection == AkronAutoKillAxisCondition.Negative, () => AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Negative, popupId, "Only trigger area kills while Madeline is moving left.", horizontalColumnX, true);
        DrawPopupChoiceRadioButton("Right", AkronModule.Settings.AutoKillHorizontalDirection == AkronAutoKillAxisCondition.Positive, () => AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Positive, popupId, "Only trigger area kills while Madeline is moving right.", horizontalColumnX, true);
        DrawPopupChoiceRadioButton("Still", AkronModule.Settings.AutoKillHorizontalDirection == AkronAutoKillAxisCondition.Zero, () => AkronModule.Settings.AutoKillHorizontalDirection = AkronAutoKillAxisCondition.Zero, popupId, "Only trigger area kills while Madeline has no horizontal speed.", horizontalColumnX, true);

        DrawPopupRowLabel("Vertical", CalculatePopupLabelWidth(120f));
        float verticalColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", AkronModule.Settings.AutoKillVerticalDirection == AkronAutoKillAxisCondition.Any, () => AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Any, popupId, "Do not require vertical movement direction.", verticalColumnX, false);
        DrawPopupChoiceRadioButton("Up", AkronModule.Settings.AutoKillVerticalDirection == AkronAutoKillAxisCondition.Negative, () => AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Negative, popupId, "Only trigger area kills while Madeline is moving up.", verticalColumnX, true);
        DrawPopupChoiceRadioButton("Down", AkronModule.Settings.AutoKillVerticalDirection == AkronAutoKillAxisCondition.Positive, () => AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Positive, popupId, "Only trigger area kills while Madeline is moving down.", verticalColumnX, true);
        DrawPopupChoiceRadioButton("Still", AkronModule.Settings.AutoKillVerticalDirection == AkronAutoKillAxisCondition.Zero, () => AkronModule.Settings.AutoKillVerticalDirection = AkronAutoKillAxisCondition.Zero, popupId, "Only trigger area kills while Madeline has no vertical speed.", verticalColumnX, true);

        bool stateCondition = AkronModule.Settings.AutoKillPlayerStateCondition;
        if (ImGui.Checkbox("Require player state##" + popupId, ref stateCondition)) {
            AkronModule.Settings.AutoKillPlayerStateCondition = stateCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's internal player state matches the configured state id.");

        DrawIntStepperRow(
            "State id",
            () => AkronModule.Settings.AutoKillPlayerState,
            value => AkronModule.Settings.AutoKillPlayerState = AkronModuleSettings.ClampAutoKillPlayerState(value),
            -1,
            1,
            0,
            99,
            popupId,
            "Internal Player.StateMachine state id required for area-based Auto Kill. Normal grounded movement is usually 0.");

        bool invertConditions = AkronModule.Settings.AutoKillInvertConditions;
        if (ImGui.Checkbox("Invert conditions##" + popupId, ref invertConditions)) {
            AkronModule.Settings.AutoKillInvertConditions = invertConditions;
        }
        DrawPopupTooltip("Trigger inside the area when enabled conditions fail instead of when they pass.");
    }

    private void DrawAutoDeafenPopupControls(string popupId) {
        ImGui.TextUnformatted("Hotkey: " + AkronActions.DescribeAutoDeafenHotkey());
        DrawPopupTooltip("Set this to the same keybind as Discord's Toggle Deafen hotkey. Akron presses it once on trigger and once again on restore.");

        if (ImGui.Button((bindingCaptureAutoDeafenHotkey ? "Press Hotkey..." : "Set Hotkey") + "##" + popupId)) {
            StartAutoDeafenHotkeyCapture();
        }
        DrawPopupTooltip("Capture the Discord Toggle Deafen hotkey. Prefer one key like F7 or Insert if Discord accepts it.");

        ImGui.SameLine();
        if (ImGui.Button("Test Toggle##" + popupId)) {
            if (AkronActions.ToggleAutoDeafenHotkeyForTest(out string error)) {
                Engine.Scene?.Add(new AkronToast(AkronActions.AutoDeafenActive ? "Auto Deafen test: hotkey sent." : "Auto Deafen test: restore hotkey sent."));
            } else {
                Engine.Scene?.Add(new AkronToast("Auto Deafen: " + error));
            }
        }
        DrawPopupTooltip("Press the configured hotkey now. This toggles Discord deafen if Discord is using the same binding.");

        ImGui.SameLine();
        if (ImGui.Button("Clear Hotkey##" + popupId)) {
            AkronActions.RestoreAutoDeafen();
            AkronModule.Settings.AutoDeafenHotkey = string.Empty;
            CancelBindingCapture();
        }
        DrawPopupTooltip("Clear the Auto Deafen hotkey. Akron will not trigger Discord until a new hotkey is set.");

        bool area = AkronModule.Settings.AutoDeafenArea;
        if (ImGui.Checkbox("Area deafen##" + popupId, ref area)) {
            AkronModule.Settings.AutoDeafenArea = area;
            if (!area) {
                AkronActions.RestoreAutoDeafen();
            }
        }
        DrawPopupTooltip("Press the configured Discord hotkey when Madeline enters any selected rectangle. Use Select Area to add rectangles.");

        bool showArea = AkronModule.Settings.AutoDeafenShowArea;
        if (ImGui.Checkbox("Show area##" + popupId, ref showArea)) {
            AkronModule.Settings.AutoDeafenShowArea = showArea;
        }
        DrawPopupTooltip("Draw selected Auto Deafen rectangles in blue below the Akron menu.");

        if (ImGui.Button("Select Area##" + popupId)) {
            BeginAutoDeafenAreaSelection();
            CloseOptionsPopup();
            ImGui.CloseCurrentPopup();
        }
        DrawPopupTooltip("Hide Akron and freeze gameplay so multiple game-view rectangles can be selected.");

        ImGui.SameLine();
        if (ImGui.Button("Clear##" + popupId)) {
            AkronModule.ClearAutoDeafenArea();
            EndAutoDeafenAreaSelection(false);
        }
        DrawPopupTooltip("Remove all selected Auto Deafen rectangles and send the restore hotkey if Akron believes Discord is deafened.");

        ImGui.TextUnformatted(DescribeAutoDeafenArea());
        ImGui.TextUnformatted("Active: " + (AkronActions.AutoDeafenActive ? "yes" : "no"));
    }
}
