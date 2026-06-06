using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);

        menu.Add(new TextMenu.SubHeader("Rulesets", topPadding: false));
        menu.Add(new TextMenu.SubHeader("Current Stack", topPadding: false));
        AddWrappedModMenuSubHeaders(menu, Settings.DescribeRulesetStack(), topPadding: false);
        AddWrappedModMenuSubHeaders(menu, Settings.DescribePrimaryRulesetBehavior(), topPadding: false);
        AddWrappedModMenuSubHeaders(menu, Settings.DescribeOverlayBehavior(), topPadding: false);

        Settings.CreateActiveProfileEntry(menu, inGame);
        menu.Add(new TextMenu.Button("Primary Ruleset: " + AkronModuleSettings.FormatPrimaryRuleset(Settings.PrimaryRuleset)) { Selectable = false });
        foreach (PrimaryRuleset ruleset in (PrimaryRuleset[]) Enum.GetValues(typeof(PrimaryRuleset))) {
            PrimaryRuleset capturedRuleset = ruleset;
            menu.Add(new TextMenu.Button("Use " + AkronModuleSettings.FormatPrimaryRuleset(capturedRuleset)).Pressed(() => ApplyRuleset(capturedRuleset)));
        }
        menu.Add(new TextMenu.OnOff("Streamer Mode", Settings.StreamerMode).Change(value => Settings.StreamerMode = value));
        menu.Add(new TextMenu.OnOff("Safe Mode", Settings.SafeMode).Change(value => Settings.SafeMode = value));

        menu.Add(new TextMenu.SubHeader("Overlay"));
        AddWrappedModMenuSubHeaders(menu, "Most Akron options live in the in-game overlay. Use the Open Overlay binding below, then right-click or Shift-click overlay rows to bind individual actions.", topPadding: false);

        menu.Add(new TextMenu.SubHeader("Community Rulesets"));
        IReadOnlyList<AkronCommunityRulesetManifest> communityRulesets = AkronCommunityRulesets.LoadAvailable();
        if (communityRulesets.Count == 0) {
            menu.Add(new TextMenu.Button("No manifests found.") { Selectable = false });
            AddWrappedModMenuSubHeaders(menu, "Bundle JSON in Rulesets/ or place local imports in Saves/AkronRulesets.", topPadding: false);
        } else {
            foreach (AkronCommunityRulesetManifest manifest in communityRulesets) {
                menu.Add(new TextMenu.Button("Apply " + manifest.Label).Pressed(() => {
                    AkronCommunityRulesets.Apply(manifest);
                    Engine.Scene?.Add(new AkronToast("Applied community ruleset: " + manifest.Label));
                }));
                if (!string.IsNullOrWhiteSpace(manifest.Description)) {
                    menu.Add(new TextMenu.SubHeader(manifest.Description, topPadding: false));
                }
            }
        }

        menu.Add(new TextMenu.SubHeader("Native Hotkey Fallbacks"));
        AddWrappedModMenuSubHeaders(menu, "Akron menu rows are bindable in the overlay by right-clicking or Shift-clicking them. These native config UIs remain for built-in keyboard/controller bindings.", topPadding: false);
        menu.Add(new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => OpenKeyboardConfig(menu)));
        menu.Add(new TextMenu.Button(Dialog.Clean("options_btnconfig")).Pressed(() => OpenButtonConfig(menu)));
        AddHotkeyPreview(menu, "Open Overlay", Settings.ToggleOverlay);
        AddHotkeyPreview(menu, "Retry", Settings.Retry);
        AddHotkeyPreview(menu, "Capture StartPos State", Settings.SaveState);
        AddHotkeyPreview(menu, "Restore StartPos State", Settings.LoadState);
        AddHotkeyPreview(menu, "Reload Room", Settings.ReloadRoom);
        AddHotkeyPreview(menu, "Open Debug Map", Settings.OpenDebugMap);
        AddHotkeyPreview(menu, "Freeze Gameplay", Settings.FreezeGameplay);
        AddHotkeyPreview(menu, "Step Frame", Settings.StepFrame);
        AddHotkeyPreview(menu, "Click Teleport Cursor", Settings.ClickTeleportCursor);
        AddHotkeyPreview(menu, "Cursor Zoom Hold", Settings.CursorZoomHold);
        AddHotkeyPreview(menu, "Set StartPos", Settings.SetStartPos);
        AddHotkeyPreview(menu, "Load StartPos", Settings.LoadStartPos);
        AddHotkeyPreview(menu, "Clear StartPos", Settings.ClearStartPos);
        AddHotkeyPreview(menu, "Previous StartPos", Settings.PreviousStartPos);
        AddHotkeyPreview(menu, "Next StartPos", Settings.NextStartPos);
        for (int slot = 1; slot <= 9; slot++) {
            AddHotkeyPreview(menu, "Load StartPos Slot " + slot, GetStartPosSlotBinding(slot));
        }
        AddHotkeyPreview(menu, "Toggle Hitboxes", Settings.ToggleHitboxes);
        AddHotkeyPreview(menu, "Toggle Entity Inspector", Settings.ToggleEntityInspector);
    }

    private void OpenKeyboardConfig(TextMenu menu) {
        if (Engine.Scene == null) {
            return;
        }

        menu.Focused = false;
        KeyboardConfigUI keyboardConfig = CreateKeyboardConfigUiMethod != null
            ? (KeyboardConfigUI) CreateKeyboardConfigUiMethod.Invoke(this, new object[] { menu })
            : new ModuleSettingsKeyboardConfigUI(this);
        keyboardConfig.OnClose = () => { menu.Focused = true; };
        Engine.Scene.Add(keyboardConfig);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    private void OpenButtonConfig(TextMenu menu) {
        if (Engine.Scene == null) {
            return;
        }

        menu.Focused = false;
        ButtonConfigUI buttonConfig = CreateButtonConfigUiMethod != null
            ? (ButtonConfigUI) CreateButtonConfigUiMethod.Invoke(this, new object[] { menu })
            : new ModuleSettingsButtonConfigUI(this);
        buttonConfig.OnClose = () => { menu.Focused = true; };
        Engine.Scene.Add(buttonConfig);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    private static void AddHotkeyPreview(TextMenu menu, string label, ButtonBinding binding) {
        menu.Add(new TextMenu.Button(label + ": " + AkronModuleSettings.DescribeBinding(binding)) { Selectable = false });
    }

    private static void AddWrappedModMenuSubHeaders(TextMenu menu, string text, bool topPadding = true) {
        foreach (string line in WrapModMenuLine(text)) {
            menu.Add(new TextMenu.SubHeader(line, topPadding));
            topPadding = false;
        }
    }

    internal static IReadOnlyList<string> WrapModMenuLine(string text, int maxCharacters = 64) {
        List<string> lines = new List<string>();
        foreach (string rawParagraph in (text ?? string.Empty).Split('\n')) {
            string paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0) {
                continue;
            }

            string current = string.Empty;
            foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (current.Length == 0) {
                    current = word;
                    continue;
                }

                if (current.Length + 1 + word.Length > maxCharacters) {
                    lines.Add(current);
                    current = word;
                    continue;
                }

                current += " " + word;
            }

            if (current.Length > 0) {
                lines.Add(current);
            }
        }

        return lines.Count == 0 ? new[] { string.Empty } : lines;
    }
}
