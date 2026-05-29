using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void RenderSection(SectionLayout section) {
        float opacity = AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity) / 100f;
        Draw.Rect(section.Bounds, AkronWindowBackground * opacity);

        bool headerHovered = string.Equals(hoveredHeaderTitle, section.Title, StringComparison.OrdinalIgnoreCase);
        Draw.Rect(section.HeaderRect, headerHovered ? AkronAccentHovered * 0.98f : AkronAccent * 0.98f);

        string displayTitle = GetSectionDisplayTitle(section);
        float titleWidth = MeasureMenuText(displayTitle, AkronTitleFontSize).X;
        DrawMenuText(
            displayTitle,
            new Vector2(section.HeaderRect.Center.X - titleWidth / 2f, section.HeaderRect.Y + 1f),
            AkronTitleFontSize,
            Color.White);
        DrawCollapseTriangle(section.HeaderRect, section.Collapsed ? 1 : 0, Color.White * 0.82f);

        if (section.Collapsed) {
            return;
        }

        foreach (InfoRowLayout row in section.Rows) {
            RenderInfoSectionRow(row);
        }
    }

    private string GetSectionDisplayTitle(SectionLayout section) {
        return section.Title;
    }

    private void RenderInfoSectionRow(InfoRowLayout row) {
        switch (row.Row.Kind) {
            case RowKind.Search:
                RenderSearchRow(row.Rect);
                return;
            default:
                RenderInfoRow(row);
                return;
        }
    }

    private void RenderActionEntry(ActionLayout action) {
        bool selected = selectedPanel == SelectionPanel.Actions &&
                        selectedActionIndex == action.ActualIndex &&
                        selectedTabIndex == action.TabIndex;
        bool hovered = hoveredActionIndex == action.ActualIndex &&
                       hoveredActionTabIndex == action.TabIndex;
        string value = DescribeRenderedEntryValue(action.Entry);
        bool onState = IsOnState(value);
        bool offState = IsOffState(value);
        bool stateOnly = action.Entry.IsToggle || onState || offState;
        bool enabledState = stateOnly && onState;
        bool hasOptionsPopup = action.Entry.HasOptionsPopup;
        bool popupOpen = IsOptionsPopupOpen(action.Entry.Label);
        bool accentState = enabledState || popupOpen;
        bool numericInput = action.Entry.Control == OverlayEntryControl.NumericInput;
        bool selectorInput = action.Entry.Control == OverlayEntryControl.Selector;
        bool keybindInput = action.Entry.Control == OverlayEntryControl.Keybind || action.Entry.Control == OverlayEntryControl.KeybindReadOnly;
        bool searchInput = action.Entry.Control == OverlayEntryControl.SearchInput;
        bool buttonOnly = !stateOnly && !hasOptionsPopup && ShouldDrawActionSideBars(action.Entry);
        bool entryEnabled = action.Entry.Enabled();
        Color activeColor = ResolveActionActiveColor(action.Entry, selected || hovered);

        if (hovered) {
            Draw.Rect(action.Rect, new Color(28, 28, 28) * 0.58f);
        } else if (selected) {
            Draw.Rect(action.Rect, AkronFrameBackground * 0.20f);
        }

        float labelWidth = hasOptionsPopup
            ? Math.Max(120f, GetOptionsButtonRect(action.Rect).X - action.Rect.X - 8f)
            : Math.Max(120f, action.Rect.Width - 12f);
        string label = TruncateToWidth(action.Entry.Label, labelWidth, AkronRowFontSize);
        Color textColor = !entryEnabled ? AkronDisabledText : accentState ? activeColor : Color.White;
        Color indicatorColor = !entryEnabled ? AkronInactiveIndicator : accentState ? activeColor : AkronInactiveIndicator;
        if (numericInput) {
            RenderNumericActionEntry(action, label, textColor, indicatorColor, hasOptionsPopup);
            return;
        }

        if (selectorInput) {
            RenderSelectorActionEntry(action, label, textColor, indicatorColor, hasOptionsPopup);
            return;
        }

        if (keybindInput) {
            RenderKeybindActionEntry(action, label, textColor, action.Entry.Control == OverlayEntryControl.KeybindReadOnly);
            return;
        }

        if (searchInput) {
            RenderSearchActionEntry(action, label, searchInputActive ? activeColor : textColor);
            return;
        }

        if (buttonOnly) {
            Vector2 textSize = MeasureMenuText(label, AkronRowFontSize);
            float centeredX = action.Rect.X + Math.Max(12f, (action.Rect.Width - textSize.X) * 0.5f);
            DrawMenuText(
                label,
                new Vector2(centeredX, action.Rect.Y + 2.5f),
                AkronRowFontSize,
                textColor);
            DrawActionSideBars(action.Rect, selected || hovered ? activeColor : AkronInactiveIndicator);
            return;
        }

        DrawMenuText(
            label,
            new Vector2(action.Rect.X + 8f, action.Rect.Y + 2.5f),
            AkronRowFontSize,
            textColor);

        if (hasOptionsPopup) {
            DrawOptionsTriangle(action.Rect, indicatorColor);
        } else {
            DrawRightStateBar(action.Rect, indicatorColor);
        }
    }

    private void RenderSearchActionEntry(ActionLayout action, string label, Color textColor) {
        Rectangle valueRect = Rect(action.Rect.X + 2f, action.Rect.Y + 2f, 96f, action.Rect.Height - 4f);
        Draw.Rect(valueRect, AkronFrameBackground * 0.28f);
        string value = string.IsNullOrWhiteSpace(searchQuery) ? string.Empty : searchQuery;
        DrawMenuText(TruncateToWidth(value, valueRect.Width - 8f, AkronSmallFontSize), new Vector2(valueRect.X + 4f, valueRect.Y + 3f), AkronSmallFontSize, textColor);
        if (searchInputActive) {
            float caretX = valueRect.X + 4f + (string.IsNullOrWhiteSpace(searchQuery) ? 0f : Math.Min(valueRect.Width - 10f, MeasureMenuText(searchQuery, AkronSmallFontSize).X + 2f));
            Draw.Rect(caretX, valueRect.Y + 4f, 1f, valueRect.Height - 8f, AkronAccent * 0.95f);
        }

        float labelLeft = valueRect.Right + 6f;
        DrawMenuText(
            TruncateToWidth(label, Math.Max(40f, action.Rect.Right - labelLeft - 10f), AkronRowFontSize),
            new Vector2(labelLeft, action.Rect.Y + 2.5f),
            AkronRowFontSize,
            textColor);
    }
    private static Color ResolveActionActiveColor(ActionEntry entry, bool selectedOrHovered) {
        if (TryClassifyActionEntry(entry, out AkronStatus status)) {
            return ColorFromRgb(AkronPolicy.GetStatusColorRgb(status));
        }

        return selectedOrHovered ? AkronAccentHovered : AkronAccent;
    }

    private static bool TryClassifyActionEntry(ActionEntry entry, out AkronStatus status) {
        if (entry.FeatureKind.HasValue) {
            status = AkronFeatureRegistry.Get(entry.FeatureKind.Value).Classification;
            return true;
        }

        return TryClassifyOverlayUiLabel(entry.Label, out status);
    }

    private static void RenderKeybindActionEntry(ActionLayout action, string label, Color textColor, bool readOnly) {
        Rectangle clearRect = readOnly
            ? Rectangle.Empty
            : Rect(action.Rect.Right - 20f, action.Rect.Y + 2f, 16f, action.Rect.Height - 4f);
        Rectangle valueRect = Rect((readOnly ? action.Rect.Right - 84f : clearRect.X - 84f), action.Rect.Y + 2f, 78f, action.Rect.Height - 4f);
        float labelRight = valueRect.X - 6f;

        DrawMenuText(
            TruncateToWidth(label, Math.Max(40f, labelRight - action.Rect.X - 8f), AkronRowFontSize),
            new Vector2(action.Rect.X + 8f, action.Rect.Y + 2.5f),
            AkronRowFontSize,
            textColor);

        Draw.Rect(valueRect, AkronFrameBackground * 0.28f);
        string value = TruncateToWidth(SafeDescribeEntryValue(action.Entry), valueRect.Width - 8f, AkronSmallFontSize);
        float valueWidth = MeasureMenuText(value, AkronSmallFontSize).X;
        DrawMenuText(
            value,
            new Vector2(valueRect.Center.X - valueWidth / 2f, valueRect.Y + 4f),
            AkronSmallFontSize,
            textColor);

        if (!readOnly) {
            DrawMenuText("X", new Vector2(clearRect.X + 4f, clearRect.Y + 2f), AkronSmallFontSize, HasClearableBinding(action.Entry) ? textColor : AkronInactiveIndicator);
        }
    }

    private static void RenderSelectorActionEntry(ActionLayout action, string label, Color textColor, Color indicatorColor, bool hasOptionsPopup) {
        Rectangle valueRect = Rect(action.Rect.X + 2f, action.Rect.Y + 2f, 82f, action.Rect.Height - 4f);
        Draw.Rect(valueRect, AkronFrameBackground * 0.28f);
        string value = TruncateToWidth(SafeDescribeEntryValue(action.Entry), valueRect.Width - 22f, AkronSmallFontSize);
        DrawMenuText(value, new Vector2(valueRect.X + 4f, valueRect.Y + 3f), AkronSmallFontSize, textColor);
        DrawDropdownTriangle(valueRect, indicatorColor);

        float labelLeft = valueRect.Right + 6f;
        float labelRight = hasOptionsPopup ? GetOptionsButtonRect(action.Rect).X - 4f : action.Rect.Right - 10f;
        DrawMenuText(
            TruncateToWidth(label, Math.Max(40f, labelRight - labelLeft), AkronRowFontSize),
            new Vector2(labelLeft, action.Rect.Y + 2.5f),
            AkronRowFontSize,
            textColor);

        if (hasOptionsPopup) {
            DrawOptionsTriangle(action.Rect, indicatorColor);
        } else {
            DrawRightStateBar(action.Rect, indicatorColor);
        }
    }

    private void RenderNumericActionEntry(ActionLayout action, string label, Color textColor, Color indicatorColor, bool hasOptionsPopup) {
        const float valueWidth = 58f;
        Rectangle valueRect = Rect(action.Rect.X + 2f, action.Rect.Y + 2f, valueWidth, action.Rect.Height - 4f);
        Draw.Rect(valueRect, AkronFrameBackground * 0.28f);
        string value = FormatNumericEntryValue(action.Entry);
        DrawMenuText(
            TruncateToWidth(value, valueRect.Width - 6f, AkronSmallFontSize),
            new Vector2(valueRect.X + 4f, valueRect.Y + 3f),
            AkronSmallFontSize,
            textColor);

        float labelLeft = valueRect.Right + 6f;
        float labelRight = hasOptionsPopup ? GetOptionsButtonRect(action.Rect).X - 4f : action.Rect.Right - 10f;
        DrawMenuText(
            TruncateToWidth(label, Math.Max(40f, labelRight - labelLeft), AkronRowFontSize),
            new Vector2(labelLeft, action.Rect.Y + 2.5f),
            AkronRowFontSize,
            textColor);

        if (hasOptionsPopup) {
            DrawOptionsTriangle(action.Rect, indicatorColor);
        } else {
            DrawRightStateBar(action.Rect, indicatorColor);
        }
    }

    private static string FormatNumericEntryValue(ActionEntry entry) {
        float value = entry.NumericValue?.Invoke() ?? 0f;
        string formatted = entry.NumericInteger
            ? ((int) Math.Round(value)).ToString()
            : value.ToString(ConvertImGuiFormat(entry.NumericFormat));
        return string.IsNullOrWhiteSpace(entry.NumericSuffix) ? formatted : formatted + " " + entry.NumericSuffix;
    }

    private static string BuildNumericInputFormat(ActionEntry entry) {
        string format = string.IsNullOrWhiteSpace(entry.NumericFormat) ? "%.2f" : entry.NumericFormat;
        string suffix = entry.NumericSuffix?.Trim();
        if (string.IsNullOrWhiteSpace(suffix)) {
            return format;
        }

        return suffix switch {
            "%" => format,
            "x" or "s" => format + suffix,
            _ => format + " " + suffix
        };
    }

    private static string ConvertImGuiFormat(string format) {
        return format switch {
            "%.0f" => "0",
            "%.2f" => "0.00",
            "%.3f" => "0.000",
            _ => "0.0"
        };
    }

    private void RenderTooltip() {
        if (hoveredActionLayout == null ||
            hoveredTooltipSeconds < TooltipDelaySeconds ||
            !TryGetActionDescription(hoveredActionLayout.Entry.Label, out _)) {
            return;
        }

        const float paddingX = 11f;
        const float paddingY = 9f;
        float textHeight = Math.Max(12f, MeasureMenuText("Ag", AkronSmallFontSize).Y);
        float lineAdvance = textHeight + 4f;
        List<string> lines = BuildSpriteTooltipLines(hoveredActionLayout.Entry, TooltipMaxWidth - paddingX * 2f);
        if (lines.Count == 0) {
            return;
        }

        float width = 0f;
        foreach (string line in lines) {
            width = Math.Max(width, MeasureMenuText(line, AkronSmallFontSize).X);
        }

        width += paddingX * 2f;
        float height = paddingY * 2f + textHeight + Math.Max(0, lines.Count - 1) * lineAdvance;
        Vector2 mouse = MInput.Mouse.Position;
        float x = mouse.X + 16f;
        float y = mouse.Y + 18f;
        if (x + width > ScreenWidth - OverlayMargin) {
            x = mouse.X - width - 16f;
        }
        if (y + height > ScreenHeight - OverlayMargin) {
            y = ScreenHeight - OverlayMargin - height;
        }

        x = Math.Max(OverlayMargin, x);
        y = Math.Max(OverlayMargin, y);
        Rectangle bounds = RectCeiling(x, y, width, height);
        Draw.Rect(bounds, new Color(41, 41, 41) * 0.98f);

        float lineY = bounds.Y + paddingY;
        foreach (string line in lines) {
            DrawMenuText(line, new Vector2(bounds.X + paddingX, lineY - 2f), AkronSmallFontSize, Color.White);
            lineY += lineAdvance;
        }
    }

    private static List<string> BuildSpriteTooltipLines(ActionEntry entry, float maxWidth) {
        List<string> lines = new List<string> {
            entry.Label,
            DescribeTooltipMeta(entry)
        };

        if (TryGetActionDescription(entry.Label, out string description)) {
            lines.AddRange(WrapTooltipText(description, maxWidth, AkronSmallFontSize));
        }

        return lines;
    }

    private static void DrawCollapseTriangle(Rectangle rect, int direction, Color color) {
        int left = rect.X + 8;
        int top = rect.Y + 7;
        if (direction > 0) {
            for (int row = 0; row < 10; row++) {
                int width = row <= 5 ? row + 1 : 10 - row;
                Draw.Rect(left, top + row, width, 1, color);
            }
            return;
        }

        for (int row = 0; row < 8; row++) {
            int inset = row;
            int width = 14 - inset * 2;
            if (width > 0) {
                Draw.Rect(left + inset, top + row, width, 1, color);
            }
        }
    }

    private static void DrawRightStateBar(Rectangle rect, Color color) {
        Draw.Rect(rect.Right - 5f, rect.Y + 1f, 3f, rect.Height - 2f, color * 0.96f);
    }

    private static void DrawDropdownTriangle(Rectangle rect, Color color) {
        int centerX = rect.Right - 10;
        int centerY = rect.Y + rect.Height / 2;
        for (int row = 0; row < 5; row++) {
            int width = 9 - row * 2;
            Draw.Rect(centerX - width / 2, centerY - 2 + row, width, 1, color * 0.96f);
        }
    }

    private static void DrawActionSideBars(Rectangle rect, Color color) {
        Draw.Rect(rect.X + 1f, rect.Y + 1f, 3f, rect.Height - 3f, color * 0.96f);
        Draw.Rect(rect.Right - 5f, rect.Y + 1f, 3f, rect.Height - 3f, color * 0.96f);
    }

    private static bool ShouldDrawActionSideBars(ActionEntry entry) {
        if (entry.Control != OverlayEntryControl.Action || !IsUtilityButtonTab(entry.Tab)) {
            return false;
        }

        if (string.Equals(entry.Tab, "Bypass", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(entry.Tab, "Creator", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return string.Equals(entry.Label, "Retry", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Uncomplete Level", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Restart Level", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Reload Room", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Reload Chapter", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Neutral Drop", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Backboost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Skip Cutscene / Dialogue", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Export Proof JSON", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Capture StartPos State", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Restore StartPos State", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Open Options", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Start Recording", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Stop Recording", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Save Replay Buffer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "Place StartPos", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "CPU", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Label, "AMD", StringComparison.OrdinalIgnoreCase);
    }

    private void RenderActionScrollbar() {
        if (currentEntries.Count == 0) {
            return;
        }

        SectionLayout actionsSection = lastSections.FirstOrDefault(section => string.Equals(section.Title, GetSelectedTabName(), StringComparison.OrdinalIgnoreCase));
        if (actionsSection == null || actionsSection.Collapsed) {
            return;
        }

        int visibleCount = lastVisibleActionRows.Count;
        if (visibleCount == 0 || visibleCount >= currentEntries.Count) {
            return;
        }

        float trackX = actionsSection.BodyRect.Right - 6f;
        float trackY = actionsSection.BodyRect.Y + BodyPadding;
        float trackHeight = actionsSection.BodyRect.Height - BodyPadding * 2f;
        Draw.Rect(trackX, trackY, 2f, trackHeight, AkronInactiveIndicator * 0.45f);

        float thumbHeight = Math.Max(18f, trackHeight * visibleCount / currentEntries.Count);
        float thumbY = trackY + (trackHeight - thumbHeight) * actionScrollIndex / Math.Max(1, currentEntries.Count - visibleCount);
        Draw.Rect(trackX - 1f, thumbY, 4f, thumbHeight, AkronAccent * 0.96f);
    }

    private void RenderSearchRow(Rectangle rect) {
        Draw.Rect(rect, AkronFrameBackground * 0.20f);
        string value = string.IsNullOrWhiteSpace(searchQuery) ? "Type to search actions" : searchQuery;
        Color color = string.IsNullOrWhiteSpace(searchQuery) ? AkronInactiveIndicator : Color.White;
        DrawMenuText(TruncateToWidth(value, rect.Width - 12f, AkronSmallFontSize), new Vector2(rect.X + 6f, rect.Y + 3f), AkronSmallFontSize, color);
    }

    private void RenderInfoRow(InfoRowLayout row) {
        string value = row.Row.Value();
        Color valueColor = row.Row.ValueColorRgb?.Invoke() is int rgb ? ColorFromRgb(rgb) : Color.White;
        const float labelWidth = 94f;
        const float valueLeft = 102f;
        DrawMenuText(TruncateToWidth(row.Row.Label, labelWidth, AkronSmallFontSize), new Vector2(row.Rect.X + 2f, row.Rect.Y + 3f), AkronSmallFontSize, new Color(188, 188, 188));
        DrawMenuText(TruncateToWidth(value, row.Rect.Width - valueLeft, AkronSmallFontSize), new Vector2(row.Rect.X + valueLeft, row.Rect.Y + 3f), AkronSmallFontSize, valueColor);
    }

}
