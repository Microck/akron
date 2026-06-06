using System;
using System.Collections.Generic;
using Celeste;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private OptionEntryPress DrawImGuiNumericEntry(ActionEntry entry, string id, bool activeState, bool entryEnabled, bool hasOptionsPopup) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Foreground;
        NumericsVector4 indicatorColor = !entryEnabled ? AkronImGuiTheme.Muted : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float valueWidth = Math.Min(80f, Math.Max(58f, availableWidth * 0.36f));
        float arrowWidth = hasOptionsPopup ? availableWidth * 0.10f : 0f;
        float rowWidth = Math.Max(40f, availableWidth - arrowWidth);
        float labelWidth = Math.Max(32f, rowWidth - valueWidth - 4f);

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, AkronImGuiTheme.FrameBackground);
        ImGui.PushItemWidth(valueWidth);

        float value = entry.NumericValue?.Invoke() ?? 0f;
        bool valueEdited = ImGui.InputFloat(
            "##numeric_value" + id,
            ref value,
            0f,
            0f,
            BuildNumericInputFormat(entry));
        if (valueEdited && entryEnabled) {
            entry.SetNumericValue(entry.NumericInteger ? (float) Math.Round(value) : value);
            MarkValueEditFreeze();
        }

        bool valueActive = ImGui.IsItemActive();

        ImGui.PopItemWidth();
        ImGui.PopStyleColor(4);

        ImGui.SameLine(0f, 4f);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));
        bool labelPressed = ImGui.Button("##numeric_label" + id, new NumericsVector2(labelWidth, 0f));
        NumericsVector2 rowMin = ImGui.GetItemRectMin();
        NumericsVector2 rowMax = ImGui.GetItemRectMax();
        float scale = CurrentOverlayScale();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(rowMin.X, rowMin.Y + 3f * scale),
            AkronImGuiTheme.ToU32(textColor),
            TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, rowMax.X - rowMin.X - 4f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (hasOptionsPopup) {
            ImGui.SameLine(0f, 0f);
            ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
            bool arrowPressed = ImGui.Button("##open_options_num" + id, new NumericsVector2(arrowWidth, 0f));
            NumericsVector2 min = ImGui.GetItemRectMin();
            NumericsVector2 max = ImGui.GetItemRectMax();
            float top = min.Y + 4.5f * scale;
            float bottom = max.Y - 4.5f * scale;
            float right = max.X - 4.5f * scale;
            float side = bottom - top;
            float left = right - side;
            ImGui.GetWindowDrawList().AddTriangleFilled(
                new NumericsVector2(right, top),
                new NumericsVector2(left, bottom),
                new NumericsVector2(right, bottom),
                AkronImGuiTheme.ToU32(indicatorColor));
            ImGui.PopStyleColor(3);

            if (arrowPressed) {
                return OptionEntryPress.Arrow;
            }
        } else {
            ImGui.GetWindowDrawList().AddRectFilled(
                new NumericsVector2(rowMax.X - 5f * scale, rowMin.Y + 1f * scale),
                new NumericsVector2(rowMax.X - 2f * scale, rowMax.Y - 1f * scale),
                AkronImGuiTheme.ToU32(indicatorColor));
        }

        return valueEdited || valueActive ? OptionEntryPress.None : labelPressed ? OptionEntryPress.Label : OptionEntryPress.None;
    }

    private OptionEntryPress DrawImGuiSelectorEntry(ActionEntry entry, string id, bool activeState, bool entryEnabled, bool hasOptionsPopup) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Foreground;
        NumericsVector4 indicatorColor = !entryEnabled ? AkronImGuiTheme.Muted : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;
        IReadOnlyList<SelectorDropdownChoice> dropdownChoices = GetSelectorDropdownChoices(entry);
        bool hasDropdown = dropdownChoices.Count > 0;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        const float valueWidth = 82f;
        float arrowWidth = hasOptionsPopup ? availableWidth * 0.10f : 0f;
        float rowWidth = Math.Max(40f, availableWidth - arrowWidth);
        float labelWidth = Math.Max(32f, rowWidth - valueWidth - 4f);
        bool dropdownInteracted = false;
        float scale = CurrentOverlayScale();

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, AkronImGuiTheme.FrameBackground);
        ImGui.PushItemWidth(valueWidth);
        if (hasDropdown && entryEnabled) {
            string preview = TruncateImGuiTextToWidth(SafeDescribeEntryValue(entry), valueWidth - 22f);
            if (ImGui.BeginCombo("##selector_combo" + id, preview)) {
                dropdownInteracted = true;
                for (int index = 0; index < dropdownChoices.Count; index++) {
                    SelectorDropdownChoice choice = dropdownChoices[index];
                    bool enabled = choice.Enabled();
                    bool selected = choice.Selected();
                    if (!enabled) {
                        ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.DisabledText);
                    }

                    if (ImGui.Selectable(choice.Label + "##selector_choice" + id + "_" + index, selected) && enabled) {
                        choice.Apply();
                        searchInputActive = false;
                        searchInputUsesImGui = false;
                        ClearSearchInputFocusRequest();
                    }

                    if (selected) {
                        ImGui.SetItemDefaultFocus();
                    }

                    if (!enabled) {
                        ImGui.PopStyleColor();
                    }
                }

                ImGui.EndCombo();
            }

            dropdownInteracted = dropdownInteracted || ImGui.IsItemActive() || ImGui.IsItemClicked();
        } else {
            ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.FrameBackground);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.FrameBackground);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.FrameBackground);
            dropdownInteracted = ImGui.Button("##selector_value" + id, new NumericsVector2(valueWidth, 0f));
            NumericsVector2 valueMin = ImGui.GetItemRectMin();
            NumericsVector2 valueMax = ImGui.GetItemRectMax();
            string value = TruncateImGuiTextToWidth(SafeDescribeEntryValue(entry), Math.Max(16f, valueMax.X - valueMin.X - 8f));
            ImGui.GetWindowDrawList().AddText(
                new NumericsVector2(valueMin.X + 4f * scale, valueMin.Y + 3f * scale),
                AkronImGuiTheme.ToU32(textColor),
                value);
            ImGui.PopStyleColor(3);
        }

        ImGui.PopItemWidth();
        ImGui.PopStyleColor(4);
        ImGui.SameLine(0f, 4f);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        bool labelPressed = ImGui.Button("##selector_label" + id, new NumericsVector2(labelWidth, 0f));
        NumericsVector2 rowMin = ImGui.GetItemRectMin();
        NumericsVector2 rowMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(rowMin.X, rowMin.Y + 3f * scale),
            AkronImGuiTheme.ToU32(textColor),
            TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, rowMax.X - rowMin.X - 4f)));
        ImGui.PopStyleColor(3);

        if (hasOptionsPopup) {
            ImGui.SameLine(0f, 0f);
            ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
            bool arrowPressed = ImGui.Button("##open_options_selector" + id, new NumericsVector2(arrowWidth, 0f));
            NumericsVector2 min = ImGui.GetItemRectMin();
            NumericsVector2 max = ImGui.GetItemRectMax();
            float top = min.Y + 4.5f * scale;
            float bottom = max.Y - 4.5f * scale;
            float right = max.X - 4.5f * scale;
            float side = bottom - top;
            float left = right - side;
            ImGui.GetWindowDrawList().AddTriangleFilled(
                new NumericsVector2(right, top),
                new NumericsVector2(left, bottom),
                new NumericsVector2(right, bottom),
                AkronImGuiTheme.ToU32(indicatorColor));
            ImGui.PopStyleColor(3);

            if (arrowPressed) {
                return OptionEntryPress.Arrow;
            }
        }

        return dropdownInteracted ? OptionEntryPress.Dropdown : labelPressed ? OptionEntryPress.Label : OptionEntryPress.None;
    }

    private static IReadOnlyList<SelectorDropdownChoice> GetSelectorDropdownChoices(ActionEntry entry) {
        if (entry?.SelectorChoices == null) {
            return Array.Empty<SelectorDropdownChoice>();
        }

        return entry.SelectorChoices() ?? Array.Empty<SelectorDropdownChoice>();
    }
}
