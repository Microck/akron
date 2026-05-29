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

    private void DrawPreventDownDashRedirectsPopupControls(string popupId) {
        DrawPopupChoiceCheckbox("Normal", AkronModule.Settings.PreventDownDashRedirects == AkronPreventDownDashRedirectMode.Normal, () => AkronModule.Settings.PreventDownDashRedirects = AkronPreventDownDashRedirectMode.Normal, popupId, "Restore pure down when no horizontal input is held.");
        DrawPopupChoiceCheckbox("Diagonal", AkronModule.Settings.PreventDownDashRedirects == AkronPreventDownDashRedirectMode.Diagonal, () => AkronModule.Settings.PreventDownDashRedirects = AkronPreventDownDashRedirectMode.Diagonal, popupId, "Preserve diagonal down redirects.");
    }

    private static void DrawPopupChoiceCheckbox(string label, bool selected, Action apply, string popupId, string tooltip) {
        bool value = selected;
        if (ImGui.Checkbox(label + "##" + popupId, ref value) && value && !selected) {
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
