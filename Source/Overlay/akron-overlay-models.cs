using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private sealed class OverlayEntry {
        public OverlayEntry(
            string label,
            Func<bool> enabled,
            Func<string> value,
            Action execute,
            string searchTerms = "",
            bool isToggle = false,
            OverlayEntryControl control = OverlayEntryControl.Toggle,
            AkronFeatureKind? featureKind = null,
            Func<float> numericValue = null,
            Action<float> numericSetter = null,
            float numericMinimum = 0f,
            float numericMaximum = 0f,
            string numericFormat = null,
            string numericSuffix = null,
            bool numericInteger = false,
            string actionKeyOverride = null,
            Func<IReadOnlyList<SelectorDropdownChoice>> selectorChoices = null,
            bool forceOptionsPopup = false,
            string optionsPopupKey = null,
            string rowOrderKey = null,
            bool reorderable = false,
            string customHudLabelId = null,
            bool isAddCustomLabelRow = false,
            string soundGroupLabel = null) {
            Label = label;
            Enabled = enabled;
            Value = value;
            Execute = execute;
            SearchTerms = searchTerms ?? string.Empty;
            IsToggle = isToggle;
            Control = control;
            FeatureKind = featureKind;
            NumericValue = numericValue;
            NumericSetter = numericSetter;
            NumericMinimum = numericMinimum;
            NumericMaximum = numericMaximum;
            NumericFormat = numericFormat;
            NumericSuffix = numericSuffix;
            NumericInteger = numericInteger;
            ActionKeyOverride = actionKeyOverride;
            SelectorChoices = selectorChoices;
            ForceOptionsPopup = forceOptionsPopup;
            OptionsPopupKey = optionsPopupKey;
            RowOrderKey = rowOrderKey;
            Reorderable = reorderable;
            CustomHudLabelId = customHudLabelId;
            IsAddCustomLabelRow = isAddCustomLabelRow;
            SoundGroupLabel = soundGroupLabel;
        }

        public string Label { get; }
        public string ActionKeyOverride { get; }
        public Func<bool> Enabled { get; }
        public Func<string> Value { get; }
        public Action Execute { get; }
        public string SearchTerms { get; }
        public bool IsToggle { get; }
        public OverlayEntryControl Control { get; }
        public AkronFeatureKind? FeatureKind { get; }
        public Func<float> NumericValue { get; }
        public Action<float> NumericSetter { get; }
        public float NumericMinimum { get; }
        public float NumericMaximum { get; }
        public string NumericFormat { get; }
        public string NumericSuffix { get; }
        public bool NumericInteger { get; }
        public Func<IReadOnlyList<SelectorDropdownChoice>> SelectorChoices { get; }
        public bool ForceOptionsPopup { get; }
        public string OptionsPopupKey { get; }
        public string RowOrderKey { get; }
        public bool Reorderable { get; }
        public string CustomHudLabelId { get; }
        public bool IsAddCustomLabelRow { get; }
        public string SoundGroupLabel { get; }
    }

    private sealed class ActionEntry {
        public ActionEntry(string tab, OverlayEntry entry) {
            Tab = tab;
            Label = entry.Label;
            ActionKey = string.IsNullOrWhiteSpace(entry.ActionKeyOverride) ? BuildActionKey(tab, entry.Label) : entry.ActionKeyOverride;
            Enabled = entry.Enabled;
            Value = entry.Value;
            Execute = entry.Execute;
            SearchTerms = entry.SearchTerms;
            IsToggle = entry.IsToggle;
            Control = entry.Control;
            FeatureKind = entry.FeatureKind;
            NumericValue = entry.NumericValue;
            NumericSetter = entry.NumericSetter;
            NumericMinimum = entry.NumericMinimum;
            NumericMaximum = entry.NumericMaximum;
            NumericFormat = entry.NumericFormat;
            NumericSuffix = entry.NumericSuffix;
            NumericInteger = entry.NumericInteger;
            SelectorChoices = entry.SelectorChoices;
            OptionsPopupKey = string.IsNullOrWhiteSpace(entry.OptionsPopupKey) ? Label : entry.OptionsPopupKey;
            RowOrderKey = entry.RowOrderKey;
            Reorderable = entry.Reorderable;
            CustomHudLabelId = entry.CustomHudLabelId;
            IsAddCustomLabelRow = entry.IsAddCustomLabelRow;
            IsCustomHudLabelRow = !string.IsNullOrWhiteSpace(CustomHudLabelId);
            SoundGroupLabel = entry.SoundGroupLabel;
            HasOptionsPopup = entry.Control != OverlayEntryControl.Keybind &&
                              entry.Control != OverlayEntryControl.KeybindReadOnly &&
                              entry.Control != OverlayEntryControl.SearchInput &&
                              entry.Control != OverlayEntryControl.GroupHeader &&
                              (entry.ForceOptionsPopup || AkronOverlay.HasOptionsPopup(Label));
        }

        public string Tab { get; }
        public string Label { get; }
        public string ActionKey { get; }
        public Func<bool> Enabled { get; }
        public Func<string> Value { get; }
        public Action Execute { get; }
        public string SearchTerms { get; }
        public bool IsToggle { get; }
        public OverlayEntryControl Control { get; }
        public AkronFeatureKind? FeatureKind { get; }
        public Func<float> NumericValue { get; }
        public Action<float> NumericSetter { get; }
        public float NumericMinimum { get; }
        public float NumericMaximum { get; }
        public string NumericFormat { get; }
        public string NumericSuffix { get; }
        public bool NumericInteger { get; }
        public Func<IReadOnlyList<SelectorDropdownChoice>> SelectorChoices { get; }
        public bool HasOptionsPopup { get; }
        public string OptionsPopupKey { get; }
        public string RowOrderKey { get; }
        public bool Reorderable { get; }
        public string CustomHudLabelId { get; }
        public bool IsCustomHudLabelRow { get; }
        public bool IsAddCustomLabelRow { get; }
        public string SoundGroupLabel { get; }

        public void SetNumericValue(float value) {
            if (NumericSetter == null) {
                return;
            }

            float clamped = NumericMinimum < NumericMaximum
                ? Calc.Clamp(value, NumericMinimum, NumericMaximum)
                : value;
            NumericSetter(clamped);
        }
    }

    private sealed class BindableAction {
        public BindableAction(string actionKey, string displayName, Action execute) {
            ActionKey = actionKey;
            DisplayName = displayName;
            Execute = execute;
        }

        public string ActionKey { get; }
        public string DisplayName { get; }
        public Action Execute { get; }
    }

    private readonly struct KeybindOverviewSpec {
        public KeybindOverviewSpec(string label, string actionKey, params string[] tags) {
            Label = label;
            ActionKey = actionKey;
            Tags = tags ?? Array.Empty<string>();
        }

        public string Label { get; }
        public string ActionKey { get; }
        public string[] Tags { get; }
    }

    private sealed class SelectorDropdownChoice {
        public SelectorDropdownChoice(string label, Func<bool> selected, Action apply, Func<bool> enabled = null) {
            Label = label;
            Selected = selected;
            Apply = apply;
            Enabled = enabled ?? (() => true);
        }

        public string Label { get; }
        public Func<bool> Selected { get; }
        public Action Apply { get; }
        public Func<bool> Enabled { get; }
    }

    private sealed class RowSpec {
        public RowSpec(string label, Func<string> value, RowKind kind, Func<int?> valueColorRgb = null) {
            Label = label;
            Value = value;
            Kind = kind;
            ValueColorRgb = valueColorRgb;
        }

        public string Label { get; }
        public Func<string> Value { get; }
        public RowKind Kind { get; }
        public Func<int?> ValueColorRgb { get; }
    }

    private sealed class SectionLayout {
        public SectionLayout(string title, Rectangle bounds, Rectangle headerRect, Rectangle bodyRect, bool collapsed) {
            Title = title;
            Bounds = bounds;
            HeaderRect = headerRect;
            BodyRect = bodyRect;
            Collapsed = collapsed;
        }

        public string Title { get; }
        public Rectangle Bounds { get; }
        public Rectangle HeaderRect { get; }
        public Rectangle BodyRect { get; }
        public bool Collapsed { get; }
        public List<InfoRowLayout> Rows { get; } = new List<InfoRowLayout>();
    }

    private sealed class InfoRowLayout {
        public InfoRowLayout(RowSpec row, Rectangle rect) {
            Row = row;
            Rect = rect;
        }

        public RowSpec Row { get; }
        public Rectangle Rect { get; }
    }

    private sealed class ActionLayout {
        public ActionLayout(ActionEntry entry, int actualIndex, int tabIndex, Rectangle rect) {
            Entry = entry;
            ActualIndex = actualIndex;
            TabIndex = tabIndex;
            Rect = rect;
        }

        public ActionEntry Entry { get; }
        public int ActualIndex { get; }
        public int TabIndex { get; }
        public Rectangle Rect { get; }
    }

    private enum RowKind {
        Info,
        Search,
        MenuBinding
    }

    private enum SelectionPanel {
        Categories,
        Actions
    }

    private enum OptionEntryPress {
        None,
        Label,
        Arrow,
        Dropdown
    }

    private enum OverlayEntryControl {
        Toggle,
        Action,
        NumericInput,
        Selector,
        SearchInput,
        Keybind,
        KeybindReadOnly,
        Color,
        GroupHeader,
        StartPosActions
    }

    private static readonly string[] BaseTabs = { "Global", "Level", "StartPos", "Backups", "Bypass", "Keybinds", "Player", "Sound", "Creator", "Interface", "Labels", "Shortcuts", "Internal Recorder" };
    private static readonly string[] ExternalToolTabs = { "Speedrun Tool", "CelesteTAS", "Extended Variant Mode", "Extended Camera Dynamics" };

    private static bool IsExternalToolTab(string tabName) {
        return ExternalToolTabs.Contains(tabName, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] GetVisibleTabs() {
        return BuildVisibleTabs(AkronInterop.SpeedrunToolLoaded, AkronInterop.CelesteTasLoaded, AkronInterop.ExtendedVariantModeLoaded, AkronInterop.ExtendedCameraDynamicsLoaded);
    }

    private static string[] BuildVisibleTabs(bool speedrunToolLoaded, bool celesteTasLoaded, bool extendedVariantModeAvailable, bool extendedCameraDynamicsLoaded) {
        List<string> tabs = new List<string>(BaseTabs);
        if (extendedVariantModeAvailable) {
            tabs.Add("Extended Variant Mode");
        }
        if (extendedCameraDynamicsLoaded) {
            tabs.Add("Extended Camera Dynamics");
        }
        if (speedrunToolLoaded) {
            tabs.Add("Speedrun Tool");
        }
        if (celesteTasLoaded) {
            tabs.Add("CelesteTAS");
        }

        return tabs.ToArray();
    }

    private static string[] GetToggleableSections() {
        List<string> sections = new List<string>(BaseTabs);
        sections.AddRange(ExternalToolTabs);
        return sections.ToArray();
    }
}
