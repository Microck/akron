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

        menu.Add(new TextMenu.SubHeader("Overlay", topPadding: false));
        AddWrappedModMenuSubHeaders(menu, "Most Akron options live in the in-game overlay.", topPadding: false);
        AddHotkeyPreview(menu, "Open Overlay", Settings.ToggleOverlay);

        menu.Add(new TextMenu.SubHeader("Native Bindings"));
        menu.Add(new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => OpenKeyboardConfig(menu)));
        menu.Add(new TextMenu.Button(Dialog.Clean("options_btnconfig")).Pressed(() => OpenButtonConfig(menu)));

        menu.Add(new TextMenu.SubHeader("Safety"));
        menu.Add(new TextMenu.OnOff("Streamer Mode", Settings.StreamerMode).Change(value => Settings.StreamerMode = value));
        menu.Add(new TextMenu.OnOff("Safe Mode", Settings.SafeMode).Change(value => Settings.SafeMode = value));

        if (inGame) {
            Settings.CreateCurrentMapCompatibilityEntry(menu, inGame);
        }
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
