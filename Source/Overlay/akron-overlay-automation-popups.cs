using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawAutoKillPopupControls(string popupId) {
        bool timer = AkronModule.Settings.AutoKillTimer;
        if (ImGui.Checkbox("Timer kill##" + popupId, ref timer)) {
            AkronModule.Settings.AutoKillTimer = timer;
        }
        DrawPopupTooltip("Kill the attempt when current map time reaches the configured threshold.");

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

        bool area = AkronModule.Settings.AutoKillArea;
        if (ImGui.Checkbox("Area kill##" + popupId, ref area)) {
            AkronModule.Settings.AutoKillArea = area && AkronModule.GetAutoKillAreas().Count > 0;
        }
        DrawPopupTooltip("Kill the attempt when Madeline enters any selected rectangle.");

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
