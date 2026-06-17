using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static List<OverlayEntry> BuildKeybindOverviewEntries(Level level) {
        List<OverlayEntry> entries = new List<OverlayEntry> {
            new OverlayEntry(
                "In-game only",
                () => true,
                () => AkronModule.Settings.MenuBindingsInGameOnly ? "On" : "Off",
                () => AkronModule.Settings.MenuBindingsInGameOnly = !AkronModule.Settings.MenuBindingsInGameOnly,
                BuildSearchTerms("In-game only", Array.Empty<string>()),
                true,
                OverlayEntryControl.Toggle)
        };

        HashSet<string> addedActionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddKeybindOverviewIfBound(entries, addedActionKeys, "Open Menu", OverlayToggleActionKey, "overlay", "menu");
        foreach (KeybindOverviewSpec spec in BuildDefaultKeybindOverviewSpecs()) {
            AddKeybindOverviewIfBound(entries, addedActionKeys, spec.Label, spec.ActionKey, spec.Tags);
        }

        foreach (KeybindOverviewSpec spec in BuildTopLevelCustomKeybindOverviewSpecs(level)) {
            AddKeybindOverviewIfBound(entries, addedActionKeys, spec.Label, spec.ActionKey, spec.Tags);
        }

        foreach (BindableAction action in BuildPopupBindableActions(level)) {
            AddKeybindOverviewIfBound(entries, addedActionKeys, action.DisplayName, action.ActionKey);
        }

        AddStaleCustomKeybindOverviewEntries(entries, addedActionKeys);
        return entries;
    }

    private static IEnumerable<KeybindOverviewSpec> BuildDefaultKeybindOverviewSpecs() {
        yield return new KeybindOverviewSpec("Retry", BuildActionKey("Shortcuts", "Retry"), "restart", "practice");
        yield return new KeybindOverviewSpec("Reload Room", BuildActionKey("Shortcuts", "Reload Room"), "restart", "room");
        yield return new KeybindOverviewSpec("Reload Chapter", BuildActionKey("Shortcuts", "Reload Chapter"), "restart", "chapter");
        yield return new KeybindOverviewSpec("Capture StartPos State", BuildActionKey("StartPos", "Capture State"), "startpos");
        yield return new KeybindOverviewSpec("Restore StartPos State", BuildActionKey("StartPos", "Restore State"), "startpos");
        yield return new KeybindOverviewSpec("Show Hitboxes", BuildActionKey("Level", "Show Hitboxes"), "hitboxes");
        yield return new KeybindOverviewSpec("Freeze Gameplay", BuildActionKey("Level", "Freeze Gameplay"), "pause", "step");
        yield return new KeybindOverviewSpec("Cursor Zoom", BuildActionKey("Creator", "Cursor Zoom"), "cursor", "zoom");
        yield return new KeybindOverviewSpec("Cursor Tools", BuildActionKey("Creator", "Cursor Tools"), "cursor", "tools", "hold");
        yield return new KeybindOverviewSpec("StartPos Set", PopupActionKey("StartPos", "Set"), "startpos", "practice");
        yield return new KeybindOverviewSpec("StartPos Load", PopupActionKey("StartPos", "Load"), "startpos", "practice");
        yield return new KeybindOverviewSpec("StartPos Clear", PopupActionKey("StartPos", "Clear"), "startpos", "practice");
        yield return new KeybindOverviewSpec("StartPos Previous", PopupActionKey("StartPos", "Previous"), "startpos", "practice");
        yield return new KeybindOverviewSpec("StartPos Next", PopupActionKey("StartPos", "Next"), "startpos", "practice");
        for (int slot = 1; slot <= 9; slot++) {
            yield return new KeybindOverviewSpec("StartPos Slot " + slot, PopupActionKey("StartPos", "Load Slot " + slot), "startpos", "slot");
        }
    }

    private static IEnumerable<KeybindOverviewSpec> BuildTopLevelCustomKeybindOverviewSpecs(Level level) {
        if (AkronModule.Instance == null) {
            yield break;
        }

        foreach (string tabName in GetVisibleTabs()) {
            if (string.Equals(tabName, "Keybinds", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (OverlayEntry entry in BuildEntriesForTab(tabName, level)) {
                string actionKey = string.IsNullOrWhiteSpace(entry.ActionKeyOverride) ? BuildActionKey(tabName, entry.Label) : entry.ActionKeyOverride;
                if (HasMenuBinding(actionKey)) {
                    yield return new KeybindOverviewSpec(entry.Label, actionKey, entry.SearchTerms);
                }
            }
        }
    }

    private static void AddStaleCustomKeybindOverviewEntries(List<OverlayEntry> entries, HashSet<string> addedActionKeys) {
        if (AkronModule.Instance == null) {
            return;
        }

        Dictionary<string, string> bindings = AkronModule.Settings.MenuActionBindings;
        if (bindings == null) {
            return;
        }

        foreach (string actionKey in bindings.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)) {
            AddKeybindOverviewIfBound(entries, addedActionKeys, FormatActionKeyForKeybindOverview(actionKey), actionKey);
        }
    }

    private static void AddKeybindOverviewIfBound(List<OverlayEntry> entries, HashSet<string> addedActionKeys, string label, string actionKey, params string[] tags) {
        if (string.IsNullOrWhiteSpace(actionKey) || !HasVisibleBinding(actionKey) || !addedActionKeys.Add(actionKey)) {
            return;
        }

        entries.Add(KeybindOverview(label, actionKey, tags));
    }

    private static bool HasVisibleBinding(string actionKey) {
        if (AkronModule.Instance == null) {
            return true;
        }

        if (string.Equals(actionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            return !IsEmptyBinding(CurrentSettingsOrDefault().ToggleOverlay);
        }

        return HasMenuBinding(actionKey) ||
               TryGetDefaultButtonBinding(actionKey, out ButtonBinding binding) &&
               !IsEmptyBinding(binding);
    }

    private static string FormatActionKeyForKeybindOverview(string actionKey) {
        if (string.IsNullOrWhiteSpace(actionKey)) {
            return "Unknown Binding";
        }

        const string popupPrefix = "popup/";
        string display = actionKey.StartsWith(popupPrefix, StringComparison.OrdinalIgnoreCase)
            ? actionKey[popupPrefix.Length..]
            : actionKey;
        return display.Replace("/", " / ");
    }

    private static OverlayEntry KeybindOverview(string label, string actionKey, params string[] tags) {
        return new OverlayEntry(
            label,
            () => true,
            () => DescribeOverviewBinding(actionKey, label),
            () => { },
            BuildSearchTerms(label, tags),
            false,
            OverlayEntryControl.Keybind,
            actionKeyOverride: actionKey);
    }

    private static OverlayEntry OverlayToggleKeybind() {
        return new OverlayEntry(
            "Open Menu",
            () => true,
            () => AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay),
            () => { },
            BuildSearchTerms("Open Menu", Array.Empty<string>()),
            false,
            OverlayEntryControl.Keybind,
            actionKeyOverride: OverlayToggleActionKey);
    }
}
