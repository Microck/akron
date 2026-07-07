using System;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronPromptOption {
    public AkronPromptOption(string label, Action action) {
        Label = label;
        Action = action;
    }

    public string Label { get; }
    public Action Action { get; }
}

public static class AkronPromptMenu {
    private static Level currentLevel;
    private static TextMenu currentMenu;
    private static string currentTitle = string.Empty;
    private static AkronPromptOption[] currentOptions = Array.Empty<AkronPromptOption>();
    private static bool restoreOverlayOnClose;

    public static bool IsOpen => currentMenu != null && currentLevel != null && currentMenu.Scene != null;

    public static void Show(Level level, string title, string message, params AkronPromptOption[] options) {
        if (level == null) {
            return;
        }

        if (level.Entities.FirstOrDefault(entity => entity is TextMenu) is TextMenu existingMenu) {
            existingMenu.Close();
        }

        level.wasPaused = true;
        if (!level.Paused) {
            level.StartPauseEffects();
        }

        level.Paused = true;
        level.PauseMainMenuOpen = false;

        // Akron prompts should own the screen while they are open. Hiding the
        // overlay avoids stacked UI and stale hover state behind modal prompts.
        restoreOverlayOnClose = AkronModule.IsOverlayVisible;
        if (restoreOverlayOnClose) {
            AkronModule.SetOverlayVisible(level, false);
        }

        TextMenu menu = new TextMenu();
        menu.OnESC = menu.OnCancel = menu.OnPause = () => Close(level, menu);
        menu.Add(new TextMenu.Header(title));

        foreach (string line in message.Split('\n')) {
            menu.Add(new TextMenu.SubHeader(line, topPadding: false));
        }

        menu.Add(new TextMenuExt.SubHeaderExt(string.Empty) { HeightExtra = 20f });
        foreach (AkronPromptOption option in options) {
            menu.Add(new TextMenu.Button(option.Label).Pressed(() => {
                Close(level, menu);
                option.Action?.Invoke();
            }));
        }

        string closeLabel = options.Length == 0 ? "OK" : Dialog.Clean("menu_return_cancel");
        menu.Add(new TextMenu.Button(closeLabel).Pressed(menu.OnCancel));
        currentLevel = level;
        currentMenu = menu;
        currentTitle = title ?? string.Empty;
        currentOptions = options ?? Array.Empty<AkronPromptOption>();
        level.Add(menu);
    }

    public static string DescribeState() {
        if (!IsOpen) {
            return "closed";
        }

        string options = currentOptions.Length == 0
            ? "none"
            : string.Join(" | ", currentOptions.Select(option => option.Label));
        return "open;title=" + currentTitle + ";options=" + options;
    }

    public static bool ExecuteOption(string labelOrIndex) {
        if (!IsOpen) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(labelOrIndex)) {
            return false;
        }

        AkronPromptOption option = null;
        if (int.TryParse(labelOrIndex, out int parsedIndex)) {
            int zeroBased = parsedIndex - 1;
            if (zeroBased >= 0 && zeroBased < currentOptions.Length) {
                option = currentOptions[zeroBased];
            }
        } else {
            option = currentOptions.FirstOrDefault(candidate => string.Equals(candidate.Label, labelOrIndex, StringComparison.OrdinalIgnoreCase));
        }

        if (option == null) {
            return false;
        }

        Close(currentLevel, currentMenu);
        option.Action?.Invoke();
        return true;
    }

    private static void Close(Level level, TextMenu menu) {
        menu.Focused = false;
        menu.RemoveSelf();
        level.Paused = false;
        level.unpauseTimer = 0.15f;
        Audio.Play(SFX.ui_game_unpause);
        if (ReferenceEquals(menu, currentMenu)) {
            currentMenu = null;
            currentLevel = null;
            currentTitle = string.Empty;
            currentOptions = Array.Empty<AkronPromptOption>();
            if (restoreOverlayOnClose) {
                AkronModule.SetOverlayVisible(level, true);
                restoreOverlayOnClose = false;
            }
        }
    }
}
