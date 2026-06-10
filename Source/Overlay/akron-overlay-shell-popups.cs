using System;
using System.Collections.Generic;
using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawOverlayAppearancePopupControls(string popupId) {
        if (ImGui.Button("Theme: " + AkronOverlayThemes.CurrentDisplayName() + "##" + popupId)) {
            AkronModule.Settings.OverlayThemePreset = NextOverlayThemePreset(AkronModule.Settings.OverlayThemePreset);
        }
        DrawPopupTooltip("Cycle Akron's built-in themes and the custom theme slot.");

        if (ImGui.Button("Copy to Custom##" + popupId)) {
            AkronOverlayThemes.CopyPresetToCustom();
        }
        DrawPopupTooltip("Copy the current theme colors and presentation values into the editable Custom slot.");

        ImGui.SameLine();
        if (ImGui.Button("Export .akr##" + popupId)) {
            AkronOverlayThemes.ExportCurrentTheme();
        }
        DrawPopupTooltip("Export the current overlay theme as a single-purpose .akr theme pack.");

        if (ImGui.Button("Import Latest .akr##" + popupId)) {
            AkronOverlayThemes.ImportLatestTheme();
        }
        DrawPopupTooltip("Import the newest .akr theme pack from Saves/AkronThemes.");

        string customName = AkronModule.Settings.CustomOverlayThemeName ?? "Custom";
        if (DrawPopupInputText("Custom name", ref customName, 48, popupId, 154f)) {
            AkronModule.Settings.CustomOverlayThemeName = string.IsNullOrWhiteSpace(customName) ? "Custom" : customName.Trim();
            AkronModule.Settings.OverlayThemePreset = AkronOverlayThemePreset.Custom;
        }
        DrawPopupTooltip("Name used when the custom theme is shown or exported.");

        DrawThemeColorRow("Window", () => AkronModule.Settings.CustomOverlayWindowColor, value => AkronModule.Settings.CustomOverlayWindowColor = value, popupId, "Custom theme window/background color.");
        DrawThemeColorRow("Header", () => AkronModule.Settings.CustomOverlayHeaderColor, value => AkronModule.Settings.CustomOverlayHeaderColor = value, popupId, "Custom theme header color.");
        DrawThemeColorRow("Header hover", () => AkronModule.Settings.CustomOverlayHeaderHoverColor, value => AkronModule.Settings.CustomOverlayHeaderHoverColor = value, popupId, "Custom theme hovered header and active UI color.");
        DrawThemeColorRow("Frame", () => AkronModule.Settings.CustomOverlayFrameColor, value => AkronModule.Settings.CustomOverlayFrameColor = value, popupId, "Custom theme input/value box color.");
        DrawThemeColorRow("Text", () => AkronModule.Settings.CustomOverlayTextColor, value => AkronModule.Settings.CustomOverlayTextColor = value, popupId, "Custom theme main text color.");
        DrawThemeColorRow("Muted", () => AkronModule.Settings.CustomOverlayMutedColor, value => AkronModule.Settings.CustomOverlayMutedColor = value, popupId, "Custom theme inactive indicator color.");
        DrawThemeColorRow("Disabled", () => AkronModule.Settings.CustomOverlayDisabledColor, value => AkronModule.Settings.CustomOverlayDisabledColor = value, popupId, "Custom theme disabled-row text color.");

        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.OverlayOpacity,
            value => AkronModule.Settings.OverlayOpacity = AkronModuleSettings.ClampOverlayOpacity(value),
            -5,
            5,
            55,
            100,
            popupId,
            "Overlay background opacity. Lower values make lists more transparent.");

        DrawIntStepperRow(
            "UI scale",
            () => AkronModule.Settings.OverlayScale,
            value => AkronModule.Settings.OverlayScale = AkronModuleSettings.ClampOverlayScale(value),
            -5,
            5,
            75,
            150,
            popupId,
            "Scale ImGui overlay windows for DPI and distance readability.");

        DrawIntStepperRow("Blur", () => AkronModule.Settings.OverlayBlur, value => AkronModule.Settings.OverlayBlur = AkronModuleSettings.ClampOverlayBlur(value), -5, 5, 0, 100, popupId, "Stored blur amount for overlay presentation. SpriteBatch fallback stays unblurred.");
        DrawIntStepperRow("Anim ms", () => AkronModule.Settings.OverlayAnimationMs, value => AkronModule.Settings.OverlayAnimationMs = AkronModuleSettings.ClampOverlayAnimationMs(value), -20, 20, 0, 500, popupId, "Stored animation duration for overlay presentation polish.");

        bool floating = AkronModule.Settings.FloatingButton;
        if (ImGui.Checkbox("Floating button##" + popupId, ref floating)) {
            AkronModule.Settings.FloatingButton = floating;
        }
        DrawPopupTooltip("Enable floating activation-button settings for non-keyboard workflows.");

        DrawIntStepperRow("Button %", () => AkronModule.Settings.FloatingButtonScale, value => AkronModule.Settings.FloatingButtonScale = AkronModuleSettings.ClampPercent(value, 50, 250), -5, 5, 50, 250, popupId, "Floating activation button scale.");
        DrawIntStepperRow("Button alpha", () => AkronModule.Settings.FloatingButtonOpacity, value => AkronModule.Settings.FloatingButtonOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Floating activation button opacity.");

        bool buttonLevels = AkronModule.Settings.FloatingButtonInLevels;
        if (ImGui.Checkbox("Button in levels##" + popupId, ref buttonLevels)) {
            AkronModule.Settings.FloatingButtonInLevels = buttonLevels;
        }
        DrawPopupTooltip("Allow the floating activation button in active levels.");

        bool buttonMenus = AkronModule.Settings.FloatingButtonInMenus;
        if (ImGui.Checkbox("Button in menus##" + popupId, ref buttonMenus)) {
            AkronModule.Settings.FloatingButtonInMenus = buttonMenus;
        }
        DrawPopupTooltip("Allow the floating activation button outside active levels.");

        bool autofocus = AkronModule.Settings.SearchAutofocus;
        if (ImGui.Checkbox("Search autofocus##" + popupId, ref autofocus)) {
            AkronModule.Settings.SearchAutofocus = autofocus;
        }
        DrawPopupTooltip("Focus Akron search automatically when the overlay opens.");
    }

    private void DrawThemeColorRow(string label, Func<int> getter, Action<int> setter, string popupId, string tooltip) {
        DrawHitboxColorRow(label, getter, value => {
            setter(AkronModuleSettings.ClampRgb(value));
            AkronModule.Settings.OverlayThemePreset = AkronOverlayThemePreset.Custom;
        }, popupId, tooltip);
    }

    private void DrawKeybindsPopupControls(string popupId) {
        bool inGameOnly = AkronModule.Settings.MenuBindingsInGameOnly;
        if (ImGui.Checkbox("In-game only##" + popupId, ref inGameOnly)) {
            AkronModule.Settings.MenuBindingsInGameOnly = inGameOnly;
        }
        DrawPopupTooltip("When enabled, per-action custom keybinds only execute inside an active level. The overlay toggle itself still works globally.");

        ImGui.TextUnformatted("Bound actions: " + (AkronModule.Settings.MenuActionBindings?.Count ?? 0));
        ImGui.TextUnformatted("Menu key: " + AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay));
    }

    private static AkronOverlayThemePreset NextOverlayThemePreset(AkronOverlayThemePreset preset) {
        return AkronOverlayThemes.NextPreset(preset);
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildThemeDropdownChoices() {
        return new List<SelectorDropdownChoice> {
            ThemeDropdownChoice(AkronOverlayThemePreset.Default),
            ThemeDropdownChoice(AkronOverlayThemePreset.Monochrome),
            ThemeDropdownChoice(AkronOverlayThemePreset.HighContrast),
            ThemeDropdownChoice(AkronOverlayThemePreset.Midnight),
            ThemeDropdownChoice(AkronOverlayThemePreset.Crimson),
            ThemeDropdownChoice(AkronOverlayThemePreset.Terminal),
            ThemeDropdownChoice(AkronOverlayThemePreset.Symbiote),
            ThemeDropdownChoice(AkronOverlayThemePreset.Carbon),
            ThemeDropdownChoice(AkronOverlayThemePreset.Retro),
            ThemeDropdownChoice(AkronOverlayThemePreset.Coniferous),
            ThemeDropdownChoice(AkronOverlayThemePreset.Wine),
            ThemeDropdownChoice(AkronOverlayThemePreset.Custom)
        };
    }

    private static SelectorDropdownChoice ThemeDropdownChoice(AkronOverlayThemePreset preset) {
        return new SelectorDropdownChoice(
            AkronOverlayThemes.DisplayName(preset),
            () => AkronModule.Settings.OverlayThemePreset == preset,
            () => AkronModule.Settings.OverlayThemePreset = preset);
    }
}
