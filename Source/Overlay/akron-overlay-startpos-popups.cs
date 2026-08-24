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
        DrawPopupActionBindingContext("StartPos", "Set");
        DrawPopupTooltip("Capture the current player state into the active StartPos slot.");

        ImGui.SameLine();
        if (ImGui.Button("Load##startpos_load" + popupId) && Engine.Scene is Level loadLevel) {
            AkronActions.LoadStartPos(loadLevel);
        }
        DrawPopupActionBindingContext("StartPos", "Load");
        DrawPopupTooltip("Load the active StartPos.");

        ImGui.SameLine();
        if (ImGui.Button("Clear##startpos_clear" + popupId)) {
            AkronActions.ClearActiveStartPos();
        }
        DrawPopupActionBindingContext("StartPos", "Clear");
        DrawPopupTooltip("Clear the active StartPos slot.");

        ImGui.TextUnformatted("Index: " + (Engine.Scene is Level indexLevel ? AkronActions.DescribeStartPosIndex(indexLevel) : "0/0"));
        DrawStartPosDirectSlotControls(popupId);

        bool respawn = AkronModule.Settings.RespawnAtStartPos;
        if (ImGui.Checkbox("Respawn here##" + popupId, ref respawn)) {
            AkronModule.Settings.RespawnAtStartPos = respawn;
        }
        DrawPopupActionBindingContext("StartPos", "Respawn", "StartPos / Respawn Here");
        DrawPopupTooltip("Respawn at the active StartPos after death.");

        bool waitForInput = AkronModule.Settings.StartPosWaitForInput;
        if (ImGui.Checkbox("Wait for input after load##" + popupId, ref waitForInput)) {
            AkronModule.Settings.StartPosWaitForInput = waitForInput;
        }
        DrawPopupTooltip("Hold the restored frame until a fresh gameplay input, while backdrops and wipes continue.");

        DrawStartPosConfigControls(popupId);

    }

    private void DrawStartPosDirectSlotControls(string popupId) {
        ImGui.TextUnformatted("Load slot:");
        for (int slot = 1; slot <= 9; slot++) {
            if (slot > 1) {
                ImGui.SameLine();
            }

            string actionName = "Load Slot " + slot;
            if (ImGui.Button(slot + "##startpos_load_slot_" + slot + popupId) &&
                Engine.Scene is Level level) {
                AkronActions.LoadStartPosSlot(level, slot);
            }
            DrawPopupActionBindingContext("StartPos", actionName);
            DrawPopupTooltip("Load StartPos slot " + slot + ". Right-click to bind.");
        }
    }

    private void DrawStartPosSwitcherPopupControls(string popupId) {
        if (ImGui.Button("Previous##startpos_switcher" + popupId) && Engine.Scene is Level previousLevel) {
            AkronActions.ShiftStartPos(previousLevel, -1);
        }
        DrawPopupActionBindingContext("StartPos", "Previous");
        DrawPopupTooltip("Cycle to the previous StartPos in chapter order.");

        ImGui.SameLine();
        if (ImGui.Button("Next##startpos_switcher" + popupId) && Engine.Scene is Level nextLevel) {
            AkronActions.ShiftStartPos(nextLevel, 1);
        }
        DrawPopupActionBindingContext("StartPos", "Next");
        DrawPopupTooltip("Cycle to the next StartPos in chapter order.");

        ImGui.SameLine();
        ImGui.TextUnformatted("Index: " + (Engine.Scene is Level indexLevel ? AkronActions.DescribeStartPosIndex(indexLevel) : "0/0"));

    }

    private static string DescribeStartPosSwitcherBindings() {
        return DescribePopupActionBinding(PopupActionKey("StartPos", "Previous")) + " / " +
               DescribePopupActionBinding(PopupActionKey("StartPos", "Next"));
    }

    private static string DescribePopupActionBinding(string actionKey) {
        if (HasMenuBinding(actionKey)) {
            return DescribeMenuBinding(actionKey);
        }

        return TryGetDefaultButtonBinding(actionKey, out ButtonBinding binding) && !IsEmptyBinding(binding)
            ? AkronModuleSettings.DescribeBinding(binding)
            : "Unbound";
    }

    private void DrawPlaceStartPosPopupControls(string popupId, bool includePlacementToggle = true) {
        if (includePlacementToggle) {
            bool mousePlacement = AkronModule.Settings.StartPosMousePlacement;
            if (ImGui.Checkbox("Placement mode##" + popupId, ref mousePlacement)) {
                AkronModule.Settings.StartPosMousePlacement = mousePlacement;
            }
            DrawPopupActionBindingContext("StartPos", "Place");
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
        DrawIntStepperRow("Slot count", () => AkronModule.Settings.StartPosSlotCount, value => AkronModule.Settings.StartPosSlotCount = AkronModuleSettings.ClampStartPosSlotCount(value), -1, 1, 1, AkronModuleSettings.MaximumStartPosSlots, popupId, "Selectors and previous/next always expose at least 15 StartPos slots. Raise this when you want more. Each slot you capture holds a full savestate in memory, so a high count costs memory.");
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
