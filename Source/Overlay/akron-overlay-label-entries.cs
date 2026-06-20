using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static List<OverlayEntry> BuildLabelEntries() {
        AkronModuleSettings settings = AkronModule.Instance == null ? new AkronModuleSettings() : AkronModule.Settings;
        List<AkronCustomHudLabel> customLabels = AkronCustomHudLabels.CloneLabels(settings.CustomHudLabelDefinitions);
        List<string> normalizedLabelOrder = AkronModuleSettings.NormalizeLabelRowOrder(settings.LabelRowOrder, customLabels);
        if (AkronModule.Instance != null) {
            settings.LabelRowOrder = normalizedLabelOrder;
        }

        Dictionary<string, OverlayEntry> rows = new Dictionary<string, OverlayEntry>(StringComparer.OrdinalIgnoreCase) {
            ["Death Stats"] = LabelToggle("Death Stats", () => settings.DeathStatsWidget, value => settings.DeathStatsWidget = value),
            ["Room"] = LabelToggle("Room", () => settings.RoomLabels, value => settings.RoomLabels = value),
            ["Status"] = LabelToggle("Status", () => settings.StatusLabelsWidget, value => settings.StatusLabelsWidget = value),
            ["Toasts"] = LabelToggle("Toasts", () => settings.ToastLabels, value => settings.ToastLabels = value, "toast", "notification", "option feedback"),
            ["Cheat Indicator"] = LabelToggle("Cheat Indicator", () => settings.HudCheatIndicator, value => settings.HudCheatIndicator = value),
            ["Input History"] = LabelToggle("Input History", () => settings.InputViewer || settings.InputHistoryPanel, value => {
                settings.InputViewer = value;
                settings.InputHistoryPanel = value;
            }),
            ["Inputs per second"] = LabelPolicyToggle("Inputs per second", AkronFeatureKind.InputsPerSecondCounter, () => settings.InputsPerSecondCounter, value => settings.InputsPerSecondCounter = value),
            ["Dash Stats"] = LabelToggle("Dash Stats", () => settings.DashCountStats, value => settings.DashCountStats = value, "dash count", "stats"),
            ["Jump Stats"] = LabelToggle("Jump Stats", () => settings.JumpCount, value => settings.JumpCount = value, "jump count", "stats"),
            ["StartPos HUD"] = LabelPolicyToggle("StartPos HUD", AkronFeatureKind.StartPosTools, () => settings.StartPosShowLabel, value => settings.StartPosShowLabel = value),
            ["Room Timer"] = LabelToggle("Room Timer", () => settings.RoomTimerWidget, value => settings.RoomTimerWidget = value),
            ["Room Stat Tracker"] = LabelToggle("Room Stat Tracker", () => settings.RoomStatTracker, value => settings.RoomStatTracker = value),
            ["Attempts"] = LabelToggle("Attempts", () => settings.TotalAttemptsWidget, value => settings.TotalAttemptsWidget = value),
            ["No Short Numbers"] = LabelToggle("No Short Numbers", () => settings.NoShortNumbers, value => settings.NoShortNumbers = value)
        };

        foreach (AkronCustomHudLabel label in customLabels) {
            if (string.IsNullOrWhiteSpace(label.Id)) {
                continue;
            }

            string key = AkronModuleSettings.BuildCustomLabelRowKey(label.Id);
            rows[key] = CustomLabelRow(label, key);
        }

        List<OverlayEntry> entries = new List<OverlayEntry> {
            Toggle("Visible", () => settings.LabelSystemVisible, value => settings.LabelSystemVisible = value, "show labels", "labels", "hud labels", "status labels", "all labels"),
            new OverlayEntry(
                "Player Overlap",
                () => true,
                () => IsLabelPlayerOverlapEnabled(settings) ? "On" : "Off",
                () => SetLabelPlayerOverlapEnabled(settings, !IsLabelPlayerOverlapEnabled(settings)),
                BuildSearchTerms("Player Overlap", new[] { "player", "madeline", "overlap", "fade", "move", "opacity" }),
                true,
                OverlayEntryControl.Toggle)
        };

        foreach (string key in normalizedLabelOrder) {
            if (rows.TryGetValue(key, out OverlayEntry row)) {
                entries.Add(row);
            }
        }

        entries.Add(new OverlayEntry(
            "+ Custom",
            () => true,
            () => string.Empty,
            () => AkronCustomHudLabels.AddCustom(),
            BuildSearchTerms("Add Custom", new[] { "custom label", "hud label", "new label" }),
            false,
            OverlayEntryControl.Action,
            AkronFeatureKind.CustomHudLabels,
            actionKeyOverride: BuildActionKey("Labels", "+ Custom"),
            isAddCustomLabelRow: true));
        return entries;
    }

    private static OverlayEntry LabelToggle(string label, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => getter() ? "On" : "Off",
            () => setter(!getter()),
            BuildSearchTerms(label, tags),
            true,
            OverlayEntryControl.Toggle,
            rowOrderKey: label,
            reorderable: true);
    }

    private static OverlayEntry LabelPolicyToggle(string label, AkronFeatureKind featureKind, Func<bool> getter, Action<bool> setter, params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => getter() ? "On" : "Off",
            () => {
                bool next = !getter();
                if (next && !AkronModule.TryUse(featureKind)) {
                    return;
                }

                setter(next);
            },
            BuildSearchTerms(label, tags),
            true,
            OverlayEntryControl.Toggle,
            featureKind,
            rowOrderKey: label,
            reorderable: true);
    }

    private static OverlayEntry CustomLabelRow(AkronCustomHudLabel label, string rowOrderKey) {
        string labelId = label.Id;
        string displayName = string.IsNullOrWhiteSpace(label.Name) ? "Custom" : label.Name;
        return new OverlayEntry(
            displayName,
            () => true,
            () => {
                AkronCustomHudLabel active = FindCustomHudLabel(labelId);
                return active?.Visible == true ? "On" : "Off";
            },
            () => {
                AkronCustomHudLabel active = FindCustomHudLabel(labelId);
                if (active == null) {
                    return;
                }

                active.Visible = !active.Visible;
                AkronModule.Settings.CustomHudLabels = true;
            },
            BuildSearchTerms(displayName, new[] { "custom label", "hud label", "template" }),
            true,
            OverlayEntryControl.Toggle,
            AkronFeatureKind.CustomHudLabels,
            actionKeyOverride: BuildActionKey("Labels", rowOrderKey),
            forceOptionsPopup: true,
            optionsPopupKey: BuildActionKey("Labels", rowOrderKey),
            rowOrderKey: rowOrderKey,
            reorderable: true,
            customHudLabelId: labelId);
    }

    private static AkronCustomHudLabel FindCustomHudLabel(string id) {
        return AkronModule.Settings.CustomHudLabelDefinitions?.FirstOrDefault(label => string.Equals(label.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
