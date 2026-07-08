using System;
using System.Collections.Generic;
using Celeste;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawStartPosPopupControls(string popupId) {
        if (ImGui.Button("Set##startpos_set" + popupId) && Engine.Scene is Level setLevel) {
            AkronActions.SetStartPos(setLevel);
        }
        DrawPopupTooltip("Capture the current player state into the active StartPos slot.");

        ImGui.SameLine();
        if (ImGui.Button("Load##startpos_load" + popupId) && Engine.Scene is Level loadLevel) {
            AkronActions.LoadStartPos(loadLevel);
        }
        DrawPopupTooltip("Load the active StartPos.");

        ImGui.SameLine();
        if (ImGui.Button("Clear##startpos_clear" + popupId)) {
            AkronActions.ClearActiveStartPos();
        }
        DrawPopupTooltip("Clear the active StartPos slot.");

        ImGui.TextUnformatted("Index: " + (Engine.Scene is Level indexLevel ? AkronActions.DescribeStartPosIndex(indexLevel) : "0/0"));
        DrawStartPosConfigControls(popupId);

        ImGui.Separator();
        ImGui.TextUnformatted("Menu bindings");
        DrawPopupActionBindingRow("Set", PopupActionKey("StartPos", "Set"), "StartPos / Set", popupId);
        DrawPopupActionBindingRow("Load", PopupActionKey("StartPos", "Load"), "StartPos / Load", popupId);
        DrawPopupActionBindingRow("Clear", PopupActionKey("StartPos", "Clear"), "StartPos / Clear", popupId);
        for (int slot = 1; slot <= 9; slot++) {
            DrawPopupActionBindingRow(
                "Slot " + slot,
                PopupActionKey("StartPos", "Load Slot " + slot),
                "StartPos / Load Slot " + slot,
                popupId);
        }
    }

    private void DrawStartPosSwitcherPopupControls(string popupId) {
        if (ImGui.Button("Previous##startpos_switcher" + popupId) && Engine.Scene is Level previousLevel) {
            AkronActions.ShiftStartPos(previousLevel, -1);
        }
        DrawPopupTooltip("Cycle to the previous StartPos in chapter order.");

        ImGui.SameLine();
        if (ImGui.Button("Next##startpos_switcher" + popupId) && Engine.Scene is Level nextLevel) {
            AkronActions.ShiftStartPos(nextLevel, 1);
        }
        DrawPopupTooltip("Cycle to the next StartPos in chapter order.");

        ImGui.SameLine();
        ImGui.TextUnformatted("Index: " + (Engine.Scene is Level indexLevel ? AkronActions.DescribeStartPosIndex(indexLevel) : "0/0"));

        ImGui.Separator();
        ImGui.TextUnformatted("Menu bindings");
        DrawPopupActionBindingRow("Previous", PopupActionKey("StartPos", "Previous"), "StartPos / Previous", popupId);
        DrawPopupActionBindingRow("Next", PopupActionKey("StartPos", "Next"), "StartPos / Next", popupId);
    }

    private void DrawPopupActionBindingRow(string label, string actionKey, string displayName, string popupId) {
        ImGui.TextUnformatted(label + ": " + DescribeMenuBinding(actionKey));
        ImGui.SameLine();
        if (ImGui.Button("Bind##" + label + popupId)) {
            StartBindingCapture(actionKey, displayName);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##" + label + popupId)) {
            ClearMenuBinding(actionKey);
        }
    }

    private static string DescribeStartPosSwitcherBindings() {
        return DescribeMenuBinding(PopupActionKey("StartPos", "Previous")) + " / " +
               DescribeMenuBinding(PopupActionKey("StartPos", "Next"));
    }

    private void DrawPlaceStartPosPopupControls(string popupId, bool includePlacementToggle = true) {
        if (includePlacementToggle) {
            bool mousePlacement = AkronModule.Settings.StartPosMousePlacement;
            if (ImGui.Checkbox("Placement mode##" + popupId, ref mousePlacement)) {
                AkronModule.Settings.StartPosMousePlacement = mousePlacement;
            }
            DrawPopupTooltip("Enter the frozen free-camera placement editor.");

            if (ImGui.Button("Open editor##" + popupId, new NumericsVector2(112f, 0f))) {
                AkronModule.Settings.StartPosMousePlacement = true;
            }
            DrawPopupTooltip("Freeze gameplay, activate free camera, and place StartPos previews with the mouse.");
        }

        DrawStartPosConfigControls(popupId);
        DrawIntStepperRow("Preview opacity", () => AkronModule.Settings.StartPosPreviewOpacity, value => AkronModule.Settings.StartPosPreviewOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Mouse placement preview opacity percentage.");
    }

    private void DrawStartPosConfigControls(string popupId) {
        DrawIntStepperRow("Slot count", () => AkronModule.Settings.StartPosSlotCount, value => AkronModule.Settings.StartPosSlotCount = AkronModuleSettings.ClampStartPosSlotCount(value), -1, 1, 1, 99, popupId, "Selectors and previous/next always expose at least 15 StartPos slots. Raise this when you want more.");
        DrawIntStepperRow("Dashes", () => ActiveStartPosDashes(), SetActiveStartPosDashes, -1, 1, -1, 5, popupId, "-1 keeps the native/current dash count. 0-5 force that many dashes after spawning.");
        DrawIntStepperRow("Stamina %", () => ActiveStartPosStaminaPercent(), SetActiveStartPosStaminaPercent, -5, 5, -1, 100, popupId, "-1 keeps native/current stamina. 0-100 forces stamina after spawning.");
        DrawPopupChoiceCombo(
            "Facing",
            () => ActiveStartPosFacing().ToString(),
            BuildStartPosFacingChoices(),
            popupId,
            "Current keeps the native facing. Left and Right force the direction after spawning.");

        bool idle = ActiveStartPosIdle();
        if (ImGui.Checkbox("Idle speed##" + popupId, ref idle)) {
            SetActiveStartPosIdle(idle);
        }
        DrawPopupTooltip("Clear speed after spawning so the StartPos begins from an idle state.");

        bool grab = ActiveStartPosGrab();
        if (ImGui.Checkbox("Spawn grabbing##" + popupId, ref grab)) {
            SetActiveStartPosGrab(grab);
        }
        DrawPopupTooltip("Attempt to enter Celeste's climb/grab state after spawning.");
    }

    private static int ActiveStartPosDashes() {
        return AkronActions.GetActiveStartPos()?.Dashes ?? AkronModule.Settings.StartPosConfiguredDashes;
    }

    private static void SetActiveStartPosDashes(int dashes) {
        AkronModule.Settings.StartPosConfiguredDashes = AkronModuleSettings.ClampStartPosDashes(dashes);
        if (AkronActions.GetActiveStartPos() is AkronStartPos startPos) {
            AkronActions.ApplyStartPosConfiguration(startPos);
        }
    }

    private static int ActiveStartPosStaminaPercent() {
        return AkronActions.GetActiveStartPos()?.StaminaPercent ?? AkronModule.Settings.StartPosConfiguredStaminaPercent;
    }

    private static void SetActiveStartPosStaminaPercent(int staminaPercent) {
        AkronModule.Settings.StartPosConfiguredStaminaPercent = AkronModuleSettings.ClampStartPosStaminaPercent(staminaPercent);
        if (AkronActions.GetActiveStartPos() is AkronStartPos startPos) {
            AkronActions.ApplyStartPosConfiguration(startPos);
        }
    }

    private static AkronStartPosFacing ActiveStartPosFacing() {
        return AkronActions.GetActiveStartPos()?.Facing ?? AkronModule.Settings.StartPosConfiguredFacing;
    }

    private static void SetActiveStartPosFacing(AkronStartPosFacing facing) {
        AkronModule.Settings.StartPosConfiguredFacing = facing;
        if (AkronActions.GetActiveStartPos() is AkronStartPos startPos) {
            AkronActions.ApplyStartPosConfiguration(startPos);
        }
    }

    private static bool ActiveStartPosIdle() {
        return AkronActions.GetActiveStartPos()?.Idle ?? AkronModule.Settings.StartPosConfiguredIdle;
    }

    private static void SetActiveStartPosIdle(bool idle) {
        AkronModule.Settings.StartPosConfiguredIdle = idle;
        if (AkronActions.GetActiveStartPos() is AkronStartPos startPos) {
            AkronActions.ApplyStartPosConfiguration(startPos);
        }
    }

    private static bool ActiveStartPosGrab() {
        return AkronActions.GetActiveStartPos()?.Grab ?? AkronModule.Settings.StartPosConfiguredGrab;
    }

    private static void SetActiveStartPosGrab(bool grab) {
        AkronModule.Settings.StartPosConfiguredGrab = grab;
        if (AkronActions.GetActiveStartPos() is AkronStartPos startPos) {
            AkronActions.ApplyStartPosConfiguration(startPos);
        }
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildStartPosFacingChoices() {
        return new[] {
            new SelectorDropdownChoice("Current", () => ActiveStartPosFacing() == AkronStartPosFacing.Current, () => SetActiveStartPosFacing(AkronStartPosFacing.Current)),
            new SelectorDropdownChoice("Left", () => ActiveStartPosFacing() == AkronStartPosFacing.Left, () => SetActiveStartPosFacing(AkronStartPosFacing.Left)),
            new SelectorDropdownChoice("Right", () => ActiveStartPosFacing() == AkronStartPosFacing.Right, () => SetActiveStartPosFacing(AkronStartPosFacing.Right))
        };
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildStartPosSlotChoices() {
        List<SelectorDropdownChoice> choices = new List<SelectorDropdownChoice>();
        int slotCount = AkronModuleSettings.ClampStartPosSelectableSlotCount(AkronModule.Settings.StartPosSlotCount);
        for (int slot = 1; slot <= slotCount; slot++) {
            int capturedSlot = slot;
            choices.Add(new SelectorDropdownChoice(
                "Slot " + capturedSlot,
                () => AkronModule.Settings.ActiveStartPosSlot == capturedSlot,
                () => AkronActions.SetStartPosSlot(capturedSlot)));
        }

        return choices;
    }
}
