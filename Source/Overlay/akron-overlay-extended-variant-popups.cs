using System;
using ImGuiNET;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawExtendedVariantsRandomizerPopupControls(string popupId) {
        if (!AkronExtendedVariants.Available) {
            ImGui.TextWrapped("External variant controls are not loaded.");
            return;
        }

        bool enabled = AkronExtendedVariants.RandomizerEnabled;
        if (ImGui.Checkbox("Change variants randomly##" + popupId, ref enabled)) {
            if (!enabled || AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                AkronExtendedVariants.MasterSwitch = true;
                AkronExtendedVariants.RandomizerEnabled = enabled;
                AkronExtendedVariants.RecordRandomizerCheatUseIfEnabled();
            }
        }
        DrawPopupTooltip("Use the external variant randomizer loop.");

        bool reroll = AkronExtendedVariants.RandomizerRerollMode;
        if (ImGui.Checkbox("Reroll mode##" + popupId, ref reroll)) {
            if (AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                AkronExtendedVariants.RandomizerRerollMode = reroll;
                AkronExtendedVariants.RecordRandomizerCheatUseIfEnabled();
            }
        }
        DrawPopupTooltip("Replace the randomized variant set instead of changing one option at a time.");

        bool display = AkronExtendedVariants.DisplayEnabledVariants;
        if (ImGui.Checkbox("Display enabled variants##" + popupId, ref display)) {
            AkronExtendedVariants.DisplayEnabledVariants = display;
        }
        DrawPopupTooltip("Show the active variant list on-screen.");

        DrawIntStepperRow(
            "Interval",
            () => AkronExtendedVariants.RandomizerInterval,
            value => {
                AkronExtendedVariants.RandomizerInterval = value;
                AkronExtendedVariants.RecordRandomizerCheatUseIfEnabled();
            },
            -5,
            5,
            0,
            3600,
            popupId,
            "Seconds between randomizer changes. Zero means screen-change based behavior.");

        DrawIntStepperRow(
            "Max",
            () => AkronExtendedVariants.RandomizerMaxEnabled,
            value => {
                AkronExtendedVariants.RandomizerMaxEnabled = value;
                AkronExtendedVariants.RecordRandomizerCheatUseIfEnabled();
            },
            -1,
            1,
            0,
            Math.Max(0, AkronExtendedVariants.OptionCount),
            popupId,
            "Maximum number of simultaneously randomized variants.");
    }

    private void DrawExtendedVariantPopupControls(string label, string popupId) {
        AkronExtendedVariantOption option = GetExtendedVariantOptionFromLabel(label);
        if (option == null) {
            ImGui.TextWrapped("External variant option is unavailable.");
            return;
        }

        ImGui.TextUnformatted(option.Label);
        ImGui.TextUnformatted("State: " + (option.IsDefault ? "Off" : "On"));
        ImGui.TextUnformatted("Configured: " + AkronExtendedVariants.FormatValue(AkronExtendedVariants.GetConfiguredOrCurrentValue(option)));
        ImGui.TextUnformatted("Current: " + AkronExtendedVariants.FormatValue(option.CurrentValue));
        ImGui.TextUnformatted("Default: " + AkronExtendedVariants.FormatValue(option.DefaultValue));

        if (option.CurrentValue is bool boolean) {
            bool value = boolean;
            if (ImGui.Checkbox("Value##" + popupId, ref value)) {
                SetExtendedVariantValue(option, value);
            }
            DrawPopupTooltip("Boolean external variant option.");
            return;
        }

        if (option.CurrentValue is int) {
            DrawIntStepperRow(
                "Value",
                () => Convert.ToInt32(AkronExtendedVariants.GetConfiguredOrCurrentValue(AkronExtendedVariants.GetOption(option.Name) ?? option)),
                value => SetExtendedVariantConfiguredValue(option, value),
                -1,
                1,
                -100000,
                100000,
                popupId,
                "Integer external variant value. The variant provider validates the gameplay meaning.");
            DrawExtendedVariantResetButton(option, popupId);
            return;
        }

        if (option.CurrentValue is float) {
            DrawFloatValueRow(
                "Value",
                () => Convert.ToSingle(AkronExtendedVariants.GetConfiguredOrCurrentValue(AkronExtendedVariants.GetOption(option.Name) ?? option)),
                value => SetExtendedVariantConfiguredValue(option, value),
                -0.1f,
                0.1f,
                -100000f,
                100000f,
                "%.2f",
                popupId,
                "Decimal external variant value. Use documented ranges for sane results.");
            DrawExtendedVariantResetButton(option, popupId);
            return;
        }

        Type currentType = option.CurrentValue?.GetType();
        if (currentType?.IsEnum == true) {
            string[] names = Enum.GetNames(currentType);
            int index = Array.IndexOf(names, option.CurrentValue.ToString());
            if (index < 0) {
                index = 0;
            }

            const float comboWidth = 160f;
            DrawPopupRowLabel("Value", CalculatePopupLabelWidth(comboWidth));
            ImGui.PushItemWidth(comboWidth);
            if (ImGui.BeginCombo("##Value" + popupId, names[index])) {
                for (int optionIndex = 0; optionIndex < names.Length; optionIndex++) {
                    bool selected = optionIndex == index;
                    if (ImGui.Selectable(names[optionIndex] + "##Value" + popupId + optionIndex, selected)) {
                        index = optionIndex;
                        SetExtendedVariantConfiguredValue(option, Enum.Parse(currentType, names[index]));
                    }

                    if (selected) {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            DrawPopupTooltip("Enum external variant value.");
            DrawExtendedVariantResetButton(option, popupId);
            return;
        }

        if (option.CurrentValue is string currentString) {
            string value = currentString;
            if (DrawPopupInputText("Value", ref value, 96, popupId, 160f)) {
                SetExtendedVariantConfiguredValue(option, value);
                MarkValueEditFreeze();
            }
            if (ImGui.IsItemActive()) {
                MarkValueEditFreeze();
            }
            DrawPopupTooltip("String external variant value. For color grading, use a known color-grade ID.");
            DrawExtendedVariantResetButton(option, popupId);
            return;
        }

        if (option.CurrentValue is bool[][]) {
            if (ImGui.Button("All##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "all");
            }
            ImGui.SameLine();
            if (ImGui.Button("Cardinal##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "cardinal");
            }
            ImGui.SameLine();
            if (ImGui.Button("Diagonal##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "diagonal");
            }
            if (ImGui.Button("None##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "none");
            }
            ImGui.SameLine();
            if (ImGui.Button("Horizontal##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "horizontal");
            }
            ImGui.SameLine();
            if (ImGui.Button("Vertical##" + popupId)) {
                SetExtendedVariantConfiguredFromText(option, "vertical");
            }
            DrawPopupTooltip("Dash direction presets for the direction matrix option.");
            DrawExtendedVariantResetButton(option, popupId);
            return;
        }

        ImGui.TextWrapped("Akron can display this value, but it cannot edit type " + option.TypeName + " safely yet.");
        DrawExtendedVariantResetButton(option, popupId);
    }

    private void DrawExtendedVariantResetButton(AkronExtendedVariantOption option, string popupId) {
        if (ImGui.Button("Reset##" + option.Name + popupId)) {
            ResetExtendedVariantConfiguredValue(option);
        }
        DrawPopupTooltip("Clear this configured value and restore the external variant default.");
    }
}
