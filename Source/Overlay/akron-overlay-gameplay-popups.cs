using System;
using System.Globalization;
using Celeste;
using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawSavestateSlotPopupControls(string popupId) {
        DrawIntStepperRow(
            "Slot",
            () => AkronModule.Settings.ActiveSavestateSlot,
            value => {
                AkronModule.SetActiveSavestateSlot(value);
                Engine.Scene?.Add(new AkronToast("Active StartPos snapshot slot: " + AkronModule.Settings.ActiveSavestateSlot));
            },
            -1,
            1,
            1,
            9,
            popupId,
            "StartPos snapshot slots store full Akron room snapshots, not just coordinates.");

        if (ImGui.Button("Save##" + popupId) && Engine.Scene is Level saveLevel) {
            AkronModule.PerformSaveState(saveLevel);
        }
        DrawPopupTooltip("Capture the current full room state into this slot.");

        ImGui.SameLine();
        if (ImGui.Button("Load##" + popupId) && Engine.Scene is Level loadLevel) {
            AkronModule.PerformLoadState(loadLevel);
        }
        DrawPopupTooltip("Restore the full room state from this slot.");

        ImGui.TextUnformatted("Built-ins: " +
                              AkronModuleSettings.DescribeBinding(AkronModule.Settings.PreviousSlot) +
                              " / " +
                              AkronModuleSettings.DescribeBinding(AkronModule.Settings.NextSlot));

        ImGui.Separator();
        ImGui.TextUnformatted("Menu bindings");
        DrawPopupActionBindingRow("Capture", PopupActionKey("StartPos Snapshot Slot", "Capture"), "StartPos Snapshot Slot / Capture", popupId);
        DrawPopupActionBindingRow("Restore", PopupActionKey("StartPos Snapshot Slot", "Restore"), "StartPos Snapshot Slot / Restore", popupId);
    }

    private void DrawGrabModePopupControls(string popupId) {
        DrawGrabModeChoice("Hold", GrabModes.Hold, popupId);
        DrawGrabModeChoice("Toggle", GrabModes.Toggle, popupId);
        DrawGrabModeChoice("Invert", GrabModes.Invert, popupId);
        ImGui.TextUnformatted("Configured: " + AkronModule.Settings.GrabModeOverrideMode);
        ImGui.TextUnformatted("Active: " + Settings.Instance.GrabMode);
    }

    private void DrawGrabModeChoice(string label, GrabModes mode, string popupId) {
        bool selected = AkronModule.Settings.GrabModeOverrideMode == mode;
        bool value = selected;
        if (ImGui.Checkbox(label + "##" + popupId, ref value) && value && !selected) {
            AkronModule.Settings.GrabModeOverrideMode = mode;
            ApplyGrabModeOverrideIfEnabled();
        }
        DrawPopupTooltip(label switch {
            "Toggle" => "Press grab once to hold, then press again to release.",
            "Invert" => "Hold grab to release instead of to grab.",
            _ => "Vanilla grab behavior: hold the grab key to grab."
        });
    }

    private void DrawConfirmActionsPopupControls(string popupId) {
        DrawConfirmToggle("Confirm Restart", () => AkronModule.Settings.ConfirmRestart, value => AkronModule.Settings.ConfirmRestart = value, popupId);
        DrawConfirmToggle("Confirm Full Reset", () => AkronModule.Settings.ConfirmFullReset, value => AkronModule.Settings.ConfirmFullReset = value, popupId);
        DrawConfirmToggle("Reload room", () => AkronModule.Settings.ConfirmReloadRoom, value => AkronModule.Settings.ConfirmReloadRoom = value, popupId);
        DrawConfirmToggle("Restore StartPos State", () => AkronModule.Settings.ConfirmLoadState, value => AkronModule.Settings.ConfirmLoadState = value, popupId);
    }

    private void DrawConfirmToggle(string label, Func<bool> getter, Action<bool> setter, string popupId) {
        bool enabled = getter();
        if (ImGui.Checkbox(label + "##" + popupId, ref enabled)) {
            setter(enabled);
        }
        DrawPopupTooltip("Ask for confirmation before executing this action.");
    }

    private void DrawAirJumpsPopupControls(string popupId) {
        bool infinite = AkronModule.Settings.JumpHackInfinite;
        if (ImGui.Checkbox("Infinite jumps##" + popupId, ref infinite)) {
            AkronModule.Settings.JumpHackInfinite = infinite;
        }
        DrawPopupTooltip("Allow every unhandled airborne jump press to trigger another normal jump.");

        DrawIntStepperRow(
            "Extra jumps",
            () => AkronModule.Settings.JumpHackExtraJumps,
            value => AkronModule.Settings.JumpHackExtraJumps = AkronModuleSettings.ClampJumpHackExtraJumps(value),
            -1,
            1,
            1,
            99,
            popupId,
            "Used when Infinite jumps is off.");

        bool allowVerticalDashJumps = AkronModule.Settings.JumpHackAllowVerticalDashJumps;
        if (ImGui.Checkbox("Dash verticals##" + popupId, ref allowVerticalDashJumps)) {
            AkronModule.Settings.JumpHackAllowVerticalDashJumps = allowVerticalDashJumps;
        }
        DrawPopupTooltip("Allow Air Jumps to cancel vertical or upward dash directions. Off keeps vanilla dash-jump direction rules.");
    }

    private void DrawCoreModePopupControls(string popupId) {
        DrawPopupRowLabel("Mode", CalculatePopupLabelWidth(84f));
        float choiceColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton(
            "Hot",
            AkronModule.Settings.CoreModeOverride == AkronCoreModeOverride.Hot,
            () => {
                AkronModule.Settings.CoreModeOverride = AkronCoreModeOverride.Hot;
            },
            popupId,
            "Click toggles the configured Core Mode override on or off.",
            choiceColumnX,
            false);
        DrawPopupChoiceRadioButton(
            "Cold",
            AkronModule.Settings.CoreModeOverride == AkronCoreModeOverride.Cold,
            () => {
                AkronModule.Settings.CoreModeOverride = AkronCoreModeOverride.Cold;
            },
            popupId,
            "Click toggles the configured Core Mode override on or off.",
            choiceColumnX,
            true);

        ImGui.Separator();
        DrawPopupRowLabel("Click", CalculatePopupLabelWidth(84f));
        choiceColumnX = ImGui.GetCursorPosX();
        DrawPopupChoiceRadioButton(
            "Toggle",
            AkronModule.Settings.CoreModeClickBehavior == AkronCoreModeClickBehavior.Toggle,
            () => AkronModule.Settings.CoreModeClickBehavior = AkronCoreModeClickBehavior.Toggle,
            popupId,
            "Clicking the row toggles the configured Hot/Cold override on or off.",
            choiceColumnX,
            false);
        DrawPopupChoiceRadioButton(
            "Cycle",
            AkronModule.Settings.CoreModeClickBehavior == AkronCoreModeClickBehavior.Cycle,
            () => AkronModule.Settings.CoreModeClickBehavior = AkronCoreModeClickBehavior.Cycle,
            popupId,
            "Clicking the row cycles directly between Hot and Cold and enables the override.",
            choiceColumnX,
            true);
    }

    private void DrawSetInventoryPopupControls(string popupId) {
        DrawIntStepperRow(
            "Dashes",
            () => AkronModule.Settings.SetInventoryDashes,
            value => AkronModule.Settings.SetInventoryDashes = AkronModuleSettings.ClampSetInventoryDashes(value),
            -1,
            1,
            0,
            5,
            popupId,
            "Dash inventory applied when Set Inventory is clicked.");

        DrawIntStepperRow(
            "Jumps",
            () => AkronModule.Settings.SetInventoryJumps,
            value => AkronModule.Settings.SetInventoryJumps = AkronModuleSettings.ClampSetInventoryJumps(value),
            -1,
            1,
            0,
            99,
            popupId,
            "Extra air jumps applied through Akron's Air Jumps setting. Zero disables extra jumps.");

        bool restoreOnDeath = AkronModule.Settings.SetInventoryRestoreOnDeath;
        if (ImGui.Checkbox("Restore on Death##" + popupId, ref restoreOnDeath)) {
            AkronModule.Settings.SetInventoryRestoreOnDeath = restoreOnDeath;
            if (!restoreOnDeath) {
                AkronActions.ClearSetInventory();
            }
        }
        DrawPopupTooltip("Restore the pre-click dash and Air Jumps settings on the next death.");

        if (ImGui.Button("Set##" + popupId) && Engine.Scene is Level level) {
            AkronActions.ApplySetInventory(level);
        }
        DrawPopupTooltip("Apply the configured inventory to Madeline now. Restore on Death controls whether a snapshot is armed.");

        ImGui.TextUnformatted(AkronModule.Settings.SetInventoryRestoreOnDeath
            ? "Restore snapshot is captured on apply."
            : "Chapter restart returns to map defaults.");
    }

    private void DrawDreamStatePopupControls(string popupId) {
        if (ImGui.Button("Toggle dream state##" + popupId) && Engine.Scene is Level level) {
            AkronActions.ToggleDreamState(level);
        }
        DrawPopupTooltip("Toggle whether Madeline's current inventory can use dream blocks.");
    }

    private void DrawGroundRefillsPopupControls(string popupId) {
        bool dash = AkronModule.Settings.GroundDashRefill;
        if (ImGui.Checkbox("Dash refill##" + popupId, ref dash)) {
            AkronModule.Settings.GroundDashRefill = dash;
        }
        DrawPopupTooltip("Allow ground contact to restore dash charges while Ground Refills is enabled.");

        bool stamina = AkronModule.Settings.GroundStaminaRefill;
        if (ImGui.Checkbox("Stamina refill##" + popupId, ref stamina)) {
            AkronModule.Settings.GroundStaminaRefill = stamina;
        }
        DrawPopupTooltip("Allow ground contact to restore stamina while Ground Refills is enabled.");
    }

    private void DrawLagPauserPopupControls(string popupId) {
        DrawIntStepperRow(
            "Threshold ms",
            () => AkronModule.Settings.LagPauserThresholdMs,
            value => AkronModule.Settings.LagPauserThresholdMs = AkronModuleSettings.ClampLagPauserThresholdMs(value),
            -25,
            25,
            50,
            5000,
            popupId,
            "Frame-time spike threshold that triggers the pause while Lag Pauser is toggled on.");

        AkronModuleSession session = AkronModule.Session;
        ImGui.TextUnformatted("Triggers: " + (session?.LagPauserTriggerCount.ToString(CultureInfo.InvariantCulture) ?? "0"));
        ImGui.TextUnformatted("Last spike: " + (session?.LagPauserLastSpikeMs.ToString("0.000", CultureInfo.InvariantCulture) ?? "0.000") + " ms");

        if (ImGui.Button("Reset threshold##" + popupId)) {
            AkronModule.Settings.LagPauserThresholdMs = 250;
        }
        DrawPopupTooltip("Restore the default 250 ms spike threshold.");
    }

    private void DrawDashRedirectDirectionsPopupControls(string popupId) {
        DrawDashRedirectDirectionCheckbox("Down", AkronDashRedirectDirection.Down, popupId, "Prevent Celeste from redirecting straight down dashes.");
        DrawDashRedirectDirectionCheckbox("Down-left", AkronDashRedirectDirection.DownLeft, popupId, "Prevent Celeste from redirecting down-left dashes.");
        DrawDashRedirectDirectionCheckbox("Down-right", AkronDashRedirectDirection.DownRight, popupId, "Prevent Celeste from redirecting down-right dashes.");
        DrawDashRedirectDirectionCheckbox("Left", AkronDashRedirectDirection.Left, popupId, "Prevent Celeste from redirecting left dashes.");
        DrawDashRedirectDirectionCheckbox("Right", AkronDashRedirectDirection.Right, popupId, "Prevent Celeste from redirecting right dashes.");
        DrawDashRedirectDirectionCheckbox("Up", AkronDashRedirectDirection.Up, popupId, "Prevent Celeste from redirecting straight up dashes.");
        DrawDashRedirectDirectionCheckbox("Up-left", AkronDashRedirectDirection.UpLeft, popupId, "Prevent Celeste from redirecting up-left dashes.");
        DrawDashRedirectDirectionCheckbox("Up-right", AkronDashRedirectDirection.UpRight, popupId, "Prevent Celeste from redirecting up-right dashes.");
        DrawDashRedirectDirectionCheckbox("Down diagonal", AkronDashRedirectDirection.DownLeft | AkronDashRedirectDirection.DownRight, popupId, "Prevent Celeste from redirecting down-left and down-right dashes.");
        DrawDashRedirectDirectionCheckbox("Up diagonal", AkronDashRedirectDirection.UpLeft | AkronDashRedirectDirection.UpRight, popupId, "Prevent Celeste from redirecting up-left and up-right dashes.");
        DrawDashRedirectDirectionCheckbox("All", AkronDashRedirectDirection.All, popupId, "Prevent dash redirects for every dash direction.");
    }

    private void DrawDashRedirectDirectionCheckbox(string label, AkronDashRedirectDirection direction, string popupId, string tooltip) {
        AkronDashRedirectDirection directions = AkronModuleSettings.NormalizeDashRedirectDirections(AkronModule.Settings.DashRedirectDirections);
        bool selected = (directions & direction) == direction;
        if (ImGui.Checkbox(label + "##" + popupId, ref selected)) {
            directions = selected ? directions | direction : directions & ~direction;
            AkronModule.Settings.DashRedirectDirections = directions == AkronDashRedirectDirection.None
                ? AkronDashRedirectDirection.Down
                : directions;
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawPopupChoiceCheckbox(string label, bool selected, Action apply, string popupId, string tooltip) {
        bool value = selected;
        if (ImGui.Checkbox(label + "##" + popupId, ref value) && value && !selected) {
            apply();
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawPopupChoiceRadioButton(
        string label,
        bool selected,
        Action apply,
        string popupId,
        string tooltip,
        float choiceColumnX = -1f,
        bool newLineBefore = false) {
        if (newLineBefore) {
            if (choiceColumnX >= 0f) {
                ImGui.SetCursorPosX(choiceColumnX);
            }
        }

        if (ImGui.RadioButton(label + "##" + popupId, selected)) {
            apply();
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawBerryObtainOptionsPopupControls(string popupId) {
        bool regular = AkronModule.Settings.BerryObtainIncludeRegular;
        if (ImGui.Checkbox("Regular strawberries##" + popupId, ref regular)) {
            AkronModule.Settings.BerryObtainIncludeRegular = regular;
        }
        DrawPopupTooltip("Include normal red strawberries and winged red strawberries in obtain actions.");

        bool golden = AkronModule.Settings.BerryObtainIncludeGolden;
        if (ImGui.Checkbox("Golden berries##" + popupId, ref golden)) {
            AkronModule.Settings.BerryObtainIncludeGolden = golden;
        }
        DrawPopupTooltip("Include goldenBerry entities and 1A's winged golden memorial berry in obtain actions.");

        bool moon = AkronModule.Settings.BerryObtainIncludeMoon;
        if (ImGui.Checkbox("Moon berry##" + popupId, ref moon)) {
            AkronModule.Settings.BerryObtainIncludeMoon = moon;
        }
        DrawPopupTooltip("Include Farewell's moon berry when it exists in the selected room/chapter.");
    }
}
