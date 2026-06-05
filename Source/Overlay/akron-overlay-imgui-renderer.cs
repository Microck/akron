using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Celeste;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawImGuiMenu() {
        ImGui.GetIO().FontGlobalScale = AkronModuleSettings.ClampOverlayScale(AkronModule.Settings.OverlayScale) / 100f;
        ApplyOverlayThemePreset();
        imguiOptionsPopupAnchorRects.Clear();
        pendingImGuiOptionsPopupEntry = null;
        pendingImGuiTooltipEntry = null;
        pendingImGuiTooltipAnchor = Rectangle.Empty;
        pendingImGuiTextTooltip = string.Empty;
        pendingImGuiTextTooltipAnchor = Rectangle.Empty;
        suppressImGuiRowPressesThisFrame = imguiPopupBlockedRowsLastFrame;
        imguiPopupBlockedRowsLastFrame = false;
        openedImGuiOptionsPopupThisFrame = false;

        Level level = Scene as Level;
        const float menuLeft = 4f;
        const float menuTop = 4f;
        const float menuColumnWidth = 208f;
        const float menuColumnGap = PanelGap;

        List<float> columnXPositions = CalculateActionColumnXPositions(menuLeft, menuColumnWidth, menuColumnGap, ImGui.GetIO().DisplaySize.X);
        if (columnXPositions.Count == 0) {
            DrawImGuiBindingCapturePopup();
            DrawInternalRecorderExperimentalWarningPopup();
            return;
        }

        string[] visibleTabs = GetVisibleTabs();
        ExternalToolPlacementPlan externalPlacementPlan = BuildExternalToolPlacementPlan(visibleTabs, level, columnXPositions.Count, menuTop, ImGui.GetIO().DisplaySize.Y);
        List<float> columnBottoms = columnXPositions.Select(_ => menuTop).ToList();
        List<int> columnSectionCounts = columnXPositions.Select(_ => 0).ToList();
        for (int tabIndex = 0; tabIndex < visibleTabs.Length; tabIndex++) {
            string tabName = visibleTabs[tabIndex];
            List<ActionEntry> entries = GetFilteredDisplayActionEntries(tabName, level);
            if (!string.IsNullOrWhiteSpace(searchQuery) && entries.Count == 0) {
                continue;
            }

            int columnIndex = GetPlannedActionColumnIndex(tabName, columnXPositions.Count, columnBottoms, columnSectionCounts, externalPlacementPlan, ImGui.GetIO().DisplaySize.Y);
            float x = columnXPositions[columnIndex];
            float y = columnBottoms[columnIndex];
            float sectionScreenHeight = GetSectionScreenHeight(tabName, columnIndex, externalPlacementPlan, ImGui.GetIO().DisplaySize.Y);
            float height = CalculateActionSectionHeight(entries.Count, y, sectionScreenHeight);
            DrawActionWindow(tabName, x, y, menuColumnWidth, entries, tabIndex);
            columnBottoms[columnIndex] = y + GetStackedActionSectionHeight(tabName, height) + ColumnStackGap;
            columnSectionCounts[columnIndex]++;
        }

        DrawCommunityPackBrowserWindow();
        DrawPendingImGuiOptionsPopup();
        DrawPendingImGuiActionTooltip();
        DrawPendingImGuiItemTooltip();
        DrawImGuiBindingCapturePopup();
        DrawInternalRecorderExperimentalWarningPopup();
    }

    private void DrawStartPosPlacementEditor() {
        ImGui.GetIO().FontGlobalScale = AkronModuleSettings.ClampOverlayScale(AkronModule.Settings.OverlayScale) / 100f;
        ApplyOverlayThemePreset();
        pendingImGuiTextTooltip = string.Empty;
        pendingImGuiTextTooltipAnchor = Rectangle.Empty;

        ImGui.SetNextWindowPos(new NumericsVector2(AkronModule.Settings.StartPosPlacementPanelX, AkronModule.Settings.StartPosPlacementPanelY), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(330f, 0f), ImGuiCond.Always);
        ImGui.Begin(
            "Place StartPos",
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize);

        NumericsVector2 panelPos = ImGui.GetWindowPos();
        AkronModule.Settings.StartPosPlacementPanelX = Calc.Clamp((int) Math.Round(panelPos.X), 0, 1920);
        AkronModule.Settings.StartPosPlacementPanelY = Calc.Clamp((int) Math.Round(panelPos.Y), 0, 1080);
        startPosPlacementPanelMin = panelPos;
        startPosPlacementPanelMax = new NumericsVector2(panelPos.X + ImGui.GetWindowWidth(), panelPos.Y + ImGui.GetWindowHeight());

        bool minimized = AkronModule.Settings.StartPosPlacementPanelMinimized;
        if (ImGui.Button(minimized ? "Expand##startpos_placement_min" : "Minimize##startpos_placement_min", new NumericsVector2(92f, 0f))) {
            AkronModule.Settings.StartPosPlacementPanelMinimized = !minimized;
            minimized = !minimized;
        }
        ImGui.SameLine();
        if (ImGui.Button("Done##startpos_placement_done", new NumericsVector2(92f, 0f))) {
            EndStartPosPlacement(true);
        }

        if (!minimized) {
            ImGui.Separator();
            ImGui.TextUnformatted("Click the shadow to place the active StartPos.");
            ImGui.TextUnformatted("Placed shadows stay visible and less opaque.");
            DrawIntStepperRow(
                "Slot",
                () => AkronModule.Settings.ActiveStartPosSlot,
                AkronActions.SetStartPosSlot,
                -1,
                1,
                1,
                9999,
                "placement_editor",
                "Slot for the active placed StartPos.");
            DrawPlaceStartPosPopupControls("placement_editor", includePlacementToggle: false);
        }
        ImGui.End();
        DrawPendingImGuiItemTooltip();
    }

    private void DrawInfoWindow(string title, float x, float y, float width, List<RowSpec> rows) {
        if (!BeginFixedWindow(title, x, y, width, CalculateInfoSectionHeight(rows.Count))) {
            ImGui.End();
            return;
        }

        foreach (RowSpec row in rows) {
            if (row.Kind == RowKind.Search) {
                DrawImGuiSearchRow(row);
            } else if (row.Kind == RowKind.MenuBinding) {
                DrawImGuiMenuBindingRow(row);
            } else {
                DrawImGuiInfoRow(row);
            }
        }

        ImGui.End();
    }

    private void DrawActionWindow(string title, float x, float y, float width, List<ActionEntry> entries, int tabIndex) {
        long start = Stopwatch.GetTimestamp();
        float displayHeight = ImGui.GetIO().DisplaySize.Y;
        bool needsScrolling = entries.Count > CalculateVisibleActionRows(y, displayHeight);
        if (!BeginFixedWindow(title, x, y, width, CalculateActionSectionHeight(entries.Count, y, displayHeight), needsScrolling)) {
            ImGui.End();
            AkronPerformanceTelemetry.RecordOverlayWindowCost(title, ElapsedMilliseconds(start, Stopwatch.GetTimestamp()));
            return;
        }

        DrawImGuiActionRows(entries, tabIndex, needsScrolling);

        ImGui.End();
        AkronPerformanceTelemetry.RecordOverlayWindowCost(title, ElapsedMilliseconds(start, Stopwatch.GetTimestamp()));
    }

    private static double ElapsedMilliseconds(long start, long end) {
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }

    private void DrawImGuiActionRows(List<ActionEntry> entries, int tabIndex, bool clipped) {
        if (!clipped || entries.Count == 0) {
            for (int index = 0; index < entries.Count; index++) {
                DrawImGuiActionEntry(entries[index], index, tabIndex);
            }

            return;
        }

        float itemHeight = Math.Max(1f, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y);
        float scrollY = Math.Max(0f, ImGui.GetScrollY());
        float visibleHeight = Math.Max(itemHeight, ImGui.GetWindowHeight());
        int first = Calc.Clamp((int) Math.Floor(scrollY / itemHeight), 0, entries.Count);
        int last = Calc.Clamp((int) Math.Ceiling((scrollY + visibleHeight) / itemHeight) + 1, first, entries.Count);

        if (first > 0) {
            ImGui.Dummy(new NumericsVector2(1f, first * itemHeight));
        }

        for (int index = first; index < last; index++) {
            DrawImGuiActionEntry(entries[index], index, tabIndex);
        }

        int remaining = entries.Count - last;
        if (remaining > 0) {
            ImGui.Dummy(new NumericsVector2(1f, remaining * itemHeight));
        }
    }

    private bool BeginFixedWindow(string title, float x, float y, float width, float height, bool allowScrollbar = false) {
        float scale = AkronModuleSettings.ClampOverlayScale(AkronModule.Settings.OverlayScale) / 100f;
        ImGui.SetNextWindowPos(new NumericsVector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new NumericsVector2(width * scale, height * Math.Max(1f, scale)), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity) / 100f);
        if (pendingImGuiCollapseSync.Remove(title)) {
            ImGui.SetNextWindowCollapsed(collapsedWindowTitles.Contains(title), ImGuiCond.Always);
        }

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings;
        if (!allowScrollbar) {
            // MegaHack-style panels should only scroll when their rows overflow the available screen height.
            flags |= ImGuiWindowFlags.NoScrollbar;
        }

        bool visible = ImGui.Begin(
            title + "##akron_window_" + title,
            flags);

        if (ImGui.IsWindowCollapsed()) {
            collapsedWindowTitles.Add(title);
        } else {
            collapsedWindowTitles.Remove(title);
        }

        return visible;
    }

    private static void ApplyOverlayThemePreset() {
        ImGuiStylePtr style = ImGui.GetStyle();
        AkronOverlayThemeDefinition theme = AkronOverlayThemes.CurrentDefinition();
        style.Colors[(int) ImGuiCol.WindowBg] = ToImGuiColor(theme.WindowColor);
        style.Colors[(int) ImGuiCol.TitleBg] = ToImGuiColor(theme.HeaderColor);
        style.Colors[(int) ImGuiCol.TitleBgActive] = ToImGuiColor(theme.HeaderHoverColor);
        style.Colors[(int) ImGuiCol.TitleBgCollapsed] = ToImGuiColor(theme.HeaderColor);
        style.Colors[(int) ImGuiCol.Text] = ToImGuiColor(theme.TextColor);
        style.Colors[(int) ImGuiCol.FrameBg] = AkronImGuiTheme.FrameBackground;
        style.Colors[(int) ImGuiCol.FrameBgHovered] = AkronImGuiTheme.ButtonHovered;
        style.Colors[(int) ImGuiCol.FrameBgActive] = AkronImGuiTheme.ButtonActive;
        style.Colors[(int) ImGuiCol.Button] = AkronImGuiTheme.FrameBackground;
        style.Colors[(int) ImGuiCol.ButtonHovered] = ToImGuiColor(theme.HeaderHoverColor, 0.32f);
        style.Colors[(int) ImGuiCol.ButtonActive] = ToImGuiColor(theme.HeaderHoverColor, 0.48f);
        style.Colors[(int) ImGuiCol.PopupBg] = ToImGuiColor(theme.WindowColor);
    }

    private static NumericsVector4 ToImGuiColor(int rgb, float alpha = 1f) {
        int clamped = AkronModuleSettings.ClampRgb(rgb);
        return new NumericsVector4(
            ((clamped >> 16) & 0xFF) / 255f,
            ((clamped >> 8) & 0xFF) / 255f,
            (clamped & 0xFF) / 255f,
            Calc.Clamp(alpha, 0f, 1f));
    }

    private static Color ColorFromRgb(int rgb) {
        int clamped = AkronModuleSettings.ClampRgb(rgb);
        return new Color((clamped >> 16) & 0xFF, (clamped >> 8) & 0xFF, clamped & 0xFF);
    }

    private void DrawImGuiInfoRow(RowSpec row) {
        string label = row.Label;
        string value = row.Value();
        float labelWidth = 94f;
        NumericsVector4 valueColor = row.ValueColorRgb?.Invoke() is int rgb
            ? ToImGuiColor(rgb)
            : AkronImGuiTheme.Foreground;

        ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.Muted);
        ImGui.TextUnformatted(label.Length > 14 ? label[..14] : label);
        ImGui.PopStyleColor();
        ImGui.SameLine(labelWidth);
        ImGui.PushStyleColor(ImGuiCol.Text, valueColor);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }

    private void DrawImGuiMenuBindingRow(RowSpec row) {
        string value = row.Value();
        string popupId = "akron_overlay_toggle_binding_context";

        ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.Foreground);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));

        bool pressed = ImGui.Button(row.Label + ": " + value + "##akron_menu_bind", new NumericsVector2(ImGui.GetContentRegionAvail().X, 0f));
        bool hovered = ImGui.IsItemHovered();
        bool shiftLeftClick = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && IsShiftDown();
        if (pressed || (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) || shiftLeftClick) {
            if (pressed && !shiftLeftClick) {
                StartOverlayToggleBindingCapture();
            } else {
                ImGui.OpenPopup(popupId);
            }
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (!ImGui.BeginPopup(popupId)) {
            if (hovered && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal)) {
                DrawImGuiItemTooltip("Click to rebind the Akron menu input.\nRight-click or Shift-click for binding options.");
            }
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.TextUnformatted("Open Overlay");
        ImGui.Separator();
        ImGui.TextUnformatted("Current: " + AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay));
        if (ImGui.MenuItem("Bind input")) {
            StartOverlayToggleBindingCapture();
        }
        if (ImGui.MenuItem("Reset to Tab")) {
            ResetOverlayToggleBinding();
        }
        ImGui.EndPopup();
    }

    private void DrawImGuiActionEntry(ActionEntry entry, int index, int tabIndex) {
        bool selected = selectedPanel == SelectionPanel.Actions &&
                        selectedActionIndex == index &&
                        selectedTabIndex == tabIndex;
        bool entryEnabled = entry.Enabled();
        bool hasOptionsPopup = entry.HasOptionsPopup;
        bool activeState = ShouldReadEntryValueForActiveState(entry) && IsOnState(entry.Value());
        bool searchMatch = !string.IsNullOrWhiteSpace(searchQuery) && entry.Control != OverlayEntryControl.SearchInput && MatchesSearch(entry.Tab, entry);
        activeState = activeState || searchMatch;
        string id = "##akron_" + entry.Tab + "_" + entry.ActionKey + "_" + index;
        OptionEntryPress optionPress = OptionEntryPress.None;

        ImGui.BeginGroup();
        bool pressed;
        if (entry.Control == OverlayEntryControl.GroupHeader) {
            pressed = DrawImGuiGroupHeaderEntry(entry, id, entryEnabled);
        } else if (entry.Control == OverlayEntryControl.SearchInput) {
            pressed = DrawImGuiSearchEntry(entry, id, entryEnabled);
        } else if (entry.Control == OverlayEntryControl.Keybind || entry.Control == OverlayEntryControl.KeybindReadOnly) {
            pressed = DrawImGuiKeybindEntry(entry, id, entryEnabled, entry.Control == OverlayEntryControl.KeybindReadOnly);
        } else if (entry.Control == OverlayEntryControl.StartPosActions) {
            optionPress = DrawImGuiStartPosEntry(entry, id, activeState, entryEnabled);
            pressed = optionPress != OptionEntryPress.None;
        } else if (entry.Control == OverlayEntryControl.NumericInput) {
            optionPress = DrawImGuiNumericEntry(entry, id, activeState, entryEnabled, hasOptionsPopup);
            pressed = optionPress != OptionEntryPress.None;
        } else if (entry.Control == OverlayEntryControl.Selector) {
            optionPress = DrawImGuiSelectorEntry(entry, id, activeState, entryEnabled, hasOptionsPopup);
            pressed = optionPress != OptionEntryPress.None;
        } else if (hasOptionsPopup) {
            optionPress = DrawImGuiOptionEntry(entry, id, activeState, entryEnabled);
            pressed = optionPress != OptionEntryPress.None;
        } else {
            pressed = DrawImGuiPlainEntry(entry, id, activeState, entryEnabled);
        }
        ImGui.EndGroup();

        NumericsVector2 rowMin = ImGui.GetItemRectMin();
        NumericsVector2 rowMax = ImGui.GetItemRectMax();
        bool rowHovered = ImGui.IsMouseHoveringRect(rowMin, rowMax);
        bool rowActive = ImGui.IsItemActive();
        bool bindingContextRequested = entry.Control != OverlayEntryControl.KeybindReadOnly &&
                                       entry.Control != OverlayEntryControl.GroupHeader &&
                                       rowHovered &&
                                       (ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
                                        (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && IsShiftDown()));
        if (rowHovered) {
            selectedPanel = SelectionPanel.Actions;
            selectedActionIndex = index;
            if (tabIndex >= 0) {
                selectedTabIndex = tabIndex;
            }
        }

        DrawLabelRowReorderTarget(entry, rowHovered, rowActive, rowMin, rowMax);

        if ((pressed || bindingContextRequested) && searchInputActive && entry.Control != OverlayEntryControl.SearchInput) {
            searchInputActive = false;
            searchInputUsesImGui = false;
            ClearSearchInputFocusRequest();
        }

        if (pressed && entryEnabled && !labelRowDragActive && !suppressImGuiRowPressesThisFrame && !bindingContextRequested && entry.Control != OverlayEntryControl.SearchInput) {
            selectedPanel = SelectionPanel.Actions;
            selectedActionIndex = index;
            if (tabIndex >= 0) {
                selectedTabIndex = tabIndex;
            }

            if (optionPress == OptionEntryPress.Dropdown) {
                // The selector's value box owns the dropdown interaction. It has
                // already opened and rendered its popup in the row renderer.
            } else if (entry.IsAddCustomLabelRow) {
                AkronCustomHudLabel label = AkronCustomHudLabels.AddCustom();
                AkronModule.Settings.LabelRowOrder.Add(AkronModuleSettings.BuildCustomLabelRowKey(label.Id));
                AkronModule.Settings.LabelRowOrder = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
                InvalidateDisplayActionEntryCache();
            } else if (hasOptionsPopup && (optionPress == OptionEntryPress.Arrow || IsSettingsOnlyOptionsRow(entry.Label))) {
                SelectCustomHudLabel(entry.CustomHudLabelId);
                OpenOptionsPopup(entry.OptionsPopupKey);
                openedImGuiOptionsPopupThisFrame = true;
            } else if (entry.Control == OverlayEntryControl.Keybind) {
                StartBindingCaptureForEntry(entry);
            } else if (ShouldWarnBeforeInternalRecorderAction(entry)) {
                pendingInternalRecorderExperimentalAction = entry;
                internalRecorderExperimentalWarningDontShowAgain = false;
                ImGui.OpenPopup(GetInternalRecorderExperimentalWarningPopupId());
            } else {
                ExecuteActionEntry(entry, "imgui");
            }
        }

        DrawImGuiBindingContext(entry, bindingContextRequested);
        DrawImGuiActionTooltip(entry, rowHovered);
        if (hasOptionsPopup) {
            RecordImGuiOptionsPopupAnchor(entry, rowMin, rowMax);
            if (IsOptionsPopupOpen(entry.OptionsPopupKey)) {
                pendingImGuiOptionsPopupEntry = entry;
            }
        }
    }

    private void DrawPendingImGuiOptionsPopup() {
        if (pendingImGuiOptionsPopupEntry != null) {
            DrawImGuiOptionsPopup(pendingImGuiOptionsPopupEntry);
        }
    }

    private void RecordImGuiOptionsPopupAnchor(ActionEntry entry, NumericsVector2 rowMin, NumericsVector2 rowMax) {
        if (entry == null || string.IsNullOrWhiteSpace(entry.OptionsPopupKey)) {
            return;
        }

        imguiOptionsPopupAnchorRects[entry.OptionsPopupKey] = RectCeiling(
            rowMin.X,
            rowMin.Y,
            Math.Max(1f, rowMax.X - rowMin.X),
            Math.Max(1f, rowMax.Y - rowMin.Y));
    }

    private void DrawLabelRowReorderTarget(ActionEntry entry, bool rowHovered, bool rowActive, NumericsVector2 rowMin, NumericsVector2 rowMax) {
        if (!entry.Reorderable || !string.Equals(entry.Tab, "Labels", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        if (rowHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            draggingLabelRowKey = entry.RowOrderKey;
            labelRowDragStart = ImGui.GetMousePos();
            labelRowDragActive = false;
        }

        if (rowActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f)) {
            if (string.IsNullOrWhiteSpace(draggingLabelRowKey)) {
                draggingLabelRowKey = entry.RowOrderKey;
                labelRowDragStart = rowMin;
            }

            labelRowDragActive = true;
        }

        bool draggingThisEntry = string.Equals(draggingLabelRowKey, entry.RowOrderKey, StringComparison.OrdinalIgnoreCase);
        float dragDistanceSquared = NumericsVector2.DistanceSquared(ImGui.GetMousePos(), labelRowDragStart);
        if (draggingThisEntry &&
            (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f) || dragDistanceSquared > 16f)) {
            labelRowDragActive = true;
        }

        bool mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool mouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool dragReady = !string.IsNullOrWhiteSpace(draggingLabelRowKey) &&
                         (labelRowDragActive || dragDistanceSquared > 16f);
        if (rowHovered &&
            dragReady &&
            !string.Equals(draggingLabelRowKey, entry.RowOrderKey, StringComparison.OrdinalIgnoreCase)) {
            ImGui.GetWindowDrawList().AddRect(
                rowMin,
                rowMax,
                AkronImGuiTheme.ToU32(AkronImGuiTheme.Accent));
            if (mouseDown || mouseReleased) {
                bool afterTarget = ImGui.GetMousePos().Y > (rowMin.Y + rowMax.Y) * 0.5f;
                MoveLabelRow(draggingLabelRowKey, entry.RowOrderKey, afterTarget);
                labelRowDragActive = true;
            }
        }

        if (mouseReleased || !mouseDown && !string.IsNullOrWhiteSpace(draggingLabelRowKey)) {
            draggingLabelRowKey = string.Empty;
            labelRowDragActive = false;
        }
    }

    private void MoveLabelRow(string draggedKey, string targetKey, bool afterTarget) {
        if (string.IsNullOrWhiteSpace(draggedKey) ||
            string.IsNullOrWhiteSpace(targetKey) ||
            string.Equals(draggedKey, targetKey, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        List<string> order = AkronModuleSettings.NormalizeLabelRowOrder(AkronModule.Settings.LabelRowOrder, AkronModule.Settings.CustomHudLabelDefinitions);
        int from = order.FindIndex(key => string.Equals(key, draggedKey, StringComparison.OrdinalIgnoreCase));
        int to = order.FindIndex(key => string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || to < 0 || from == to) {
            return;
        }

        string key = order[from];
        order.RemoveAt(from);
        if (from < to) {
            to--;
        }

        order.Insert(afterTarget ? Math.Min(order.Count, to + 1) : to, key);
        AkronModule.Settings.LabelRowOrder = order;
        InvalidateDisplayActionEntryCache();
    }

    private void SelectCustomHudLabel(string id) {
        if (string.IsNullOrWhiteSpace(id) || AkronModule.Settings.CustomHudLabelDefinitions == null) {
            return;
        }

        int index = AkronModule.Settings.CustomHudLabelDefinitions.FindIndex(label => string.Equals(label.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) {
            AkronModule.Settings.CustomHudLabelIndex = index;
        }
    }

    private void InvalidateDisplayActionEntryCache() {
        displayActionEntryCache.Clear();
        InvalidateBindableActionCache();
    }

    private static void InvalidateBindableActionCache() {
        bindableActionCache.Clear();
        bindableActionCacheLevel = null;
        bindableActionCacheRevision = -1;
    }

    private bool DrawImGuiKeybindEntry(ActionEntry entry, string id, bool entryEnabled, bool readOnly) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : AkronImGuiTheme.Foreground;
        NumericsVector4 boxColor = AkronImGuiTheme.FrameBackground;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float valueWidth = Math.Min(82f, Math.Max(54f, availableWidth * 0.42f));
        float clearWidth = readOnly ? 0f : 18f;
        float labelWidth = Math.Max(40f, availableWidth - valueWidth - clearWidth - 4f);

        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        bool labelPressed = ImGui.Button("##keybind_label" + id, new NumericsVector2(labelWidth, 0f));
        NumericsVector2 labelMin = ImGui.GetItemRectMin();
        NumericsVector2 labelMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(labelMin.X, labelMin.Y + 3f),
            AkronImGuiTheme.ToU32(textColor),
            TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, labelMax.X - labelMin.X - 4f)));

        ImGui.SameLine(0f, 2f);
        bool valuePressed = ImGui.Button("##keybind_value" + id, new NumericsVector2(valueWidth, 0f));
        NumericsVector2 valueMin = ImGui.GetItemRectMin();
        NumericsVector2 valueMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(valueMin, valueMax, AkronImGuiTheme.ToU32(boxColor));
        string value = TruncateImGuiTextToWidth(SafeDescribeEntryValue(entry), Math.Max(16f, valueMax.X - valueMin.X - 8f));
        NumericsVector2 valueSize = ImGui.CalcTextSize(value);
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(valueMin.X + Math.Max(4f, ((valueMax.X - valueMin.X) - valueSize.X) * 0.5f), valueMin.Y + 3f),
            AkronImGuiTheme.ToU32(textColor),
            value);

        bool clearPressed = false;
        if (!readOnly) {
            ImGui.SameLine(0f, 2f);
            NumericsVector4 clearColor = HasClearableBinding(entry) ? textColor : AkronImGuiTheme.Muted;
            ImGui.PushStyleColor(ImGuiCol.Text, clearColor);
            clearPressed = ImGui.Button("X##keybind_clear" + id, new NumericsVector2(clearWidth, 0f));
            ImGui.PopStyleColor();
        }
        ImGui.PopStyleColor(3);

        if (clearPressed && HasClearableBinding(entry)) {
            ClearBindingForEntry(entry);
            return false;
        }

        return !readOnly && (labelPressed || valuePressed) && entryEnabled;
    }

    private bool DrawImGuiPlainEntry(ActionEntry entry, string id, bool activeState, bool entryEnabled) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Foreground;
        NumericsVector4 indicatorColor = !entryEnabled ? AkronImGuiTheme.Muted : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;
        bool actionControl = ShouldDrawActionSideBars(entry);

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));

        bool pressed = ImGui.Button("##plain_row" + id, new NumericsVector2(ImGui.GetContentRegionAvail().X, 0f));
        NumericsVector2 min = ImGui.GetItemRectMin();
        NumericsVector2 max = ImGui.GetItemRectMax();
        if (actionControl) {
            DrawImGuiActionSideBars(min, max, indicatorColor);
            string centeredLabel = TruncateImGuiTextToWidth(entry.Label, Math.Max(24f, max.X - min.X - 22f));
            NumericsVector2 textSize = ImGui.CalcTextSize(centeredLabel);
            ImGui.GetWindowDrawList().AddText(
                new NumericsVector2(min.X + Math.Max(10f, ((max.X - min.X) - textSize.X) * 0.5f), min.Y + 3f),
                AkronImGuiTheme.ToU32(textColor),
                centeredLabel);
        } else {
            ImGui.GetWindowDrawList().AddText(
                new NumericsVector2(min.X, min.Y + 3f),
                AkronImGuiTheme.ToU32(textColor),
                TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, max.X - min.X - 12f)));
            ImGui.GetWindowDrawList().AddRectFilled(
                new NumericsVector2(max.X - 5f, min.Y + 1f),
                new NumericsVector2(max.X - 2f, max.Y - 1f),
                AkronImGuiTheme.ToU32(indicatorColor));
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        return pressed;
    }

    private bool DrawImGuiGroupHeaderEntry(ActionEntry entry, string id, bool entryEnabled) {
        bool expanded = IsSoundGroupExpanded(entry.SoundGroupLabel);
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : AkronImGuiTheme.Foreground;
        NumericsVector4 indicatorColor = entryEnabled ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;

        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));

        bool pressed = ImGui.Button("##group_header" + id, new NumericsVector2(ImGui.GetContentRegionAvail().X, 0f));
        NumericsVector2 min = ImGui.GetItemRectMin();
        NumericsVector2 max = ImGui.GetItemRectMax();
        DrawImGuiDisclosureTriangle(min, max, expanded, indicatorColor);

        float valueWidth = Math.Min(82f, Math.Max(60f, (max.X - min.X) * 0.42f));
        string label = TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, max.X - min.X - valueWidth - 24f));
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(min.X + 18f, min.Y + 3f),
            AkronImGuiTheme.ToU32(textColor),
            label);

        string value = TruncateImGuiTextToWidth(SafeDescribeEntryValue(entry), valueWidth);
        NumericsVector2 valueSize = ImGui.CalcTextSize(value);
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(max.X - valueSize.X - 4f, min.Y + 3f),
            AkronImGuiTheme.ToU32(AkronImGuiTheme.Muted),
            value);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return pressed;
    }

    private static void DrawImGuiDisclosureTriangle(NumericsVector2 min, NumericsVector2 max, bool expanded, NumericsVector4 color) {
        float centerY = (min.Y + max.Y) * 0.5f;
        float left = min.X + 5f;
        uint packed = AkronImGuiTheme.ToU32(color);
        if (expanded) {
            ImGui.GetWindowDrawList().AddTriangleFilled(
                new NumericsVector2(left, centerY - 3f),
                new NumericsVector2(left + 8f, centerY - 3f),
                new NumericsVector2(left + 4f, centerY + 4f),
                packed);
            return;
        }

        ImGui.GetWindowDrawList().AddTriangleFilled(
            new NumericsVector2(left + 2f, centerY - 5f),
            new NumericsVector2(left + 2f, centerY + 5f),
            new NumericsVector2(left + 8f, centerY),
            packed);
    }

    private OptionEntryPress DrawImGuiStartPosEntry(ActionEntry entry, string id, bool activeState, bool entryEnabled) {
        NumericsVector4 indicatorColor = !entryEnabled ? AkronImGuiTheme.Muted : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float arrowWidth = Math.Min(30f, Math.Max(24f, availableWidth * 0.12f));
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float buttonWidth = Math.Max(42f, (availableWidth - arrowWidth - spacing * 2f) / 3f);
        Level level = Engine.Scene as Level;

        bool setPressed = DrawStartPosActionButton("Set", "##startpos_set" + id, buttonWidth, entryEnabled && level != null);
        if (setPressed && level != null) {
            AkronActions.SetStartPos(level);
        }

        ImGui.SameLine(0f, spacing);
        bool loadPressed = DrawStartPosActionButton("Load", "##startpos_load" + id, buttonWidth, entryEnabled && level != null);
        if (loadPressed && level != null) {
            AkronActions.LoadStartPos(level);
        }

        ImGui.SameLine(0f, spacing);
        bool clearPressed = DrawStartPosActionButton("Clear", "##startpos_clear" + id, buttonWidth, entryEnabled);
        if (clearPressed) {
            AkronActions.ClearActiveStartPos();
        }

        ImGui.SameLine(0f, 0f);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        bool arrowPressed = ImGui.Button("##open_startpos_options" + id, new NumericsVector2(arrowWidth, 0f));
        NumericsVector2 min = ImGui.GetItemRectMin();
        NumericsVector2 max = ImGui.GetItemRectMax();
        float top = min.Y + 4.5f;
        float bottom = max.Y - 4.5f;
        float right = max.X - 4.5f;
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

        if (setPressed || loadPressed || clearPressed) {
            searchInputActive = false;
            searchInputUsesImGui = false;
            ClearSearchInputFocusRequest();
        }

        return OptionEntryPress.None;
    }

    private bool DrawStartPosActionButton(string label, string id, float width, bool enabled) {
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled ? AkronImGuiTheme.ButtonHovered : AkronImGuiTheme.FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled ? AkronImGuiTheme.ButtonActive : AkronImGuiTheme.FrameBackground);
        if (!enabled) {
            ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.DisabledText);
        }

        bool pressed = ImGui.Button(label + id, new NumericsVector2(width, 0f)) && enabled;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal)) {
            DrawImGuiItemTooltip(label + " StartPos");
        }

        if (!enabled) {
            ImGui.PopStyleColor();
        }
        ImGui.PopStyleColor(3);

        return pressed;
    }

    private static string TruncateImGuiTextToWidth(string text, float maxWidth) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        int widthKey = Math.Max(0, (int) Math.Ceiling(maxWidth));
        (string Text, int Width) cacheKey = (text, widthKey);
        if (imguiTextTruncationCache.TryGetValue(cacheKey, out string cached)) {
            return cached;
        }

        if (ImGui.CalcTextSize(text).X <= maxWidth) {
            CacheImGuiTruncatedText(cacheKey, text);
            return text;
        }

        const string suffix = "...";
        for (int length = text.Length - 1; length > 0; length--) {
            string candidate = text[..length].TrimEnd() + suffix;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth) {
                CacheImGuiTruncatedText(cacheKey, candidate);
                return candidate;
            }
        }

        CacheImGuiTruncatedText(cacheKey, suffix);
        return suffix;
    }

    private static void CacheImGuiTruncatedText((string Text, int Width) key, string value) {
        if (imguiTextTruncationCache.Count > 1024) {
            imguiTextTruncationCache.Clear();
        }

        imguiTextTruncationCache[key] = value;
    }

    private static bool ShouldReadEntryValueForActiveState(ActionEntry entry) {
        if (entry == null) {
            return false;
        }

        if (entry.Control == OverlayEntryControl.Action ||
            entry.Control == OverlayEntryControl.Keybind ||
            entry.Control == OverlayEntryControl.KeybindReadOnly ||
            entry.Control == OverlayEntryControl.GroupHeader) {
            return false;
        }

        return entry.IsToggle ||
               entry.Control == OverlayEntryControl.NumericInput ||
               entry.Control == OverlayEntryControl.Selector;
    }

    private static void DrawImGuiActionSideBars(NumericsVector2 min, NumericsVector2 max, NumericsVector4 color) {
        uint packed = AkronImGuiTheme.ToU32(color);
        ImGui.GetWindowDrawList().AddRectFilled(
            new NumericsVector2(min.X + 1f, min.Y + 1f),
            new NumericsVector2(min.X + 4f, max.Y - 2f),
            packed);
        ImGui.GetWindowDrawList().AddRectFilled(
            new NumericsVector2(max.X - 5f, min.Y + 1f),
            new NumericsVector2(max.X - 2f, max.Y - 2f),
            packed);
    }
}
