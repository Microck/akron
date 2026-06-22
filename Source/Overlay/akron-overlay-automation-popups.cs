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

        AkronAutoKillAreaData defaultAreaConditions = AkronModule.GetAutoKillDefaultAreaConditions();
        string defaultHeader = "Default conditions for new areas: " + DescribeAutoKillConditionsSummary(defaultAreaConditions) + "##" + popupId;
        if (ImGui.CollapsingHeader(defaultHeader)) {
            DrawAutoKillConditionControls(popupId + "-default", defaultAreaConditions);
        }
        DrawPopupTooltip("Conditions copied into each Auto Kill area when it is placed. Existing areas keep their own conditions.");

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

        int areaCount = AkronModule.GetAutoKillAreaCount();
        if (areaCount > 0 && AkronModule.TryGetSelectedAutoKillArea(out AkronAutoKillAreaData selectedArea)) {
            int selectedIndex = AkronModule.GetSelectedAutoKillAreaIndex();
            ImGui.TextUnformatted("Selected area: #" + (selectedIndex + 1) + " of " + areaCount);

            if (ImGui.Button("Previous##" + popupId)) {
                AkronModule.TrySelectAutoKillArea(selectedIndex <= 0 ? areaCount - 1 : selectedIndex - 1);
            }
            DrawPopupTooltip("Select the previous Auto Kill area for editing.");

            ImGui.SameLine();
            if (ImGui.Button("Next##" + popupId)) {
                AkronModule.TrySelectAutoKillArea(selectedIndex >= areaCount - 1 ? 0 : selectedIndex + 1);
            }
            DrawPopupTooltip("Select the next Auto Kill area for editing.");

            ImGui.SameLine();
            if (ImGui.Button("Clear Selected##" + popupId)) {
                AkronModule.RemoveSelectedAutoKillArea();
            }
            DrawPopupTooltip("Remove only the highlighted Auto Kill area.");

            if (ImGui.Button("Use Selected as Default##" + popupId)) {
                AkronModule.UseSelectedAutoKillAreaAsDefault();
            }
            DrawPopupTooltip("Copy the highlighted area's conditions into the default used by newly placed Auto Kill areas.");

            string header = "Conditions for Area #" + (AkronModule.GetSelectedAutoKillAreaIndex() + 1) + ": " + DescribeAutoKillConditionsSummary(selectedArea) + "##" + popupId;
            if (ImGui.CollapsingHeader(header)) {
                DrawAutoKillConditionControls(popupId, selectedArea);
            }
            DrawPopupTooltip("Optional filters for the highlighted Auto Kill area: speed, dashes, grounded state, movement direction, player state, and inverted matching.");
        } else {
            ImGui.TextUnformatted("Selected area: none");
            DrawPopupTooltip("Place an Auto Kill area before editing per-area conditions.");
        }
    }

    private void DrawAutoKillConditionControls(string popupId, AkronAutoKillAreaData area) {
        bool speedCondition = area.SpeedCondition;
        if (ImGui.Checkbox("Require speed range##" + popupId, ref speedCondition)) {
            area.SpeedCondition = speedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute speed is inside the configured range.");

        DrawIntStepperRow(
            "Min speed",
            () => area.MinSpeed,
            value => area.MinSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max speed",
            () => area.MaxSpeed,
            value => area.MaxSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute speed allowed for area-based Auto Kill.");

        bool horizontalSpeedCondition = area.HorizontalSpeedCondition;
        if (ImGui.Checkbox("Require horizontal speed##" + popupId, ref horizontalSpeedCondition)) {
            area.HorizontalSpeedCondition = horizontalSpeedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute horizontal speed is inside the configured range.");

        DrawIntStepperRow(
            "Min H speed",
            () => area.MinHorizontalSpeed,
            value => area.MinHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute horizontal speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max H speed",
            () => area.MaxHorizontalSpeed,
            value => area.MaxHorizontalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute horizontal speed allowed for area-based Auto Kill.");

        bool verticalSpeedCondition = area.VerticalSpeedCondition;
        if (ImGui.Checkbox("Require vertical speed##" + popupId, ref verticalSpeedCondition)) {
            area.VerticalSpeedCondition = verticalSpeedCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's absolute vertical speed is inside the configured range.");

        DrawIntStepperRow(
            "Min V speed",
            () => area.MinVerticalSpeed,
            value => area.MinVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Minimum absolute vertical speed required for area-based Auto Kill.");

        DrawIntStepperRow(
            "Max V speed",
            () => area.MaxVerticalSpeed,
            value => area.MaxVerticalSpeed = AkronModuleSettings.ClampAutoKillSpeed(value),
            -50,
            50,
            0,
            5000,
            popupId,
            "Maximum absolute vertical speed allowed for area-based Auto Kill.");

        bool dashCondition = area.DashCountCondition;
        if (ImGui.Checkbox("Require dash count##" + popupId, ref dashCondition)) {
            area.DashCountCondition = dashCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline has the configured current dash count.");

        DrawIntStepperRow(
            "Dash count",
            () => area.DashCount,
            value => area.DashCount = AkronModuleSettings.ClampAutoKillDashCount(value),
            -1,
            1,
            0,
            5,
            popupId,
            "Current dash count required for area-based Auto Kill.");

        DrawPopupRowLabel("Ground", CalculatePopupLabelWidth(120f));
        float groundColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", area.GroundCondition == AkronAutoKillGroundCondition.Any, () => area.GroundCondition = AkronAutoKillGroundCondition.Any, popupId, "Do not require grounded or airborne state.", groundColumnX, false);
        DrawPopupChoiceRadioButton("Grounded", area.GroundCondition == AkronAutoKillGroundCondition.Grounded, () => area.GroundCondition = AkronAutoKillGroundCondition.Grounded, popupId, "Only trigger area kills while Madeline is on the ground.", groundColumnX, true);
        DrawPopupChoiceRadioButton("Airborne", area.GroundCondition == AkronAutoKillGroundCondition.Airborne, () => area.GroundCondition = AkronAutoKillGroundCondition.Airborne, popupId, "Only trigger area kills while Madeline is airborne.", groundColumnX, true);

        DrawPopupRowLabel("Horizontal", CalculatePopupLabelWidth(120f));
        float horizontalColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", area.HorizontalDirection == AkronAutoKillAxisCondition.Any, () => area.HorizontalDirection = AkronAutoKillAxisCondition.Any, popupId, "Do not require horizontal movement direction.", horizontalColumnX, false);
        DrawPopupChoiceRadioButton("Left", area.HorizontalDirection == AkronAutoKillAxisCondition.Negative, () => area.HorizontalDirection = AkronAutoKillAxisCondition.Negative, popupId, "Only trigger area kills while Madeline is moving left.", horizontalColumnX, true);
        DrawPopupChoiceRadioButton("Right", area.HorizontalDirection == AkronAutoKillAxisCondition.Positive, () => area.HorizontalDirection = AkronAutoKillAxisCondition.Positive, popupId, "Only trigger area kills while Madeline is moving right.", horizontalColumnX, true);
        DrawPopupChoiceRadioButton("Still", area.HorizontalDirection == AkronAutoKillAxisCondition.Zero, () => area.HorizontalDirection = AkronAutoKillAxisCondition.Zero, popupId, "Only trigger area kills while Madeline has no horizontal speed.", horizontalColumnX, true);

        DrawPopupRowLabel("Vertical", CalculatePopupLabelWidth(120f));
        float verticalColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton("Any", area.VerticalDirection == AkronAutoKillAxisCondition.Any, () => area.VerticalDirection = AkronAutoKillAxisCondition.Any, popupId, "Do not require vertical movement direction.", verticalColumnX, false);
        DrawPopupChoiceRadioButton("Up", area.VerticalDirection == AkronAutoKillAxisCondition.Negative, () => area.VerticalDirection = AkronAutoKillAxisCondition.Negative, popupId, "Only trigger area kills while Madeline is moving up.", verticalColumnX, true);
        DrawPopupChoiceRadioButton("Down", area.VerticalDirection == AkronAutoKillAxisCondition.Positive, () => area.VerticalDirection = AkronAutoKillAxisCondition.Positive, popupId, "Only trigger area kills while Madeline is moving down.", verticalColumnX, true);
        DrawPopupChoiceRadioButton("Still", area.VerticalDirection == AkronAutoKillAxisCondition.Zero, () => area.VerticalDirection = AkronAutoKillAxisCondition.Zero, popupId, "Only trigger area kills while Madeline has no vertical speed.", verticalColumnX, true);

        bool stateCondition = area.PlayerStateCondition;
        if (ImGui.Checkbox("Require player state##" + popupId, ref stateCondition)) {
            area.PlayerStateCondition = stateCondition;
        }
        DrawPopupTooltip("Only trigger area kills while Madeline's internal player state matches the configured state id.");

        DrawIntStepperRow(
            "State id",
            () => area.PlayerState,
            value => area.PlayerState = AkronModuleSettings.ClampAutoKillPlayerState(value),
            -1,
            1,
            0,
            99,
            popupId,
            "Internal Player.StateMachine state id required for area-based Auto Kill. Normal grounded movement is usually 0.");

        bool invertConditions = area.InvertConditions;
        if (ImGui.Checkbox("Invert conditions##" + popupId, ref invertConditions)) {
            area.InvertConditions = invertConditions;
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
