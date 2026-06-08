using System;
using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawMadelineColorsPopupControls(string popupId) {
        bool noDash = AkronModule.Settings.MadelineColorNoDash;
        if (ImGui.Checkbox("No-dash color##" + popupId, ref noDash)) {
            AkronModule.Settings.MadelineColorNoDash = noDash;
        }
        DrawPopupTooltip("Customize depleted-dash blue hair.");

        bool oneDash = AkronModule.Settings.MadelineColorOneDash;
        if (ImGui.Checkbox("One-dash color##" + popupId, ref oneDash)) {
            AkronModule.Settings.MadelineColorOneDash = oneDash;
        }
        DrawPopupTooltip("Customize normal dash-available hair. This is the only custom state enabled by default.");

        bool twoDash = AkronModule.Settings.MadelineColorTwoDash;
        if (ImGui.Checkbox("Two-dash color##" + popupId, ref twoDash)) {
            AkronModule.Settings.MadelineColorTwoDash = twoDash;
        }
        DrawPopupTooltip("Customize two-dash pink hair.");

        bool threeDash = AkronModule.Settings.MadelineColorThreeDash;
        if (ImGui.Checkbox("Three-dash color##" + popupId, ref threeDash)) {
            AkronModule.Settings.MadelineColorThreeDash = threeDash;
        }
        DrawPopupTooltip("Customize hair when Madeline has three dashes.");

        bool fourDash = AkronModule.Settings.MadelineColorFourDash;
        if (ImGui.Checkbox("Four-dash color##" + popupId, ref fourDash)) {
            AkronModule.Settings.MadelineColorFourDash = fourDash;
        }
        DrawPopupTooltip("Customize hair when Madeline has four dashes.");

        bool fiveDash = AkronModule.Settings.MadelineColorFiveDash;
        if (ImGui.Checkbox("Five-dash color##" + popupId, ref fiveDash)) {
            AkronModule.Settings.MadelineColorFiveDash = fiveDash;
        }
        DrawPopupTooltip("Customize hair when Madeline has five or more dashes.");

        bool gradient = AkronModule.Settings.MadelineColorGradient;
        if (ImGui.Checkbox("Gradient##" + popupId, ref gradient)) {
            AkronModule.Settings.MadelineColorGradient = gradient;
        }
        DrawPopupTooltip("Animate customized states between Gradient A and Gradient B.");

        DrawFloatValueRow(
            "Speed",
            () => AkronModule.Settings.MadelineColorGradientSpeed,
            value => AkronModule.Settings.MadelineColorGradientSpeed = AkronModuleSettings.ClampMadelineGradientSpeed(value),
            -0.1f,
            0.1f,
            0.1f,
            10f,
            "%.1f",
            popupId,
            "Gradient cycle speed.");

        DrawHitboxColorRow("No dash", () => AkronModule.Settings.MadelineNoDashColor, value => AkronModule.Settings.MadelineNoDashColor = value, popupId, "Color used when Madeline has no dash.");
        DrawHitboxColorRow("One dash", () => AkronModule.Settings.MadelineOneDashColor, value => AkronModule.Settings.MadelineOneDashColor = value, popupId, "Color used for baseline dash-available Madeline.");
        DrawHitboxColorRow("Two dash", () => AkronModule.Settings.MadelineTwoDashColor, value => AkronModule.Settings.MadelineTwoDashColor = value, popupId, "Color used when Madeline has two dashes.");
        DrawHitboxColorRow("Three dash", () => AkronModule.Settings.MadelineThreeDashColor, value => AkronModule.Settings.MadelineThreeDashColor = value, popupId, "Color used when Madeline has three dashes.");
        DrawHitboxColorRow("Four dash", () => AkronModule.Settings.MadelineFourDashColor, value => AkronModule.Settings.MadelineFourDashColor = value, popupId, "Color used when Madeline has four dashes.");
        DrawHitboxColorRow("Five dash", () => AkronModule.Settings.MadelineFiveDashColor, value => AkronModule.Settings.MadelineFiveDashColor = value, popupId, "Color used when Madeline has five or more dashes.");
        DrawHitboxColorRow("Gradient A", () => AkronModule.Settings.MadelineGradientColorA, value => AkronModule.Settings.MadelineGradientColorA = value, popupId, "Gradient start color.");
        DrawHitboxColorRow("Gradient B", () => AkronModule.Settings.MadelineGradientColorB, value => AkronModule.Settings.MadelineGradientColorB = value, popupId, "Gradient end color.");
    }

    private void DrawTrailVisibilityPopupControls(string popupId) {
        if (ImGui.Button("Mode: " + AkronModule.Settings.TrailVisibility + "##" + popupId)) {
            AkronModule.Settings.TrailVisibility = NextTrailVisibility(AkronModule.Settings.TrailVisibility);
            AkronModule.Settings.SetNoTrails(AkronModule.Settings.TrailVisibility == AkronTrailVisibility.Hidden);
        }
        DrawPopupTooltip("Vanilla keeps Celeste behavior, Hidden clears trails, Always emits a lightweight trail continuously.");

        DrawIntStepperRow(
            "Cut rate",
            () => AkronModule.Settings.TrailCuttingRate,
            value => AkronModule.Settings.TrailCuttingRate = AkronModuleSettings.ClampTrailCuttingRate(value),
            -1,
            1,
            1,
            12,
            popupId,
            "Emit one forced trail every N frames in Always mode. 1 is the densest trail.");
    }

    private void DrawMadelineHairLengthPopupControls(string popupId) {
        DrawIntStepperRow("No dash", () => AkronModule.Settings.MadelineNoDashHairLength, value => AkronModule.Settings.MadelineNoDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has no dash.");
        DrawIntStepperRow("One dash", () => AkronModule.Settings.MadelineOneDashHairLength, value => AkronModule.Settings.MadelineOneDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has one dash.");
        DrawIntStepperRow("Two dash", () => AkronModule.Settings.MadelineTwoDashHairLength, value => AkronModule.Settings.MadelineTwoDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has two dashes.");
        DrawIntStepperRow("Three dash", () => AkronModule.Settings.MadelineThreeDashHairLength, value => AkronModule.Settings.MadelineThreeDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has three dashes.");
        DrawIntStepperRow("Four dash", () => AkronModule.Settings.MadelineFourDashHairLength, value => AkronModule.Settings.MadelineFourDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has four dashes.");
        DrawIntStepperRow("Five dash", () => AkronModule.Settings.MadelineFiveDashHairLength, value => AkronModule.Settings.MadelineFiveDashHairLength = AkronModuleSettings.ClampMadelineHairLength(value), -1, 1, 1, 100, popupId, "Hair segment count when Madeline has five or more dashes.");
    }

    private void DrawMadelineEffectSyncPopupControls(string popupId) {
        DrawMadelineEffectSyncModeRow("Dash particles", () => AkronModule.Settings.MadelineDashParticleSync, value => AkronModule.Settings.MadelineDashParticleSync = value, popupId, "Match dash burst particles to active hair color.");
        DrawMadelineEffectSyncModeRow("Dash trail", () => AkronModule.Settings.MadelineDashTrailSync, value => AkronModule.Settings.MadelineDashTrailSync = value, popupId, "Match custom trail color to active hair color.");
        DrawMadelineEffectSyncModeRow("Death effect", () => AkronModule.Settings.MadelineDeathEffectSync, value => AkronModule.Settings.MadelineDeathEffectSync = value, popupId, "Match the death burst effect to active hair color.");
        DrawMadelineEffectSyncModeRow("Feather color", () => AkronModule.Settings.MadelineFeatherColorSync, value => AkronModule.Settings.MadelineFeatherColorSync = value, popupId, "Allow Madeline Colors to keep coloring feather-state hair.");
        DrawMadelineEffectSyncModeRow("Crown color", () => AkronModule.Settings.MadelineCrownColorSync, value => AkronModule.Settings.MadelineCrownColorSync = value, popupId, "Match compatible crown sprites to active hair color.");
    }

    private void DrawMadelineEffectSyncModeRow(
        string label,
        Func<AkronMadelineEffectSyncMode> getter,
        Action<AkronMadelineEffectSyncMode> setter,
        string popupId,
        string tooltip) {
        if (ImGui.Button(label + ": " + FormatMadelineEffectSyncMode(getter()) + "##" + popupId)) {
            setter(getter() == AkronMadelineEffectSyncMode.MatchHair
                ? AkronMadelineEffectSyncMode.Off
                : AkronMadelineEffectSyncMode.MatchHair);
        }
        DrawPopupTooltip(tooltip);
    }

    private void DrawCustomTrailPopupControls(string popupId) {
        if (ImGui.Button("Mode: " + AkronModule.Settings.CustomTrailMode + "##" + popupId)) {
            AkronModule.Settings.CustomTrailMode = AkronModule.Settings.CustomTrailMode == AkronCustomTrailMode.Fixed
                ? AkronCustomTrailMode.Rainbow
                : AkronCustomTrailMode.Fixed;
        }
        DrawPopupTooltip("Fixed uses the configured color; Rainbow cycles hue over time.");

        bool pulse = AkronModule.Settings.CustomTrailPulse;
        if (ImGui.Checkbox("Pulse##" + popupId, ref pulse)) {
            AkronModule.Settings.CustomTrailPulse = pulse;
        }
        DrawPopupTooltip("Pulse custom trail brightness over time.");

        DrawIntStepperRow(
            "Cut rate",
            () => AkronModule.Settings.TrailCuttingRate,
            value => AkronModule.Settings.TrailCuttingRate = AkronModuleSettings.ClampTrailCuttingRate(value),
            -1,
            1,
            1,
            12,
            popupId,
            "Emit one custom trail every N frames. 1 is the densest trail.");

        DrawIntStepperRow("Opacity", () => AkronModule.Settings.CustomTrailOpacity, value => AkronModule.Settings.CustomTrailOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Custom trail opacity percentage.");

        DrawFloatValueRow(
            "Rainbow",
            () => AkronModule.Settings.CustomTrailRainbowSpeed,
            value => AkronModule.Settings.CustomTrailRainbowSpeed = AkronModuleSettings.ClampCustomTrailRainbowSpeed(value),
            -0.1f,
            0.1f,
            0.1f,
            10f,
            "%.1f",
            popupId,
            "Rainbow cycle speed.");

        DrawHitboxColorRow("Color", () => AkronModule.Settings.CustomTrailColor, value => AkronModule.Settings.CustomTrailColor = value, popupId, "Fixed custom trail color.");
    }
}
