using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawShowTapsPopupControls(string popupId) {
        if (ImGui.Button("Corner: " + AkronModule.Settings.TapDisplayCorner + "##" + popupId)) {
            AkronModule.Settings.TapDisplayCorner = NextIndicatorCorner(AkronModule.Settings.TapDisplayCorner);
        }
        DrawPopupTooltip("Choose the screen corner used by the input board.");

        if (ImGui.Button("Source: " + AkronModule.Settings.InputBoardSource + "##" + popupId)) {
            AkronModule.Settings.InputBoardSource = AkronModule.Settings.InputBoardSource == AkronInputBoardSource.GameActions
                ? AkronInputBoardSource.KeyboardKeys
                : AkronInputBoardSource.GameActions;
        }
        DrawPopupTooltip("Game Actions lights keys from Celeste actions. Keyboard Keys lights them from physical keyboard keys.");

        DrawIntStepperRow("Scale", () => AkronModule.Settings.TapDisplayScale, value => AkronModule.Settings.TapDisplayScale = AkronModuleSettings.ClampPercent(value, 50, 250), -5, 5, 50, 250, popupId, "Input-board scale percentage.");
        DrawIntStepperRow("Opacity", () => AkronModule.Settings.TapDisplayOpacity, value => AkronModule.Settings.TapDisplayOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Input-board opacity percentage.");

        if (ImGui.TreeNode("Presets##" + popupId)) {
            DrawInputBoardPresetButton("Compact keyboard", AkronInputBoard.BuildCompactElements, popupId, "Tight WASD-style block with actions attached.");
            DrawInputBoardPresetButton("Split clusters", AkronInputBoard.BuildSplitElements, popupId, "Separated menu/action and direction clusters with second jump and demo keys.");

            ImGui.Separator();
            if (ImGui.Button("Save Full .akr##" + popupId)) {
                AkronControlDisplayPresets.ExportCurrent();
            }
            DrawPopupTooltip("Save the complete Control Display setup, including placement, source, labels, keys, bindings, colors, and text scale.");

            if (ImGui.Button("Import Latest .akr##" + popupId)) {
                CaptureInputBoardUndoSnapshot();
                AkronControlDisplayPresets.ImportLatest();
                selectedInputBoardElementIndex = 0;
                inputBoardKeyBindingElementId = string.Empty;
            }
            DrawPopupTooltip("Import the newest full Control Display preset from Saves/AkronControlDisplay.");

            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Label presets##" + popupId)) {
            DrawInputBoardLabelPresetButton(AkronInputBoardLabelPreset.Names, popupId);
            DrawInputBoardLabelPresetButton(AkronInputBoardLabelPreset.Keyboard, popupId);
            DrawInputBoardLabelPresetButton(AkronInputBoardLabelPreset.Arrows, popupId);
            DrawInputBoardLabelPresetButton(AkronInputBoardLabelPreset.Short, popupId);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Edit layout##" + popupId)) {
            DrawInputBoardEditor(popupId);
            ImGui.TreePop();
        }
    }

    private void DrawInputBoardPresetButton(string label, Func<List<AkronInputBoardElement>> build, string popupId, string tooltip) {
        if (ImGui.Button(label + "##" + popupId)) {
            CaptureInputBoardUndoSnapshot();
            AkronModule.Settings.InputBoardElements = build();
            AkronInputBoard.ApplyLabelPreset(AkronModule.Settings.InputBoardElements, AkronModule.Settings.InputBoardLabelPreset);
            selectedInputBoardElementIndex = 0;
            inputBoardKeyBindingElementId = string.Empty;
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawInputBoardLabelPresetButton(AkronInputBoardLabelPreset preset, string popupId) {
        if (ImGui.Button(preset + "##" + popupId)) {
            CaptureInputBoardUndoSnapshot();
            AkronModule.Settings.InputBoardLabelPreset = preset;
            AkronInputBoard.ApplyLabelPreset(AkronModule.Settings.InputBoardElements, preset);
        }
        DrawPopupTooltip("Apply a label style to the current board without moving keys.");
    }

    private void DrawInputBoardEditor(string popupId) {
        List<AkronInputBoardElement> elements = AkronInputBoard.NormalizeElements(AkronModule.Settings.InputBoardElements);
        AkronModule.Settings.InputBoardElements = elements;
        selectedInputBoardElementIndex = Calc.Clamp(selectedInputBoardElementIndex, 0, Math.Max(0, elements.Count - 1));

        DrawInputBoardPreview(elements, popupId);

        if (ImGui.Button("Add key##" + popupId)) {
            CaptureInputBoardUndoSnapshot();
            elements.Add(AkronInputBoard.CreateCustomElement(elements.Count));
            selectedInputBoardElementIndex = elements.Count - 1;
            inputBoardKeyBindingElementId = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate##" + popupId)) {
            CaptureInputBoardUndoSnapshot();
            AkronInputBoardElement duplicate = elements[selectedInputBoardElementIndex].Clone();
            duplicate.Id = "custom-" + elements.Count.ToString(CultureInfo.InvariantCulture);
            duplicate.X += 12;
            duplicate.Y += 12;
            elements.Add(duplicate);
            selectedInputBoardElementIndex = elements.Count - 1;
            inputBoardKeyBindingElementId = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove##" + popupId) && elements.Count > 1) {
            CaptureInputBoardUndoSnapshot();
            elements.RemoveAt(selectedInputBoardElementIndex);
            selectedInputBoardElementIndex = Calc.Clamp(selectedInputBoardElementIndex, 0, elements.Count - 1);
            inputBoardKeyBindingElementId = string.Empty;
        }
        DrawPopupTooltip("Add or remove visible board keys.");

        if (ImGui.Button("Selected: " + DescribeSelectedInputBoardElement(elements, selectedInputBoardElementIndex) + "##" + popupId)) {
            selectedInputBoardElementIndex = (selectedInputBoardElementIndex + 1) % elements.Count;
            inputBoardKeyBindingElementId = string.Empty;
        }
        DrawPopupTooltip("Cycle the key currently being edited.");

        AkronInputBoardElement element = elements[selectedInputBoardElementIndex];
        string elementPopupId = popupId + "::element-" + selectedInputBoardElementIndex.ToString(CultureInfo.InvariantCulture) + "-" + (element.Id ?? string.Empty);
        string label = element.Label ?? string.Empty;
        if (DrawPopupInputText("Label", ref label, 24, elementPopupId, 154f)) {
            if (!string.Equals(element.Label, label, StringComparison.Ordinal)) {
                CaptureInputBoardUndoSnapshot();
            }
            element.Label = string.IsNullOrWhiteSpace(label) ? "Key" : label.Trim();
        }

        bool visible = element.Visible;
        if (ImGui.Checkbox("Visible##" + elementPopupId, ref visible)) {
            CaptureInputBoardUndoSnapshot();
            element.Visible = visible;
        }

        DrawInputBoardIntStepperRow("X", () => element.X, value => element.X = Calc.Clamp(value, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition), -2, 2, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition, elementPopupId, "Horizontal board position.");
        DrawInputBoardIntStepperRow("Y", () => element.Y, value => element.Y = Calc.Clamp(value, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition), -2, 2, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition, elementPopupId, "Vertical board position.");
        DrawInputBoardIntStepperRow("Width", () => element.Width, value => element.Width = Calc.Clamp(value, AkronInputBoard.MinimumElementSize, AkronInputBoard.MaximumElementSize), -2, 2, AkronInputBoard.MinimumElementSize, AkronInputBoard.MaximumElementSize, elementPopupId, "Key width.");
        DrawInputBoardIntStepperRow("Height", () => element.Height, value => element.Height = Calc.Clamp(value, AkronInputBoard.MinimumElementSize, AkronInputBoard.MaximumElementSize), -2, 2, AkronInputBoard.MinimumElementSize, AkronInputBoard.MaximumElementSize, elementPopupId, "Key height.");
        DrawInputBoardIntStepperRow("Text scale", () => element.TextScale, value => element.TextScale = Calc.Clamp(value, AkronInputBoard.MinimumTextScale, AkronInputBoard.MaximumTextScale), -5, 5, AkronInputBoard.MinimumTextScale, AkronInputBoard.MaximumTextScale, elementPopupId, "Text scale for this key.");

        DrawInputBoardBindingEditor(element, elementPopupId);

        if (ImGui.TreeNode("Colors##" + elementPopupId)) {
            DrawInputBoardColorRow("Fill", () => element.FillColor, value => element.FillColor = value, elementPopupId, "Key background color.");
            DrawInputBoardColorRow("Pressed", () => element.PressedFillColor, value => element.PressedFillColor = value, elementPopupId, "Key color while any bound input is held.");
            DrawInputBoardColorRow("Stroke", () => element.StrokeColor, value => element.StrokeColor = value, elementPopupId, "Key outline color.");
            DrawInputBoardColorRow("Text", () => element.TextColor, value => element.TextColor = value, elementPopupId, "Key text color.");
            DrawInputBoardIntStepperRow("Outline", () => element.OutlineWidth, value => element.OutlineWidth = Calc.Clamp(value, 0, 8), -1, 1, 0, 8, elementPopupId, "Outline width in pixels.");
            ImGui.TreePop();
        }

        HandleInputBoardEditorShortcuts(elements);
    }

    private void DrawInputBoardPreview(IReadOnlyList<AkronInputBoardElement> elements, string popupId) {
        if (elements == null || elements.Count == 0) {
            return;
        }

        List<(AkronInputBoardElement Element, int Index)> visibleElements = elements
            .Select((element, index) => (Element: element, Index: index))
            .Where(item => item.Element?.Visible == true)
            .ToList();
        if (visibleElements.Count == 0) {
            return;
        }

        int left = visibleElements.Min(item => item.Element.X);
        int top = visibleElements.Min(item => item.Element.Y);
        int right = visibleElements.Max(item => item.Element.X + item.Element.Width);
        int bottom = visibleElements.Max(item => item.Element.Y + item.Element.Height);
        float boardWidth = Math.Max(1f, right - left);
        float boardHeight = Math.Max(1f, bottom - top);
        float previewWidth = Math.Min(360f, Math.Max(240f, ImGui.GetContentRegionAvail().X));
        const float previewHeight = 180f;

        ImGui.InvisibleButton("Board preview##" + popupId, new NumericsVector2(previewWidth, previewHeight));
        NumericsVector2 previewMin = ImGui.GetItemRectMin();
        NumericsVector2 previewMax = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(previewMin, previewMax, AkronImGuiTheme.ToU32(ToImGuiColor(0x0B0B0B, 0.82f)));
        drawList.AddRect(previewMin, previewMax, AkronImGuiTheme.ToU32(ToImGuiColor(0x454545)));

        float scale = Math.Min((previewWidth - 16f) / boardWidth, (previewHeight - 16f) / boardHeight);
        scale = Math.Max(0.1f, scale);
        NumericsVector2 boardOrigin = new NumericsVector2(
            previewMin.X + (previewWidth - boardWidth * scale) * 0.5f,
            previewMin.Y + (previewHeight - boardHeight * scale) * 0.5f);

        NumericsVector2 mouse = ImGui.GetIO().MousePos;
        int hoveredIndex = -1;
        foreach ((AkronInputBoardElement element, int index) in visibleElements) {
            NumericsVector2 min = new NumericsVector2(boardOrigin.X + (element.X - left) * scale, boardOrigin.Y + (element.Y - top) * scale);
            NumericsVector2 max = new NumericsVector2(min.X + element.Width * scale, min.Y + element.Height * scale);
            if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y) {
                hoveredIndex = index;
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && hoveredIndex >= 0) {
            selectedInputBoardElementIndex = hoveredIndex;
            inputBoardKeyBindingElementId = string.Empty;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && selectedInputBoardElementIndex >= 0) {
            NumericsVector2 delta = ImGui.GetIO().MouseDelta;
            if (Math.Abs(delta.X) >= 0.01f || Math.Abs(delta.Y) >= 0.01f) {
                if (!inputBoardDragUndoCaptured) {
                    CaptureInputBoardUndoSnapshot();
                    inputBoardDragUndoCaptured = true;
                }
                AkronInputBoardElement selected = elements[Calc.Clamp(selectedInputBoardElementIndex, 0, elements.Count - 1)];
                selected.X = Calc.Clamp(selected.X + (int) Math.Round(delta.X / scale), AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition);
                selected.Y = Calc.Clamp(selected.Y + (int) Math.Round(delta.Y / scale), AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition);
            }
        } else {
            inputBoardDragUndoCaptured = false;
        }

        foreach ((AkronInputBoardElement element, int index) in visibleElements) {
            bool selected = index == selectedInputBoardElementIndex;
            NumericsVector2 min = new NumericsVector2(boardOrigin.X + (element.X - left) * scale, boardOrigin.Y + (element.Y - top) * scale);
            NumericsVector2 max = new NumericsVector2(min.X + element.Width * scale, min.Y + element.Height * scale);
            DrawInputBoardPreviewElement(drawList, element, min, max, selected);
        }
        DrawPopupTooltip("Click a key to select it. Drag or use arrow keys to reposition it. Ctrl+Z restores the previous board edit.");
    }

    private void DrawInputBoardPreviewElement(ImDrawListPtr drawList, AkronInputBoardElement element, NumericsVector2 min, NumericsVector2 max, bool selected) {
        bool pressed = AkronInputBoard.IsPressed(element, AkronModule.Settings.InputBoardSource);
        float width = Math.Max(1f, max.X - min.X);
        float height = Math.Max(1f, max.Y - min.Y);
        drawList.AddRectFilled(min, max, AkronImGuiTheme.ToU32(ToImGuiColor(pressed ? element.PressedFillColor : element.FillColor, pressed ? 0.86f : 0.62f)));

        for (int outline = 0; outline < element.OutlineWidth; outline++) {
            NumericsVector2 outlineMin = new NumericsVector2(min.X + outline, min.Y + outline);
            NumericsVector2 outlineMax = new NumericsVector2(max.X - outline, max.Y - outline);
            drawList.AddRect(outlineMin, outlineMax, AkronImGuiTheme.ToU32(ToImGuiColor(element.StrokeColor, pressed ? 1f : 0.58f)));
        }

        if (selected) {
            drawList.AddRect(new NumericsVector2(min.X - 1f, min.Y - 1f), new NumericsVector2(max.X + 1f, max.Y + 1f), AkronImGuiTheme.ToU32(ToImGuiColor(0xFFFFFF)));
        }

        string label = string.IsNullOrWhiteSpace(element.Label) ? "Key" : element.Label;
        float labelScale = Calc.Clamp(element.TextScale, AkronInputBoard.MinimumTextScale, AkronInputBoard.MaximumTextScale) / 100f;
        float textScale = Math.Max(0.18f, Math.Min(width, height) / 82f) * labelScale;
        float fontSize = Math.Max(6f, ImGui.GetFontSize() * textScale);
        NumericsVector2 baseTextSize = ImGui.CalcTextSize(label);
        NumericsVector2 textSize = baseTextSize * (fontSize / Math.Max(1f, ImGui.GetFontSize()));
        float maxTextWidth = Math.Max(1f, width - 6f);
        float maxTextHeight = Math.Max(1f, height - 4f);
        if ((textSize.X > maxTextWidth || textSize.Y > maxTextHeight) && textSize.X > 0f && textSize.Y > 0f) {
            fontSize *= Math.Max(0.2f, Math.Min(maxTextWidth / textSize.X, maxTextHeight / textSize.Y));
            textSize = baseTextSize * (fontSize / Math.Max(1f, ImGui.GetFontSize()));
        }

        drawList.AddText(
            ImGui.GetFont(),
            fontSize,
            new NumericsVector2(min.X + (width - textSize.X) * 0.5f, min.Y + (height - textSize.Y) * 0.5f - 1f),
            AkronImGuiTheme.ToU32(ToImGuiColor(element.TextColor)),
            label);
    }

    private void DrawInputBoardIntStepperRow(
        string label,
        Func<int> getter,
        Action<int> setter,
        int decrement,
        int increment,
        int minimum,
        int maximum,
        string popupId,
        string tooltip) {
        DrawIntStepperRow(label, getter, value => {
            if (getter() != value) {
                CaptureInputBoardUndoSnapshot();
            }

            setter(value);
        }, decrement, increment, minimum, maximum, popupId, tooltip);
    }

    private void DrawInputBoardColorRow(string label, Func<int> getter, Action<int> setter, string popupId, string tooltip) {
        DrawHitboxColorRow(label, getter, value => {
            if (getter() != value) {
                CaptureInputBoardUndoSnapshot();
            }

            setter(value);
        }, popupId, tooltip);
    }

    private void HandleInputBoardEditorShortcuts(IReadOnlyList<AkronInputBoardElement> elements) {
        bool itemEditingActive = ImGui.GetIO().WantTextInput || ImGui.IsAnyItemActive();
        if (!itemEditingActive && IsControlDown() && MInput.Keyboard.Pressed(Keys.Z)) {
            RestoreInputBoardUndoSnapshot();
            inputBoardKeyboardMoveUndoCaptured = false;
            return;
        }

        if (itemEditingActive || elements == null || elements.Count == 0) {
            inputBoardKeyboardMoveUndoCaptured = false;
            return;
        }

        KeyboardState keyboard = Keyboard.GetState();
        int dx = 0;
        int dy = 0;
        if (keyboard.IsKeyDown(Keys.Left)) {
            dx--;
        }

        if (keyboard.IsKeyDown(Keys.Right)) {
            dx++;
        }

        if (keyboard.IsKeyDown(Keys.Up)) {
            dy--;
        }

        if (keyboard.IsKeyDown(Keys.Down)) {
            dy++;
        }

        if (dx == 0 && dy == 0) {
            inputBoardKeyboardMoveUndoCaptured = false;
            return;
        }

        AkronInputBoardElement element = elements[Calc.Clamp(selectedInputBoardElementIndex, 0, elements.Count - 1)];
        int step = IsShiftDown() ? 10 : 1;
        int nextX = Calc.Clamp(element.X + dx * step, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition);
        int nextY = Calc.Clamp(element.Y + dy * step, AkronInputBoard.MinimumPosition, AkronInputBoard.MaximumPosition);
        if (nextX == element.X && nextY == element.Y) {
            return;
        }

        if (!inputBoardKeyboardMoveUndoCaptured) {
            CaptureInputBoardUndoSnapshot();
            inputBoardKeyboardMoveUndoCaptured = true;
        }

        element.X = nextX;
        element.Y = nextY;
        MarkValueEditFreeze();
    }

    private void CaptureInputBoardUndoSnapshot() {
        List<AkronInputBoardElement> snapshot = AkronInputBoard.CloneElements(AkronModule.Settings.InputBoardElements);
        if (inputBoardUndoStack.Count > 0 && InputBoardElementListsEqual(inputBoardUndoStack[inputBoardUndoStack.Count - 1], snapshot)) {
            return;
        }

        inputBoardUndoStack.Add(snapshot);
        if (inputBoardUndoStack.Count > InputBoardUndoLimit) {
            inputBoardUndoStack.RemoveAt(0);
        }
    }

    private void RestoreInputBoardUndoSnapshot() {
        if (inputBoardUndoStack.Count == 0) {
            return;
        }

        int lastIndex = inputBoardUndoStack.Count - 1;
        AkronModule.Settings.InputBoardElements = AkronInputBoard.CloneElements(inputBoardUndoStack[lastIndex]);
        inputBoardUndoStack.RemoveAt(lastIndex);
        selectedInputBoardElementIndex = Calc.Clamp(selectedInputBoardElementIndex, 0, Math.Max(0, AkronModule.Settings.InputBoardElements.Count - 1));
        inputBoardKeyBindingElementId = string.Empty;
        MarkValueEditFreeze();
    }

    private static bool InputBoardElementListsEqual(IReadOnlyList<AkronInputBoardElement> left, IReadOnlyList<AkronInputBoardElement> right) {
        if (left == null || right == null || left.Count != right.Count) {
            return false;
        }

        for (int index = 0; index < left.Count; index++) {
            if (!InputBoardElementsEqual(left[index], right[index])) {
                return false;
            }
        }

        return true;
    }

    private static bool InputBoardElementsEqual(AkronInputBoardElement left, AkronInputBoardElement right) {
        if (left == null || right == null) {
            return left == right;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.Label, right.Label, StringComparison.Ordinal) &&
               left.X == right.X &&
               left.Y == right.Y &&
               left.Width == right.Width &&
               left.Height == right.Height &&
               left.Visible == right.Visible &&
               left.FillColor == right.FillColor &&
               left.PressedFillColor == right.PressedFillColor &&
               left.StrokeColor == right.StrokeColor &&
               left.TextColor == right.TextColor &&
               left.OutlineWidth == right.OutlineWidth &&
               left.TextScale == right.TextScale &&
               (left.Bindings ?? new List<AkronInputBoardBinding>()).SequenceEqual(right.Bindings ?? new List<AkronInputBoardBinding>()) &&
               (left.KeyBindings ?? new List<Keys>()).SequenceEqual(right.KeyBindings ?? new List<Keys>());
    }

    private void DrawInputBoardBindingEditor(AkronInputBoardElement element, string popupId) {
        if (ImGui.Button("Binding: " + AkronInputBoard.FormatBindings(element) + "##" + popupId)) {
            CaptureInputBoardUndoSnapshot();
            CycleSelectedInputBoardBinding(element);
        }
        DrawPopupTooltip("Cycle the game action used by this key when Source is GameActions.");

        if (!string.Equals(inputBoardKeyBindingElementId, element.Id, StringComparison.Ordinal)) {
            inputBoardKeyBindingElementId = element.Id;
            inputBoardKeyBindingText = AkronInputBoard.FormatKeyBindings(element);
            if (string.Equals(inputBoardKeyBindingText, "Unbound", StringComparison.OrdinalIgnoreCase)) {
                inputBoardKeyBindingText = string.Empty;
            }
        }

        if (DrawPopupInputText("Keyboard keys", ref inputBoardKeyBindingText, 96, popupId, 180f) &&
            AkronInputBoard.TryParseKeyBindings(inputBoardKeyBindingText, out List<Keys> keys)) {
            if (!element.KeyBindings.SequenceEqual(keys)) {
                CaptureInputBoardUndoSnapshot();
            }
            element.KeyBindings = keys;
        }
        DrawPopupTooltip("Physical keys used by this board key when Source is KeyboardKeys. Examples: Space, C, LeftShift, D.");
    }

    private static string DescribeSelectedInputBoardElement(IReadOnlyList<AkronInputBoardElement> elements, int index) {
        if (elements == null || elements.Count == 0) {
            return "None";
        }

        AkronInputBoardElement element = elements[Calc.Clamp(index, 0, elements.Count - 1)];
        return string.IsNullOrWhiteSpace(element.Label) ? "Key" : element.Label;
    }

    private static void CycleSelectedInputBoardBinding(AkronInputBoardElement element) {
        Array values = Enum.GetValues(typeof(AkronInputBoardBinding));
        if (element.Bindings == null) {
            element.Bindings = new List<AkronInputBoardBinding>();
        }

        if (element.Bindings.Count == 0) {
            element.Bindings.Add((AkronInputBoardBinding) values.GetValue(0));
            return;
        }

        AkronInputBoardBinding current = element.Bindings[0];
        int index = Array.IndexOf(values, current);
        int next = index + 1;
        if (next >= values.Length) {
            element.Bindings.Clear();
            return;
        }

        element.Bindings[0] = (AkronInputBoardBinding) values.GetValue(next);
    }
}
