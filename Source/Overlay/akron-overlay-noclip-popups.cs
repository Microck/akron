using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawNoclipPopupControls(string popupId) {
        DrawFloatStepperRow(
            "Speed",
            () => AkronModule.Settings.NoclipSpeed / 240f,
            value => SetNoclipSpeedMultiplier(value),
            -0.1f,
            0.1f,
            AkronModuleSettings.ClampNoclipSpeed(20) / 240f,
            AkronModuleSettings.ClampNoclipSpeed(900) / 240f,
            "%.1f",
            popupId,
            "Normal noclip movement speed.");

        DrawFloatStepperRow(
            "Grab speed",
            () => AkronModule.Settings.NoclipFloatSpeed / 90f,
            value => SetNoclipGrabSpeedMultiplier(value),
            -0.1f,
            0.1f,
            AkronModuleSettings.ClampNoclipFloatSpeed(10) / 90f,
            AkronModuleSettings.ClampNoclipFloatSpeed(450) / 90f,
            "%.1f",
            popupId,
            "Movement speed while Grab is held during noclip.");

        bool drawOnTop = AkronModule.Settings.NoclipDrawOnTop;
        if (ImGui.Checkbox("Draw Madeline on top##" + popupId, ref drawOnTop)) {
            AkronModule.Settings.NoclipDrawOnTop = drawOnTop;
        }
        DrawPopupTooltip("Render Madeline above most objects while noclip is active.");

        bool hidePlayer = AkronModule.Settings.NoclipHidePlayer;
        if (ImGui.Checkbox("Hide Madeline##" + popupId, ref hidePlayer)) {
            AkronModule.Settings.NoclipHidePlayer = hidePlayer;
        }
        DrawPopupTooltip("Hide the player sprite while noclip is active. Movement and camera follow still work.");
    }

    private void DrawHazardAccuracyPopupControls(string popupId) {
        DrawIntStepperRow(
            "Invalid limit",
            () => AkronModule.Settings.NoclipAccuracyInvalidLimit,
            value => AkronModule.Settings.NoclipAccuracyInvalidLimit = AkronModuleSettings.ClampNoclipAccuracyInvalidLimit(value),
            -1,
            1,
            0,
            999,
            popupId,
            "Optional invalid-contact warning threshold. Zero disables the warning.");

        bool tint = AkronModule.Settings.NoclipAccuracyTint;
        if (ImGui.Checkbox("Screen tint##" + popupId, ref tint)) {
            AkronModule.Settings.NoclipAccuracyTint = tint;
        }
        DrawPopupTooltip("Tint the gameplay screen when Hazard Accuracy detects invalid contact.");

        DrawNoclipAccuracyTintModeChoice("On invalid entry", AkronNoclipAccuracyTintMode.OnInvalidEntry, popupId);
        DrawNoclipAccuracyTintModeChoice("While touching", AkronNoclipAccuracyTintMode.WhileTouching, popupId);

        DrawHitboxColorRow(
            "Tint color",
            () => AkronModule.Settings.NoclipAccuracyTintColor,
            value => AkronModule.Settings.NoclipAccuracyTintColor = value,
            popupId,
            "Screen tint color used when Hazard Accuracy detects invalid contact.");

        DrawIntStepperRow(
            "Tint opacity",
            () => AkronModule.Settings.NoclipAccuracyTintOpacity,
            value => AkronModule.Settings.NoclipAccuracyTintOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Tint opacity percentage.");

        DrawIntStepperRow(
            "Tint duration",
            () => AkronModule.Settings.NoclipAccuracyTintDurationMs,
            value => AkronModule.Settings.NoclipAccuracyTintDurationMs = AkronModuleSettings.ClampNoclipAccuracyTintDurationMs(value),
            -100,
            100,
            0,
            5000,
            popupId,
            "Tint fade duration in milliseconds after invalid contact stops.");

        AkronNoclipAccuracySnapshot snapshot = AkronModule.GetNoclipAccuracySnapshot();
        ImGui.TextUnformatted("Accuracy: " + snapshot.Describe());
        if (ImGui.Button("Reset Accuracy##" + popupId)) {
            AkronModule.ResetNoclipAccuracy();
        }
        ImGui.SameLine();
        if (ImGui.Button("Defaults##" + popupId)) {
            AkronModule.Settings.ResetHazardAccuracyDefaults();
            AkronModule.ResetNoclipAccuracy();
        }
        DrawPopupTooltip("Restore default tint settings: tint off, red, 90% opacity, and 0 ms duration.");
    }

    private void DrawNoclipAccuracyTintModeChoice(string label, AkronNoclipAccuracyTintMode mode, string popupId) {
        bool selected = AkronModule.Settings.NoclipAccuracyTintMode == mode;
        if (ImGui.RadioButton(label + "##noclip-tint-" + popupId, selected)) {
            AkronModule.Settings.NoclipAccuracyTintMode = mode;
        }
        DrawPopupTooltip(mode == AkronNoclipAccuracyTintMode.WhileTouching
            ? "Keep refreshing the tint for as long as Madeline overlaps invalid terrain or hazards."
            : "Flash the tint once when a new invalid contact streak begins.");
    }
}
