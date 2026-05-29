using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawLabelSystemPopupControls(string popupId) {
        DrawLabelStyleRows(
            AkronModule.Settings.LabelBulkStyle,
            popupId,
            "bulk-labels",
            ApplyBulkLabelStyle,
            "Bulk-edit every label row. Individual row popups can override these values afterward.");
    }

    private void DrawBuiltInLabelStylePopupControls(string label, Func<int> getter, Action<int> setter, AkronHudLabelStyleSettings style, string popupId) {
        DrawHitboxColorRow(label, getter, setter, popupId, "Text color for this label group.");
        DrawLabelStyleRows(style, popupId, "label-style-" + label, null, "Style for this label group only.");
    }

    private void DrawToastLabelPopupControls(string popupId) {
        DrawHitboxColorRow("Color", () => AkronModule.Settings.ToastLabelColor, value => AkronModule.Settings.ToastLabelColor = value, popupId, "Text color for option feedback messages.");

        if (ImGui.Button("Anchor: " + AkronModule.Settings.ToastLabelAnchor + "##" + popupId)) {
            AkronModule.Settings.ToastLabelAnchor = NextHudAnchor(AkronModule.Settings.ToastLabelAnchor);
        }
        DrawPopupTooltip("Screen position for option feedback messages. Default is BottomLeft.");

        DrawLabelStyleRows(AkronModule.Settings.ToastLabelStyle, popupId, "label-style-toasts", null, "Style for option feedback messages.");
    }

    private void DrawStartPosLabelPopupControls(string popupId) {
        DrawHitboxColorRow("Color", () => AkronModule.Settings.StartPosLabelColor, value => AkronModule.Settings.StartPosLabelColor = value, popupId, "Text color for the StartPos label.");

        if (ImGui.Button("Anchor: " + AkronModule.Settings.StartPosLabelAnchor + "##" + popupId)) {
            AkronModule.Settings.StartPosLabelAnchor = NextHudAnchor(AkronModule.Settings.StartPosLabelAnchor);
        }
        DrawPopupTooltip("Screen position for the StartPos label. Use BottomCenter for bottom middle.");

        if (ImGui.Button("Format: " + FormatStartPosLabelFormat(AkronModule.Settings.StartPosLabelFormat) + "##" + popupId)) {
            AkronModule.Settings.StartPosLabelFormat = NextStartPosLabelFormat(AkronModule.Settings.StartPosLabelFormat);
        }
        DrawPopupTooltip("Choose between 'StartPos: N/N', bare 'N/N', or slot plus count.");

        DrawLabelStyleRows(AkronModule.Settings.StartPosLabelStyle, popupId, "label-style-startpos", null, "Style for the StartPos label only.");
    }

    private void DrawRoomStatTrackerPopupControls(string popupId) {
        DrawHitboxColorRow("Color", () => AkronModule.Settings.RoomStatTrackerColor, value => AkronModule.Settings.RoomStatTrackerColor = value, popupId, "Text color for the room stat tracker.");
        DrawPopupCheckbox("Room name", () => AkronModule.Settings.RoomStatShowRoomName, value => AkronModule.Settings.RoomStatShowRoomName = value, popupId, "Include the current room name.");
        DrawPopupCheckbox("Deaths", () => AkronModule.Settings.RoomStatShowDeaths, value => AkronModule.Settings.RoomStatShowDeaths = value, popupId, "Include deaths since entering the current room.");
        DrawPopupCheckbox("In-game time", () => AkronModule.Settings.RoomStatShowInGameTime, value => AkronModule.Settings.RoomStatShowInGameTime = value, popupId, "Include elapsed room time.");
        DrawPopupCheckbox("Strawberries", () => AkronModule.Settings.RoomStatShowStrawberries, value => AkronModule.Settings.RoomStatShowStrawberries = value, popupId, "Include strawberries collected in this room visit.");
        DrawPopupCheckbox("Alive time", () => AkronModule.Settings.RoomStatShowAliveTime, value => AkronModule.Settings.RoomStatShowAliveTime = value, popupId, "Include time since the latest respawn.");
        DrawPopupCheckbox("Hide if golden", () => AkronModule.Settings.RoomStatHideIfGolden, value => AkronModule.Settings.RoomStatHideIfGolden = value, popupId, "Hide this label during golden berry attempts.");
        DrawPopupChoiceCombo(
            "Freeze mode",
            () => FormatRoomStatTimerFreezeMode(AkronModule.Settings.RoomStatTimerFreezeMode),
            BuildRoomStatTimerFreezeChoices(),
            popupId,
            "Controls when the room stat timer should stop counting.");
        DrawLabelStyleRows(AkronModule.Settings.RoomTimerLabelStyle, popupId, "label-style-room-stat-tracker", null, "Room Stat Tracker uses the Room Timer label style.");
    }

    private static string FormatStartPosLabelFormat(AkronStartPosLabelFormat format) {
        return format switch {
            AkronStartPosLabelFormat.CountOnly => "N/N",
            AkronStartPosLabelFormat.SlotAndCount => "Slot + N/N",
            _ => "StartPos: N/N"
        };
    }

    private static string FormatRoomStatTimerFreezeMode(AkronRoomStatTimerFreezeMode mode) {
        return mode switch {
            AkronRoomStatTimerFreezeMode.Never => "Never",
            AkronRoomStatTimerFreezeMode.Paused => "Paused",
            AkronRoomStatTimerFreezeMode.Inactive => "Inactive",
            AkronRoomStatTimerFreezeMode.Cutscene => "Cutscene",
            AkronRoomStatTimerFreezeMode.PausedInactiveOrCutscene => "All pauses",
            _ => "Paused/inactive"
        };
    }

    private static AkronStartPosLabelFormat NextStartPosLabelFormat(AkronStartPosLabelFormat format) {
        return format switch {
            AkronStartPosLabelFormat.Prefix => AkronStartPosLabelFormat.CountOnly,
            AkronStartPosLabelFormat.CountOnly => AkronStartPosLabelFormat.SlotAndCount,
            _ => AkronStartPosLabelFormat.Prefix
        };
    }

    private void DrawLabelStyleRows(AkronHudLabelStyleSettings style, string popupId, string id, Action afterChange, string tooltip) {
        DrawLabelStyleRows(style, popupId, id, afterChange, tooltip, true);
    }

    private void DrawLabelStyleRows(AkronHudLabelStyleSettings style, string popupId, string id, Action afterChange, string tooltip, bool includeScaleOpacity) {
        style ??= new AkronHudLabelStyleSettings();
        DrawIntStepperRow("Offset X", () => style.OffsetX, value => {
            style.OffsetX = Calc.Clamp(value, -1920, 1920);
            afterChange?.Invoke();
        }, -5, 5, -1920, 1920, popupId + id, tooltip);
        DrawIntStepperRow("Offset Y", () => style.OffsetY, value => {
            style.OffsetY = Calc.Clamp(value, -1080, 1080);
            afterChange?.Invoke();
        }, -5, 5, -1080, 1080, popupId + id, tooltip);
        if (includeScaleOpacity) {
            DrawIntStepperRow("Scale", () => style.Scale, value => {
                style.Scale = AkronModuleSettings.ClampPercent(value, 50, 250);
                afterChange?.Invoke();
            }, -5, 5, 50, 250, popupId + id, tooltip);
            DrawIntStepperRow("Opacity", () => style.Opacity, value => {
                style.Opacity = AkronModuleSettings.ClampOpacity(value);
                afterChange?.Invoke();
            }, -5, 5, 0, 100, popupId + id, tooltip);
        }
        DrawIntStepperRow("Line spacing", () => style.LineSpacing, value => {
            style.LineSpacing = AkronModuleSettings.ClampCustomLabelLineSpacing(value);
            afterChange?.Invoke();
        }, -5, 5, 50, 250, popupId + id, tooltip);

        bool shadow = style.Shadow;
        if (ImGui.Checkbox("Shadow##" + popupId + id, ref shadow)) {
            style.Shadow = shadow;
            afterChange?.Invoke();
        }
        DrawPopupTooltip(tooltip, "Shadow");
        DrawIntStepperRow("Shadow opacity", () => style.ShadowOpacity, value => {
            style.ShadowOpacity = AkronModuleSettings.ClampOpacity(value);
            afterChange?.Invoke();
        }, -5, 5, 0, 100, popupId + id, tooltip);
        DrawIntStepperRow("Shadow X", () => style.ShadowOffsetX, value => {
            style.ShadowOffsetX = Calc.Clamp(value, -24, 24);
            afterChange?.Invoke();
        }, -1, 1, -24, 24, popupId + id, tooltip);
        DrawIntStepperRow("Shadow Y", () => style.ShadowOffsetY, value => {
            style.ShadowOffsetY = Calc.Clamp(value, -24, 24);
            afterChange?.Invoke();
        }, -1, 1, -24, 24, popupId + id, tooltip);
        DrawHitboxColorRow("Shadow color", () => style.ShadowColor, value => {
            style.ShadowColor = AkronModuleSettings.ClampRgb(value);
            afterChange?.Invoke();
        }, popupId + id, tooltip);
    }

    private void ApplyBulkLabelStyle() {
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(AkronModule.Settings.LabelBulkStyle);
        AkronModule.Settings.RoomLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.InputHistoryLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.InputsPerSecondLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.StartPosLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.RoomTimerLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.DeathStatsLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.TotalAttemptsLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.StatusLabelsLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.ToastLabelStyle = AkronModuleSettings.CloneLabelStyle(style);
        AkronModule.Settings.InputsPerSecondScale = style.Scale;
        AkronModule.Settings.InputsPerSecondOpacity = style.Opacity;

        float customScale = 0.42f * (style.Scale / 100f);
        foreach (AkronCustomHudLabel label in AkronModule.Settings.CustomHudLabelDefinitions ?? Enumerable.Empty<AkronCustomHudLabel>()) {
            label.OffsetX = style.OffsetX;
            label.OffsetY = style.OffsetY;
            label.Scale = Calc.Clamp(customScale, 0.2f, 1.5f);
            label.Opacity = style.Opacity;
            label.LineSpacing = style.LineSpacing;
            label.Shadow = style.Shadow;
            label.ShadowColor = style.ShadowColor;
            label.ShadowOpacity = style.ShadowOpacity;
            label.ShadowOffsetX = style.ShadowOffsetX;
            label.ShadowOffsetY = style.ShadowOffsetY;
        }
    }

    private void DrawCustomHudLabelsPopupControls(string popupId) {
        AkronCustomHudLabel label = AkronCustomHudLabels.GetActiveLabel();
        bool visible = label.Visible;
        if (ImGui.Checkbox("Visible##" + popupId, ref visible)) {
            label.Visible = visible;
        }
        DrawPopupTooltip("Show this label when its event condition passes.");

        string name = label.Name ?? string.Empty;
        if (DrawPopupInputText("Name", ref name, 48, popupId, 154f)) {
            label.Name = name;
            MarkValueEditFreeze();
            InvalidateDisplayActionEntryCache();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        DrawPopupTooltip("Human-readable label name.");

        string text = label.Text ?? string.Empty;
        if (DrawPopupInputText("Text", ref text, 160, popupId, 220f)) {
            label.Text = text;
            MarkValueEditFreeze();
        }
        if (ImGui.IsItemActive()) {
            MarkValueEditFreeze();
        }
        DrawPopupTooltip("Template text. Use variables like {room}, {room_time}, {status}, {round(fps)}, {precision(speed, 1)}, or {if:status:yes:no}.");

        ImGui.Separator();
        if (ImGui.Button("Anchor: " + label.Anchor + "##" + popupId)) {
            label.Anchor = NextHudAnchor(label.Anchor);
        }
        DrawPopupTooltip("Screen alignment for this label.");

        bool absolute = label.AbsolutePosition || label.Anchor == AkronHudAnchor.Absolute;
        if (ImGui.Checkbox("Absolute position##" + popupId, ref absolute)) {
            label.AbsolutePosition = absolute;
            if (absolute) {
                label.Anchor = AkronHudAnchor.Absolute;
            } else if (label.Anchor == AkronHudAnchor.Absolute) {
                label.Anchor = AkronHudAnchor.TopLeft;
            }
        }
        DrawPopupTooltip("Use exact HUD coordinates instead of the shared anchored label container.");

        if (label.AbsolutePosition || label.Anchor == AkronHudAnchor.Absolute) {
            DrawIntStepperRow("Screen X", () => label.X, value => label.X = Calc.Clamp(value, 0, 1920), -5, 5, 0, 1920, popupId, "Absolute HUD X coordinate.");
            DrawIntStepperRow("Screen Y", () => label.Y, value => label.Y = Calc.Clamp(value, 0, 1080), -5, 5, 0, 1080, popupId, "Absolute HUD Y coordinate.");
        }

        DrawIntStepperRow("Offset X", () => label.OffsetX, value => label.OffsetX = Calc.Clamp(value, -1920, 1920), -5, 5, -1920, 1920, popupId, "Horizontal label offset from its anchor or absolute point.");
        DrawIntStepperRow("Offset Y", () => label.OffsetY, value => label.OffsetY = Calc.Clamp(value, -1080, 1080), -5, 5, -1080, 1080, popupId, "Vertical label offset from its anchor or absolute point.");
        DrawFloatValueRow("Scale", () => label.Scale, value => label.Scale = Calc.Clamp(value, 0.2f, 1.5f), -0.05f, 0.05f, 0.2f, 1.5f, "%.2f", popupId, "Label text scale.");
        DrawIntStepperRow("Opacity", () => label.Opacity, value => label.Opacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Label opacity.");
        DrawIntStepperRow("Line spacing", () => label.LineSpacing, value => label.LineSpacing = AkronModuleSettings.ClampCustomLabelLineSpacing(value), -5, 5, 50, 250, popupId, "Multiline label spacing percentage.");
        DrawHitboxColorRow("Color", () => label.Color, value => label.Color = value, popupId, "Label text color.");

        if (ImGui.Button("Text align: " + label.TextAlignment + "##" + popupId)) {
            label.TextAlignment = NextLabelTextAlignment(label.TextAlignment);
        }
        DrawPopupTooltip("Align the text inside the label's measured line.");

        if (ImGui.Button("Font: " + label.Font + "##" + popupId)) {
            label.Font = NextLabelFontTheme(label.Font);
        }
        DrawPopupTooltip("Preset text size. Default uses the manual scale below.");

        bool shadow = label.Shadow;
        if (ImGui.Checkbox("Shadow##" + popupId, ref shadow)) {
            label.Shadow = shadow;
        }
        DrawPopupTooltip("Draw a black outline plus offset shadow for readability over bright gameplay.");
        DrawIntStepperRow("Shadow opacity", () => label.ShadowOpacity, value => label.ShadowOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Outline and shadow opacity.");
        DrawIntStepperRow("Shadow X", () => label.ShadowOffsetX, value => label.ShadowOffsetX = Calc.Clamp(value, -24, 24), -1, 1, -24, 24, popupId, "Shadow horizontal offset.");
        DrawIntStepperRow("Shadow Y", () => label.ShadowOffsetY, value => label.ShadowOffsetY = Calc.Clamp(value, -24, 24), -1, 1, -24, 24, popupId, "Shadow vertical offset.");
        DrawHitboxColorRow("Shadow color", () => label.ShadowColor, value => label.ShadowColor = value, popupId, "Shadow color.");

        ImGui.Separator();
        if (ImGui.Button("Event: " + label.EventMode + "##" + popupId)) {
            label.EventMode = NextLabelEventMode(label.EventMode);
        }
        DrawPopupTooltip("When this label is visible: always, on death, while buttons are held, or on noclip death markers.");
        DrawFloatValueRow("Delay", () => label.EventDelaySeconds, value => label.EventDelaySeconds = Math.Max(0f, value), -0.1f, 0.1f, 0f, 60f, "%.1fs", popupId, "Delay before event labels appear.");
        DrawFloatValueRow("Duration", () => label.EventDurationSeconds, value => label.EventDurationSeconds = Math.Max(0.1f, value), -0.1f, 0.1f, 0.1f, 60f, "%.1fs", popupId, "How long event labels remain visible.");
        bool eventStyle = label.EventOverridesStyle;
        if (ImGui.Checkbox("Event style override##" + popupId, ref eventStyle)) {
            label.EventOverridesStyle = eventStyle;
        }
        DrawPopupTooltip("Temporarily use event scale, color, and opacity while the event is active.");
        DrawFloatValueRow("Event scale", () => label.EventScale, value => label.EventScale = Calc.Clamp(value, 0.2f, 1.5f), -0.05f, 0.05f, 0.2f, 1.5f, "%.2f", popupId, "Event override scale.");
        DrawIntStepperRow("Event opacity", () => label.EventOpacity, value => label.EventOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Event override opacity.");
        DrawHitboxColorRow("Event color", () => label.EventColor, value => label.EventColor = value, popupId, "Event override color.");

        ImGui.Separator();
        if (ImGui.Button("Duplicate##" + popupId)) {
            AkronCustomHudLabels.DuplicateActive();
            InvalidateDisplayActionEntryCache();
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete##" + popupId)) {
            ImGui.OpenPopup("Confirm delete label##" + popupId);
        }
        DrawCustomLabelDeleteConfirmation(popupId, label.Name);

        if (ImGui.Button("Export .akr##" + popupId)) {
            AkronCustomHudLabels.Export();
        }
        ImGui.SameLine();
        if (ImGui.Button("Import Latest .akr##" + popupId)) {
            AkronCustomHudLabels.ImportLatest();
            InvalidateDisplayActionEntryCache();
        }
        DrawPopupTooltip("Import the latest saved custom-label .akr pack from Saves/AkronHudLabels.");
    }

    private void DrawCustomLabelDeleteConfirmation(string popupId, string labelName) {
        string confirmPopupId = "Confirm delete label##" + popupId;
        if (!ImGui.BeginPopupModal(confirmPopupId, ImGuiWindowFlags.AlwaysAutoResize)) {
            return;
        }

        imguiPopupBlockedRowsLastFrame = true;
        ImGui.TextUnformatted("Delete custom label?");
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(labelName) ? "This cannot be undone." : labelName + " will be removed.");
        ImGui.Separator();
        if (ImGui.Button("Delete##confirm-delete-" + popupId, new NumericsVector2(92f, 0f))) {
            AkronCustomHudLabels.DeleteActive();
            InvalidateDisplayActionEntryCache();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel##confirm-delete-" + popupId, new NumericsVector2(92f, 0f))) {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawLabelPlayerOverlapPopupControls(string popupId) {
        AkronModuleSettings settings = CurrentSettingsOrDefault();
        DrawPopupChoiceCombo(
            "Mode",
            () => FormatLabelPlayerOverlapMode(GetLabelPlayerOverlapMode(settings)),
            BuildLabelPlayerOverlapModeChoices(settings),
            popupId,
            "Choose the overlap response. The row itself toggles Player Overlap on or off.");

        bool onlyCurrent = AkronModule.Settings.CustomHudLabelObstructionOnlyOverlappedLabel;
        if (ImGui.Checkbox("Only current label##label-overlap-scope-" + popupId, ref onlyCurrent)) {
            AkronModule.Settings.CustomHudLabelObstructionOnlyOverlappedLabel = onlyCurrent;
        }
        DrawPopupTooltip("When off, any player overlap affects every HUD label. When on, only the overlapped label changes.");

        if (!IsLabelPlayerOverlapEnabled(settings)) {
            return;
        }

        DrawIntStepperRow("Padding", () => AkronModule.Settings.CustomHudLabelObstructionPaddingPixels, value => AkronModule.Settings.CustomHudLabelObstructionPaddingPixels = AkronModuleSettings.ClampCustomLabelObstructionPaddingPixels(value), -5, 5, 0, 400, popupId, "Extra HUD pixels around each text label.");

        if (GetLabelPlayerOverlapMode() == AkronLabelObstructionMode.Fade) {
            DrawIntStepperRow("Opacity", () => AkronModule.Settings.CustomHudLabelObstructedOpacity, value => AkronModule.Settings.CustomHudLabelObstructedOpacity = AkronModuleSettings.ClampOpacity(value), -5, 5, 0, 100, popupId, "Label opacity while Madeline overlaps it.");
            return;
        }

        ImGui.Separator();
        DrawLabelObstructedAnchorChoice("Top left", AkronHudAnchor.TopLeft, popupId);
        DrawLabelObstructedAnchorChoice("Top center", AkronHudAnchor.TopCenter, popupId);
        DrawLabelObstructedAnchorChoice("Top right", AkronHudAnchor.TopRight, popupId);
        DrawLabelObstructedAnchorChoice("Middle left", AkronHudAnchor.MiddleLeft, popupId);
        DrawLabelObstructedAnchorChoice("Center", AkronHudAnchor.Center, popupId);
        DrawLabelObstructedAnchorChoice("Middle right", AkronHudAnchor.MiddleRight, popupId);
        DrawLabelObstructedAnchorChoice("Bottom left", AkronHudAnchor.BottomLeft, popupId);
        DrawLabelObstructedAnchorChoice("Bottom center", AkronHudAnchor.BottomCenter, popupId);
        DrawLabelObstructedAnchorChoice("Bottom right", AkronHudAnchor.BottomRight, popupId);
        DrawIntStepperRow("Offset X", () => AkronModule.Settings.CustomHudLabelObstructedOffsetX, value => AkronModule.Settings.CustomHudLabelObstructedOffsetX = Calc.Clamp(value, -1920, 1920), -5, 5, -1920, 1920, popupId, "Horizontal offset from the move anchor.");
        DrawIntStepperRow("Offset Y", () => AkronModule.Settings.CustomHudLabelObstructedOffsetY, value => AkronModule.Settings.CustomHudLabelObstructedOffsetY = Calc.Clamp(value, -1080, 1080), -5, 5, -1080, 1080, popupId, "Vertical offset from the move anchor.");
    }

    private void DrawLabelObstructedAnchorChoice(string label, AkronHudAnchor anchor, string popupId) {
        bool selected = AkronModule.Settings.CustomHudLabelObstructedAnchor == anchor;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(24f));
        if (ImGui.RadioButton("##label-overlap-anchor-" + label + popupId, selected)) {
            AkronModule.Settings.CustomHudLabelObstructedAnchor = anchor;
        }
        DrawPopupTooltip("Anchor used when overlap mode is Move.", label);
    }

    private static bool IsLabelPlayerOverlapEnabled() {
        return IsLabelPlayerOverlapEnabled(CurrentSettingsOrDefault());
    }

    private static bool IsLabelPlayerOverlapEnabled(AkronModuleSettings settings) {
        return settings.CustomHudLabelObstructionEnabled;
    }

    private static void SetLabelPlayerOverlapEnabled(AkronModuleSettings settings, bool enabled) {
        settings.CustomHudLabelObstructionEnabled = enabled;
    }

    private static AkronLabelObstructionMode GetLabelPlayerOverlapMode() {
        return GetLabelPlayerOverlapMode(CurrentSettingsOrDefault());
    }

    private static AkronLabelObstructionMode GetLabelPlayerOverlapMode(AkronModuleSettings settings) {
        return settings.CustomHudLabelObstructionMode == AkronLabelObstructionMode.Move
            ? AkronLabelObstructionMode.Move
            : AkronLabelObstructionMode.Fade;
    }

    private static IReadOnlyList<SelectorDropdownChoice> BuildLabelPlayerOverlapModeChoices(AkronModuleSettings settings) {
        return new List<SelectorDropdownChoice> {
            LabelPlayerOverlapModeChoice(settings, AkronLabelObstructionMode.Fade),
            LabelPlayerOverlapModeChoice(settings, AkronLabelObstructionMode.Move)
        };
    }

    private static SelectorDropdownChoice LabelPlayerOverlapModeChoice(AkronModuleSettings settings, AkronLabelObstructionMode mode) {
        return new SelectorDropdownChoice(
            FormatLabelPlayerOverlapMode(mode),
            () => GetLabelPlayerOverlapMode(settings) == mode,
            () => settings.CustomHudLabelObstructionMode = mode);
    }

    private static string FormatLabelPlayerOverlapMode(AkronLabelObstructionMode mode) {
        return mode == AkronLabelObstructionMode.Move ? "Move" : "Fade";
    }

}
