using System;
using Celeste;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private OptionEntryPress DrawImGuiOptionEntry(ActionEntry entry, string id, bool activeState, bool entryEnabled) {
        NumericsVector4 textColor = !entryEnabled ? AkronImGuiTheme.DisabledText : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Foreground;
        NumericsVector4 indicatorColor = !entryEnabled ? AkronImGuiTheme.Muted : activeState ? AkronImGuiTheme.Accent : AkronImGuiTheme.Muted;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float arrowWidth = Math.Min(30f, Math.Max(24f, availableWidth * 0.12f));

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleColor(ImGuiCol.Button, AkronImGuiTheme.Transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AkronImGuiTheme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AkronImGuiTheme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new NumericsVector2(0f, 0.5f));

        bool labelPressed = ImGui.Button("##option_label" + id, new NumericsVector2(availableWidth - arrowWidth, 0f));
        NumericsVector2 labelMin = ImGui.GetItemRectMin();
        NumericsVector2 labelMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddText(
            new NumericsVector2(labelMin.X, labelMin.Y + 3f),
            AkronImGuiTheme.ToU32(textColor),
            TruncateImGuiTextToWidth(entry.Label, Math.Max(16f, labelMax.X - labelMin.X - 4f)));
        ImGui.SameLine(0f, 0f);
        bool arrowPressed = ImGui.Button("##open_options" + id, new NumericsVector2(arrowWidth, 0f));
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

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        if (arrowPressed) {
            return OptionEntryPress.Arrow;
        }

        return labelPressed ? OptionEntryPress.Label : OptionEntryPress.None;
    }

    private void DrawImGuiOptionsPopup(ActionEntry entry) {
        string popupKey = entry.OptionsPopupKey;
        if (!IsOptionsPopupOpen(popupKey)) {
            return;
        }

        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 maxPopupSize = GetPopupViewportMaxSize(displaySize);
        float maxPopupHeight = maxPopupSize.Y;
        float popupWidth = Math.Min(560f, maxPopupSize.X);
        if (TryGetPopupAnchorRect(popupKey, out Rectangle anchorRect)) {
            NumericsVector2 cachedSize = imguiOptionsPopupSizes.TryGetValue(popupKey, out NumericsVector2 size)
                ? size
                : new NumericsVector2(popupWidth, Math.Min(360f, maxPopupHeight));
            cachedSize = new NumericsVector2(
                Math.Min(Math.Max(1f, cachedSize.X), maxPopupSize.X),
                Math.Min(Math.Max(1f, cachedSize.Y), maxPopupSize.Y));
            ImGui.SetNextWindowPos(CalculateAnchoredPopupPosition(anchorRect, cachedSize, displaySize), ImGuiCond.Always);
        }

        ImGui.SetNextWindowSizeConstraints(new NumericsVector2(Math.Min(320f, popupWidth), 0f), new NumericsVector2(popupWidth, maxPopupHeight));
        // Options panels float above the row grid. Keep them opaque so rows
        // underneath cannot bleed through and read as if the panel is behind.
        ImGui.SetNextWindowBgAlpha(1f);
        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize;
        ImGui.Begin(GetImGuiPopupId(popupKey), flags);

        imguiPopupBlockedRowsLastFrame = true;
        if (openedImGuiOptionsPopupThisFrame) {
            ImGui.SetScrollY(0f);
        }
        if (!openedImGuiOptionsPopupThisFrame &&
            IsAnyImGuiMouseClicked() &&
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
            CloseOptionsPopup();
            ImGui.End();
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NumericsVector2(8f, 2f));
        DrawImGuiOptionsPopupContent(entry, popupKey);
        ImGui.PopStyleVar();
        if (TryGetPopupAnchorRect(popupKey, out anchorRect)) {
            NumericsVector2 actualSize = ImGui.GetWindowSize();
            imguiOptionsPopupSizes[popupKey] = actualSize;
            NumericsVector2 constrainedActualSize = new NumericsVector2(
                Math.Min(Math.Max(1f, actualSize.X), maxPopupSize.X),
                Math.Min(Math.Max(1f, actualSize.Y), maxPopupSize.Y));
            ImGui.SetWindowPos(CalculateAnchoredPopupPosition(anchorRect, constrainedActualSize, displaySize));
        }
        ImGui.End();
    }

    private bool TryGetPopupAnchorRect(string popupKey, out Rectangle rect) {
        if (imguiOptionsPopupAnchorRects.TryGetValue(popupKey, out rect)) {
            return true;
        }

        foreach (ActionLayout action in lastVisibleActionRows) {
            if (string.Equals(action.Entry.OptionsPopupKey, popupKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action.Entry.Label, popupKey, StringComparison.OrdinalIgnoreCase)) {
                rect = action.Rect;
                return true;
            }
        }

        rect = Rectangle.Empty;
        return false;
    }

    public static NumericsVector2 CalculateAnchoredPopupPosition(Rectangle anchorRect, NumericsVector2 popupSize, NumericsVector2 displaySize) {
        (float x, float y) = CalculateAnchoredPopupPosition(
            anchorRect,
            popupSize.X,
            popupSize.Y,
            displaySize.X,
            displaySize.Y);
        return new NumericsVector2(x, y);
    }

    public static (float X, float Y) CalculateAnchoredPopupPosition(Rectangle anchorRect, float popupWidth, float popupHeight, float displayWidth, float displayHeight) {
        return CalculateAnchoredPopupPosition(anchorRect.X, anchorRect.Y, anchorRect.Width, popupWidth, popupHeight, displayWidth, displayHeight);
    }

    public static (float X, float Y) CalculateAnchoredPopupPosition(float anchorX, float anchorY, float anchorWidth, float popupWidth, float popupHeight, float displayWidth, float displayHeight) {
        const float edgePadding = PopupViewportMargin;
        const float anchorGap = 8f;
        popupWidth = Math.Max(1f, popupWidth);
        popupHeight = Math.Max(1f, popupHeight);
        float anchorRight = anchorX + Math.Max(1f, anchorWidth);
        float rightX = anchorRight + anchorGap;
        float leftX = anchorX - popupWidth - anchorGap;
        float x = rightX + popupWidth <= displayWidth - edgePadding ? rightX : leftX;
        x = ClampFloat(x, edgePadding, Math.Max(edgePadding, displayWidth - popupWidth - edgePadding));

        // Keep the popup vertically aligned to the row by default. If it would
        // overflow the bottom edge, subtract exactly that overflow instead of
        // snapping to the top of the screen.
        float y = anchorY;
        float bottomOverflow = y + popupHeight - (displayHeight - edgePadding);
        if (bottomOverflow > 0f) {
            y -= bottomOverflow;
        }

        y = ClampFloat(y, edgePadding, Math.Max(edgePadding, displayHeight - popupHeight - edgePadding));
        return (x, y);
    }

    private static float ClampFloat(float value, float min, float max) {
        return Math.Min(Math.Max(value, min), max);
    }

    private void DrawImGuiBindingControls(ActionEntry entry, string popupId) {
        ImGui.TextUnformatted("Binding: " + DescribeMenuBinding(entry.ActionKey));
        if (ImGui.Button("Bind input##bind-key-" + popupId)) {
            StartBindingCapture(entry);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##clear-binding-" + popupId)) {
            ClearMenuBinding(entry.ActionKey);
        }
    }

    private void DrawImGuiBindingContext(ActionEntry entry, bool openRequested) {
        string popupId = GetImGuiBindingContextId(entry.ActionKey);
        if (openRequested) {
            ImGui.OpenPopup(popupId);
        }

        if (!ImGui.BeginPopup(popupId)) {
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.TextUnformatted(entry.Label);
        ImGui.Separator();
        ImGui.TextUnformatted("Binding: " + SafeDescribeEntryValue(entry));
        ImGui.TextUnformatted("Built-in: " + DescribeBindingForEntry(entry));
        if (ImGui.MenuItem("Bind input")) {
            StartBindingCaptureForEntry(entry);
        }
        if (ImGui.MenuItem("Clear binding", string.Empty, false, HasClearableBinding(entry))) {
            ClearBindingForEntry(entry);
        }
        ImGui.EndPopup();
    }

    private void DrawImGuiPopupBindingContext(string actionKey, string displayName, string builtIn) {
        bool openRequested = ImGui.IsItemHovered() &&
                             (ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
                              (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && IsShiftDown()));
        if (openRequested) {
            ImGui.OpenPopup(GetImGuiBindingContextId(actionKey));
        }

        DrawImGuiPopupBindingTooltip(displayName);
        DrawImGuiPopupBindingContextPopup(actionKey, displayName, builtIn);
    }

    private void DrawImGuiPopupBindingTooltip(string displayName) {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal)) {
            DrawImGuiItemTooltip(displayName + "\nRight-click or Shift-click to bind.");
        }
    }

    private void DrawImGuiPopupBindingContextPopup(string actionKey, string displayName, string builtIn) {
        string popupId = GetImGuiBindingContextId(actionKey);
        if (!ImGui.BeginPopup(popupId)) {
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.TextUnformatted(displayName);
        ImGui.Separator();
        ImGui.TextUnformatted("Binding: " + DescribeMenuBinding(actionKey));
        ImGui.TextUnformatted("Built-in: " + builtIn);
        if (ImGui.MenuItem("Bind input")) {
            StartBindingCapture(actionKey, displayName);
        }
        if (ImGui.MenuItem("Clear binding", string.Empty, false, HasMenuBinding(actionKey))) {
            ClearMenuBinding(actionKey);
        }
        ImGui.EndPopup();
    }

    private void DrawImGuiBindingCapturePopup() {
        if (string.IsNullOrWhiteSpace(bindingCaptureActionKey)) {
            return;
        }

        string popupId = "akron_bind_key_capture";
        ImGui.OpenPopup(popupId);
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize)) {
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.TextUnformatted((bindingCaptureAutoDeafenHotkey ? "Bind key for " : "Bind input for ") + bindingCaptureDisplayName);
        ImGui.Separator();
        ImGui.TextUnformatted(bindingCaptureAutoDeafenHotkey ? "Press a key to bind it." : "Press a key or controller button to bind it.");
        ImGui.TextUnformatted(bindingCaptureOverlayToggle ? "Escape cancels. Backspace resets to default." : "Escape cancels. Backspace clears.");
        ImGui.EndPopup();
    }

    private static string GetInternalRecorderExperimentalWarningPopupId() {
        return "Internal Recorder Warning##akron_internal_recorder_experimental_warning";
    }

    private static bool ShouldWarnBeforeInternalRecorderAction(ActionEntry entry) {
        return entry != null &&
               entry.Control == OverlayEntryControl.Action &&
               string.Equals(entry.Tab, "Internal Recorder", StringComparison.OrdinalIgnoreCase) &&
               !AkronModule.Settings.InternalRecorderExperimentalWarningDismissed &&
               !IsSettingsOnlyOptionsRow(entry.Label);
    }

    private void DrawInternalRecorderExperimentalWarningPopup() {
        if (pendingInternalRecorderExperimentalAction == null) {
            return;
        }

        string popupId = GetInternalRecorderExperimentalWarningPopupId();
        NumericsVector2 popupSize = new NumericsVector2(520f, 210f);
        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 popupPosition = new NumericsVector2(
            Math.Max(0f, (displaySize.X - popupSize.X) * 0.5f),
            Math.Max(0f, (displaySize.Y - popupSize.Y) * 0.5f));

        ImGui.OpenPopup(popupId);
        ImGui.SetNextWindowPos(popupPosition, ImGuiCond.Always);
        ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8f);
        ImGui.TextUnformatted("Internal Recorder is EXPERIMENTAL.");
        ImGui.Spacing();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 470f);
        ImGui.TextWrapped("Please report any bugs or issues to https://github.com/Microck/akron.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Checkbox("Don't show this again##internal-recorder-experimental-warning", ref internalRecorderExperimentalWarningDontShowAgain);

        ImGui.SetCursorPosY(popupSize.Y - 48f);
        ImGui.SetCursorPosX(popupSize.X - 296f);
        if (ImGui.Button("Continue##internal-recorder-experimental-warning", new NumericsVector2(136f, 0f))) {
            if (internalRecorderExperimentalWarningDontShowAgain) {
                AkronModule.Settings.InternalRecorderExperimentalWarningDismissed = true;
            }

            ActionEntry action = pendingInternalRecorderExperimentalAction;
            pendingInternalRecorderExperimentalAction = null;
            internalRecorderExperimentalWarningDontShowAgain = false;
            ImGui.CloseCurrentPopup();
            ExecuteActionEntry(action, "imgui");
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##internal-recorder-experimental-warning", new NumericsVector2(136f, 0f))) {
            pendingInternalRecorderExperimentalAction = null;
            internalRecorderExperimentalWarningDontShowAgain = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawImGuiActionTooltip(ActionEntry entry, bool rowHovered) {
        if (!TryGetActionDescription(entry.Label, out _)) {
            return;
        }

        if (rowHovered && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal)) {
            NumericsVector2 min = ImGui.GetItemRectMin();
            NumericsVector2 max = ImGui.GetItemRectMax();
            Rectangle anchor = RectCeiling(min.X, min.Y, Math.Max(1f, max.X - min.X), Math.Max(1f, max.Y - min.Y));
            pendingImGuiTooltipEntry = entry;
            pendingImGuiTooltipAnchor = anchor;
        }
    }

    private void DrawPendingImGuiActionTooltip() {
        ActionEntry entry = pendingImGuiTooltipEntry;
        if (entry == null || pendingImGuiTooltipAnchor == Rectangle.Empty || !TryGetActionDescription(entry.Label, out string description)) {
            return;
        }

        string tooltipKey = entry.Tab + "\n" + entry.Label;
        NumericsVector2 cachedSize = imguiTooltipSizes.TryGetValue(tooltipKey, out NumericsVector2 size)
            ? size
            : new NumericsVector2(TooltipMaxWidth, 160f);
        NumericsVector2 actualSize = DrawAnchoredTooltipWindow(pendingImGuiTooltipAnchor, cachedSize, () => {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + GetTooltipWrapWidth());
            ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.Accent);
            ImGui.TextUnformatted(entry.Label);
            ImGui.PopStyleColor();
            ImGui.TextUnformatted(DescribeTooltipMeta(entry));
            ImGui.Separator();
            ImGui.TextWrapped(description);
            ImGui.PopTextWrapPos();
        });
        imguiTooltipSizes[tooltipKey] = actualSize;
    }

    private static NumericsVector2 DrawAnchoredTooltipWindow(Rectangle anchor, NumericsVector2 expectedSize, Action drawContent) {
        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 maxSize = GetPopupViewportMaxSize(displaySize);
        NumericsVector2 constrainedExpectedSize = new NumericsVector2(
            Math.Min(Math.Max(1f, expectedSize.X), maxSize.X),
            Math.Min(Math.Max(1f, expectedSize.Y), maxSize.Y));
        ImGui.SetNextWindowPos(CalculateAnchoredPopupPosition(anchor, constrainedExpectedSize, displaySize), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new NumericsVector2(1f, 1f), maxSize);
        ImGui.SetNextWindowBgAlpha(AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity) / 100f);
        ImGui.BeginTooltip();
        drawContent?.Invoke();
        NumericsVector2 actualSize = ImGui.GetWindowSize();
        NumericsVector2 constrainedActualSize = new NumericsVector2(
            Math.Min(Math.Max(1f, actualSize.X), maxSize.X),
            Math.Min(Math.Max(1f, actualSize.Y), maxSize.Y));
        ImGui.SetWindowPos(CalculateAnchoredPopupPosition(anchor, constrainedActualSize, displaySize));
        ImGui.EndTooltip();
        return actualSize;
    }

    private static NumericsVector2 GetPopupViewportMaxSize(NumericsVector2 displaySize) {
        return new NumericsVector2(
            Math.Max(1f, displaySize.X - PopupViewportMargin * 2f),
            Math.Max(1f, displaySize.Y - PopupViewportMargin * 2f));
    }

    private static float GetTooltipWrapWidth() {
        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        return Math.Min(ImGuiTooltipWrapWidth, Math.Max(1f, displaySize.X - PopupViewportMargin * 2f - 20f));
    }

    private static string DescribeTooltipMeta(ActionEntry entry) {
        string state = SafeDescribeEntryValue(entry);
        string control = DescribeEntryControl(entry);
        string classification = DescribeEntryClassification(entry);
        if (string.IsNullOrWhiteSpace(classification)) {
            return control + " / " + state;
        }

        return control + " / " + state + " / " + classification;
    }

    private static string DescribeEntryControl(ActionEntry entry) {
        if (entry.Control == OverlayEntryControl.NumericInput) {
            return entry.HasOptionsPopup ? "Numeric toggle with settings" : "Numeric toggle";
        }

        if (entry.Control == OverlayEntryControl.Selector) {
            return entry.HasOptionsPopup ? "Selector with settings" : "Selector";
        }

        if (entry.Control == OverlayEntryControl.Keybind || entry.Control == OverlayEntryControl.KeybindReadOnly) {
            return "Keybind";
        }

        if (entry.Control == OverlayEntryControl.SearchInput) {
            return "Text input";
        }

        if (entry.Control == OverlayEntryControl.Color) {
            return "Color";
        }

        bool hasOptions = entry.HasOptionsPopup;
        if (entry.Control == OverlayEntryControl.Action) {
            return hasOptions ? "Action with settings" : "Action";
        }

        return hasOptions ? "Toggle with settings" : "Toggle";
    }

    private static string DescribeEntryClassification(ActionEntry entry) {
        if (!entry.FeatureKind.HasValue) {
            return TryClassifyOverlayUiLabel(entry.Label, out AkronStatus labelStatus)
                ? AkronModuleSettings.FormatStatus(labelStatus)
                : string.Empty;
        }

        FeatureDefinition definition = AkronFeatureRegistry.Get(entry.FeatureKind.Value);
        return AkronModuleSettings.FormatStatus(definition.Classification);
    }

    private static string SafeDescribeEntryValue(ActionEntry entry) {
        try {
            if (entry.Control == OverlayEntryControl.NumericInput) {
                return (entry.Value?.Invoke() ?? "Ready") + " / " + FormatNumericEntryValue(entry);
            }

            if (entry.Control == OverlayEntryControl.SearchInput) {
                return string.Empty;
            }

            string value = entry.Value?.Invoke();
            return string.IsNullOrWhiteSpace(value) ? "Ready" : value;
        } catch {
            return "Unavailable";
        }
    }

    private static string GetImGuiPopupId(string label) {
        return "##akron_options_" + label;
    }

    private static string GetImGuiBindingContextId(string actionKey) {
        return "akron_binding_context_" + actionKey;
    }

    private static bool IsAnyImGuiMouseClicked() {
        return ImGui.IsMouseClicked(ImGuiMouseButton.Left) ||
               ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
               ImGui.IsMouseClicked(ImGuiMouseButton.Middle);
    }
}
