using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

// Celeste's native mod options hold only what must work without Akron's overlay: the
// master switch, so a disabled Akron can be turned back on, and the menu bind, so a
// conflicting bind can be fixed when the overlay cannot be opened. Everything else
// lives in the overlay.
public partial class AkronModule {
    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);

        TextMenu.SubHeader restartNote = new TextMenu.SubHeader("Restart Celeste to apply.", topPadding: false) {
            Visible = Settings.Enabled != EnabledThisSession
        };
        menu.Add(new TextMenu.OnOff("Enabled", Settings.Enabled).Change(value => {
            Settings.Enabled = value;
            restartNote.Visible = value != EnabledThisSession;
        }));
        menu.Add(restartNote);

        menu.Add(new TextMenu.Button("Menu Bind (Keyboard)").Pressed(() => OpenMenuBindConfig(menu, new MenuBindKeyboardConfigUI())));
        menu.Add(new TextMenu.Button("Menu Bind (Controller)").Pressed(() => OpenMenuBindConfig(menu, new MenuBindButtonConfigUI())));
    }

    private static void OpenMenuBindConfig(TextMenu menu, TextMenu config) {
        if (Engine.Scene == null) {
            return;
        }

        menu.Focused = false;
        // In a level, keep a remapped key from toggling the HUD while this screen is open,
        // as Everest's ModuleSettings*ConfigUI constructors do. These screens derive from
        // Celeste's base config UI and skip those constructors.
        Level level = Engine.Scene as Level;
        bool allowHudHide = level?.AllowHudHide ?? false;
        if (level != null) {
            level.AllowHudHide = false;
        }
        config.OnClose = () => {
            if (level != null) {
                level.AllowHudHide = allowHudHide;
            }
            menu.Focused = true;
        };
        Engine.Scene.Add(config);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    // Akron reads several keys in the menu bind as a chord (all held together), while
    // Celeste's remap adds each new key next to the old ones. Keeping only the key just
    // added makes this screen replace the bind instead of building a chord. Anything else
    // (a key removed, or nothing pressed) leaves the list alone, so a chord set in the
    // overlay survives opening this screen.
    //
    // Celeste appends the new key and, at Input.MaxBindings, trims the oldest key in the
    // same update, so an add shows up as a longer list or as a new last key at the same
    // length. Comparing count and last key catches both without copying the list.
    internal static void KeepOnlyNewKey(List<Keys> keys, int countBefore, Keys lastBefore) {
        if (keys.Count < 2) {
            return;
        }

        Keys last = keys[keys.Count - 1];
        bool added = keys.Count > countBefore || (keys.Count == countBefore && last != lastBefore);
        if (!added) {
            return;
        }

        keys.Clear();
        keys.Add(last);
    }

    // Index of the bind row in both screens below: header, input info, then the row.
    private const int MenuBindRowIndex = 2;

    private sealed class MenuBindKeyboardConfigUI : KeyboardConfigUI {
        // Everest's patched base constructor calls Reload() without an index, which leaves
        // the selection on a row that no longer exists. Select the bind row, as Everest's
        // own ModuleSettingsKeyboardConfigUI does.
        public MenuBindKeyboardConfigUI() {
            Reload(MenuBindRowIndex);
        }

        public override void Reload(int index = -1) {
            Clear();
            Add(new Header(Dialog.Clean("KEY_CONFIG_TITLE")));
            Add(new InputMappingInfo(controllerMode: false));
            AddMapForceLabel("Open Akron", Settings.ToggleOverlay.Binding);
            Add(new SubHeader(""));
            Add(new Button(Dialog.Clean("KEY_CONFIG_RESET")) {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = ResetPressed
            });
            if (index >= 0) {
                Selection = index;
            }
        }

        public override void Update() {
            List<Keys> keys = Settings.ToggleOverlay.Binding.Keyboard;
            int countBefore = keys.Count;
            Keys lastBefore = countBefore > 0 ? keys[countBefore - 1] : Keys.None;
            base.Update();
            KeepOnlyNewKey(keys, countBefore, lastBefore);
        }

        public override void Reset() {
            Binding binding = Settings.ToggleOverlay.Binding;
            binding.Keyboard.Clear();
            binding.Mouse.Clear();
            binding.Keyboard.Add(Keys.Tab);
            Input.Initialize();
            Reload(Selection);
        }
    }

    // Controller buttons in the menu bind are alternatives in both Akron and Celeste, so
    // Celeste's remap behavior is kept as is.
    private sealed class MenuBindButtonConfigUI : ButtonConfigUI {
        public MenuBindButtonConfigUI() {
            // Celeste's screen only listens for its own gameplay buttons. Everest's mod
            // binding screen adds these four so mod binds can use them; do the same.
            All.Add(Buttons.Back);
            All.Add(Buttons.BigButton);
            All.Add(Buttons.RightStick);
            All.Add(Buttons.LeftStick);
            Reload(MenuBindRowIndex);
        }

        public override void Reload(int index = -1) {
            Clear();
            Add(new Header(Dialog.Clean("BTN_CONFIG_TITLE")));
            Add(new InputMappingInfo(controllerMode: true));
            AddMapForceLabel("Open Akron", Settings.ToggleOverlay.Binding);
            Add(new SubHeader(""));
            Add(new Button(Dialog.Clean("KEY_CONFIG_RESET")) {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = ResetPressed
            });
            if (index >= 0) {
                Selection = index;
            }
        }

        public override void Reset() {
            Settings.ToggleOverlay.Binding.Controller.Clear();
            Input.Initialize();
            Reload(Selection);
        }
    }
}
