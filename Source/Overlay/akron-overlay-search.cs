using System;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private bool DrawImGuiSearchEntry(ActionEntry entry, string id, bool entryEnabled) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : searchInputActive ? AkronImGuiTheme.Accent : AkronImGuiTheme.Foreground;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float valueWidth = Math.Min(96f, Math.Max(82f, availableWidth * 0.44f));
        float labelWidth = Math.Max(40f, availableWidth - valueWidth - 4f);

        ImGui.SetNextItemWidth(valueWidth);
        bool valuePressed = DrawImGuiSearchTextInput(ImGuiActionSearchInputId, string.Empty, textColor);

        ImGui.SameLine(0f, 4f);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));
        bool labelPressed = ImGui.Button("##search_label" + id, new NumericsVector2(labelWidth, 0f));
        NumericsVector2 labelMin = ImGui.GetItemRectMin();
        NumericsVector2 labelMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(labelMin.X, labelMin.Y + 3f),
            AkronImGuiTheme.ToU32(textColor),
            TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, labelMax.X - labelMin.X - 4f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        if ((valuePressed || labelPressed) && entryEnabled) {
            searchInputActive = true;
            searchInputUsesImGui = false;
            RequestSearchInputFocus(ImGuiActionSearchInputId);
            selectedPanel = SelectionPanel.Actions;
            SearchInputConsumedThisFrame = true;
            SearchOwnsGameplayInputThisFrame = true;
        }

        return valuePressed || labelPressed;
    }

    private bool DrawImGuiSearchTextInput(string id, string placeholder, NumericsVector4 textColor) {
        bool shouldFocusThisInput = searchInputFocusRequested &&
                                    (string.IsNullOrEmpty(searchInputFocusTargetId) ||
                                     string.Equals(searchInputFocusTargetId, id, StringComparison.Ordinal));

        string value = searchQuery ?? string.Empty;
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, AkronImGuiTheme.FrameBackground);
        bool changed = ImGui.InputText(id, ref value, 96);
        bool active = ImGui.IsItemActive();
        bool clicked = ImGui.IsItemClicked();
        NumericsVector2 min = ImGui.GetItemRectMin();
        ImGui.PopStyleColor(4);

        if (shouldFocusThisInput) {
            // Match EclipseMenu's pattern: focus the input after drawing it
            // with -1 instead of focusing "next item" before the input. This
            // avoids repeatedly selecting the whole query while typing.
            if (!active) {
                ImGui.SetKeyboardFocusHere(-1);
            }
            ClearSearchInputFocusRequest();
            searchInputActive = true;
            searchInputUsesImGui = true;
            selectedPanel = SelectionPanel.Actions;
            SearchOwnsGameplayInputThisFrame = true;
        }

        if (clicked || active) {
            searchInputActive = active || clicked;
            searchInputUsesImGui = true;
            selectedPanel = SelectionPanel.Actions;
            SearchInputConsumedThisFrame = true;
            SearchOwnsGameplayInputThisFrame = true;
        }

        if (changed) {
            searchQuery = value ?? string.Empty;
            selectedActionIndex = 0;
            actionScrollIndex = 0;
            selectedPanel = SelectionPanel.Actions;
            searchInputActive = true;
            searchInputUsesImGui = true;
            // Filtering can add enough windows or rows to make ImGui drop the
            // active text item for a frame. Re-acquire the same input next
            // frame so backspacing through broader queries keeps editing.
            RequestSearchInputFocus(id);
            SearchInputConsumedThisFrame = true;
            SearchOwnsGameplayInputThisFrame = true;
        }

        if (!string.IsNullOrWhiteSpace(placeholder) && string.IsNullOrEmpty(searchQuery) && !active) {
            ImGui.GetWindowDrawList().AddText(
                new NumericsVector2(min.X + 6f, min.Y + 4f),
                AkronImGuiTheme.ToU32(AkronImGuiTheme.Muted),
                placeholder);
        }

        // Text edits are handled above. Returning true here means "activate
        // this row", and treating every edit as activation re-requested ImGui
        // keyboard focus on the next frame, which selected the whole search
        // value and made the next typed letter replace it.
        return clicked;
    }

    private void RequestSearchInputFocus(string id = "") {
        searchInputFocusRequested = true;
        searchInputFocusTargetId = id ?? string.Empty;
    }

    private void ClearSearchInputFocusRequest() {
        searchInputFocusRequested = false;
        searchInputFocusTargetId = string.Empty;
    }

    public void SetSearchQuery(string query) {
        searchQuery = query ?? string.Empty;
        searchInputActive = false;
        searchInputUsesImGui = false;
        ClearSearchInputFocusRequest();
        selectedActionIndex = 0;
        actionScrollIndex = 0;
        selectedPanel = SelectionPanel.Actions;
    }

    public void ClearSearchQuery() {
        searchQuery = string.Empty;
        searchInputActive = false;
        searchInputUsesImGui = false;
        ClearSearchInputFocusRequest();
        selectedActionIndex = 0;
        actionScrollIndex = 0;
        selectedPanel = SelectionPanel.Actions;
    }

    private void UpdateSearchQuery() {
        SearchInputConsumedThisFrame = false;
        KeyboardState keyboard = Keyboard.GetState();
        Keys[] pressedKeys = keyboard.GetPressedKeys()
            .Where(key => !previousSearchKeyboard.IsKeyDown(key))
            .ToArray();
        bool keyboardPressed = pressedKeys.Length > 0;
        // A focused search field owns keyboard input for the whole focus
        // lifetime, not just on text-change frames. Otherwise held movement
        // keys can leak back into Celeste between ImGui input events.
        SearchOwnsGameplayInputThisFrame = searchInputActive;
        if (searchInputActive && searchInputUsesImGui) {
            if (keyboardPressed) {
                SearchInputConsumedThisFrame = true;
                SearchOwnsGameplayInputThisFrame = true;
            }

            previousSearchKeyboard = keyboard;
            return;
        }

        if (!searchInputActive && AkronImGuiRenderer.WantCaptureKeyboard && keyboardPressed) {
            SearchInputConsumedThisFrame = true;
            SearchOwnsGameplayInputThisFrame = true;
            previousSearchKeyboard = keyboard;
            return;
        }

        if (!searchInputActive) {
            previousSearchKeyboard = keyboard;
            return;
        }

        if (pressedKeys.Contains(Keys.Back)) {
            if (searchQuery.Length > 0) {
                searchQuery = searchQuery[..^1];
            }

            selectedActionIndex = 0;
            actionScrollIndex = 0;
            selectedPanel = SelectionPanel.Actions;
            SearchInputConsumedThisFrame = true;
            SearchOwnsGameplayInputThisFrame = true;
        }

        foreach (Keys key in pressedKeys) {
            if (TryGetSearchCharacter(keyboard, key, out char character)) {
                searchQuery += character;
                selectedActionIndex = 0;
                actionScrollIndex = 0;
                selectedPanel = SelectionPanel.Actions;
                SearchInputConsumedThisFrame = true;
                SearchOwnsGameplayInputThisFrame = true;
            }
        }

        previousSearchKeyboard = keyboard;
    }

    private static bool IsAnyKeyboardPressed() {
        foreach (Keys key in Enum.GetValues(typeof(Keys))) {
            if (MInput.Keyboard.Pressed(key)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSearchCharacter(KeyboardState keyboard, Keys key, out char character) {
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (key >= Keys.A && key <= Keys.Z) {
            character = (char) ((shift ? 'A' : 'a') + (key - Keys.A));
            return true;
        }

        if (key >= Keys.D0 && key <= Keys.D9) {
            character = (char) ('0' + (key - Keys.D0));
            return true;
        }

        character = key switch {
            Keys.Space => ' ',
            Keys.OemMinus => '-',
            Keys.OemPlus => '+',
            Keys.OemComma => ',',
            Keys.OemPeriod => '.',
            Keys.OemQuestion => '/',
            Keys.OemOpenBrackets => '[',
            Keys.OemCloseBrackets => ']',
            Keys.OemPipe => '\\',
            Keys.OemSemicolon => ';',
            Keys.OemQuotes => '\'',
            _ => '\0'
        };
        return character != '\0';
    }
}
