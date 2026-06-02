using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static KeyboardState previousOverlayToggleKeyboard;
    private static KeyboardState previousStartPosHotkeyKeyboard;
    private static bool cursorVisibilityCaptured;
    private static bool previousMouseVisible;

    private static void HandleHotkeys(Level level) {
        if (Overlay?.Visible == true) {
            if (IsOverlayTogglePressed()) {
                Overlay.ResetTransientUiState();
                Overlay.Visible = false;
                Overlay.Active = false;
                UpdateOverlayCursorState();
            }

            return;
        }

        if (IsOverlayTogglePressed()) {
            EnsureOverlay(level);
            bool nextVisible = !Overlay.Visible;
            if (!nextVisible) {
                Overlay.ResetTransientUiState();
            } else {
                Overlay.PrewarmLayout(level);
            }
            Overlay.Visible = nextVisible;
            Overlay.Active = false;
            UpdateOverlayCursorState();
            return;
        }

        if (level.Paused && !Settings.AllowPauseBuffering) {
            return;
        }

        if (Settings.Retry?.Pressed ?? false) {
            Retry(level);
        }

        if (Settings.ReloadRoom?.Pressed ?? false) {
            ReloadRoom(level);
        }

        if (Settings.OpenDebugMap?.Pressed ?? false) {
            OpenDebugMap(level);
        }

        if (Settings.ReloadChapter?.Pressed ?? false) {
            ReloadChapter(level);
        }

        if (Settings.SaveState?.Pressed ?? false) {
            SaveState(level);
        }

        if (Settings.LoadState?.Pressed ?? false) {
            LoadState(level);
        }

        if (Settings.PreviousSlot?.Pressed ?? false) {
            ShiftSavestateSlot(-1);
        }

        if (Settings.NextSlot?.Pressed ?? false) {
            ShiftSavestateSlot(1);
        }

        if (Settings.CycleGrabMode?.Pressed ?? false) {
            AkronActions.CycleGrabMode();
        }

        if (Settings.FreezeGameplay?.Pressed ?? false) {
            AkronActions.ToggleFreeze();
        }

        if (Settings.StepFrame?.Pressed ?? false && Session.FreezeGameplay) {
            Session.StepFrameRequested = true;
        }

        UpdateStepHoldRepeat();

        if (Settings.DecreaseTimescale?.Pressed ?? false) {
            AkronActions.AdjustTimescale(-0.1f);
        }

        if (Settings.IncreaseTimescale?.Pressed ?? false) {
            AkronActions.AdjustTimescale(0.1f);
        }

        HandleFrameBypassBindings();

        KeyboardState startPosKeyboard = Keyboard.GetState();
        KeyboardState previousStartPosKeyboard = previousStartPosHotkeyKeyboard;
        previousStartPosHotkeyKeyboard = startPosKeyboard;

        if (IsStartPosBindingPressed(Settings.SetStartPos, startPosKeyboard, previousStartPosKeyboard)) {
            AkronActions.SetStartPos(level);
        }

        if (IsStartPosBindingPressed(Settings.LoadStartPos, startPosKeyboard, previousStartPosKeyboard)) {
            AkronActions.LoadStartPos(level);
        }

        if (IsStartPosBindingPressed(Settings.ClearStartPos, startPosKeyboard, previousStartPosKeyboard)) {
            AkronActions.ClearActiveStartPos();
        }

        if (IsStartPosBindingPressed(Settings.PreviousStartPos, startPosKeyboard, previousStartPosKeyboard)) {
            AkronActions.ShiftStartPos(level, -1);
        }

        if (IsStartPosBindingPressed(Settings.NextStartPos, startPosKeyboard, previousStartPosKeyboard)) {
            AkronActions.ShiftStartPos(level, 1);
        }

        for (int slot = 1; slot <= 9; slot++) {
            if (IsStartPosBindingPressed(GetStartPosSlotBinding(slot), startPosKeyboard, previousStartPosKeyboard)) {
                AkronActions.LoadStartPosSlot(level, slot);
                break;
            }
        }

        if (Settings.ToggleHitboxes?.Pressed ?? false) {
            bool next = !Settings.HitboxViewer;
            if (next && !TryUse(AkronFeatureKind.HitboxViewer)) {
                return;
            }

            Settings.HitboxViewer = next;
        }

        if (Settings.ToggleEntityInspector?.Pressed ?? false) {
            bool next = !Settings.EntityInspector;
            if (next && !TryUse(AkronFeatureKind.EntityInspector)) {
                return;
            }

            Settings.EntityInspector = next;
        }

        AkronOverlay.ExecuteCustomBoundActions(level);
    }

    private static void HandleFrameBypassBindings() {
        if (!AkronMotionSmoothingInterop.Loaded) {
            return;
        }

        if (Settings.ToggleFrameBypass?.Pressed ?? false) {
            bool next = !AkronRuntimeOptions.ResolveCurrentFrameBypassRates().Active;
            if (next && !TryUse(AkronFeatureKind.FpsBypass)) {
                return;
            }

            Settings.FpsBypass = next;
            if (!next) {
                Settings.TpsBypass = false;
            }

            Engine.Scene?.Add(new AkronToast(next ? "Frame bypass enabled." : "Frame bypass disabled."));
        } else if (Settings.CycleFrameBypassCameraSmoothing?.Pressed ?? false) {
            if (!AkronRuntimeOptions.ResolveCurrentFrameBypassRates().Active) {
                return;
            }

            Settings.FrameBypassCameraSmoothing = Settings.FrameBypassCameraSmoothing switch {
                AkronCameraSmoothingMode.Fancy => AkronCameraSmoothingMode.Fast,
                AkronCameraSmoothingMode.Fast => AkronCameraSmoothingMode.Off,
                _ => AkronCameraSmoothingMode.Fancy
            };
            Engine.Scene?.Add(new AkronToast("Smooth Camera: " + Settings.FrameBypassCameraSmoothing));
        }
    }

    private static void HandleGlobalOverlayHotkeys(Scene scene) {
        if (Overlay?.Visible == true) {
            if (IsOverlayTogglePressed()) {
                Overlay.ResetTransientUiState();
                Overlay.Visible = false;
                Overlay.Active = false;
                UpdateOverlayCursorState();
            }

            return;
        }

        if (IsOverlayTogglePressed()) {
            EnsureOverlay(scene);
            bool nextVisible = !Overlay.Visible;
            if (!nextVisible) {
                Overlay.ResetTransientUiState();
            } else {
                Overlay.PrewarmLayout(scene as Level);
            }
            Overlay.Visible = nextVisible;
            Overlay.Active = false;
            UpdateOverlayCursorState();
        }

        if (scene is not Level) {
            HandleFrameBypassBindings();
        }
    }

    private static void UpdateStepHoldRepeat() {
        if (!Session.FreezeGameplay || !Settings.StepHoldRepeat || !IsKeyboardBindingHeld(Settings.StepFrame?.Keys)) {
            Session.StepFrameHoldFrames = 0;
            Session.StepFrameRepeatCountdown = 0;
            return;
        }

        Session.StepFrameHoldFrames++;
        if (Settings.StepFrame?.Pressed ?? false) {
            Session.StepFrameRepeatCountdown = Calc.Clamp(Settings.StepHoldDelayFrames, 1, 120);
            return;
        }

        if (Session.StepFrameHoldFrames <= Calc.Clamp(Settings.StepHoldDelayFrames, 1, 120)) {
            return;
        }

        Session.StepFrameRepeatCountdown--;
        if (Session.StepFrameRepeatCountdown <= 0) {
            Session.StepFrameRequested = true;
            Session.StepFrameRepeatCountdown = Calc.Clamp(Settings.StepHoldIntervalFrames, 1, 60);
        }
    }

    public static bool SetOverlayVisible(Scene scene, bool visible) {
        if (scene == null) {
            return false;
        }

        EnsureOverlay(scene);
        bool wasVisible = Overlay.Visible;
        if (!visible || !wasVisible) {
            Overlay.ClearSearchQuery();
        }
        if (!visible) {
            Overlay.ResetTransientUiState();
        } else {
            Overlay.PrewarmLayout(scene as Level);
        }
        Overlay.Visible = visible;
        Overlay.Active = Overlay.Visible && Settings.ConsumeGameplayInputInMenu;
        UpdateOverlayCursorState();
        return Overlay.Visible;
    }

    public static bool ToggleOverlayVisible(Scene scene) {
        return SetOverlayVisible(scene, !IsOverlayVisible);
    }

    public static AkronOverlay GetOverlay(Scene scene, bool ensureVisible = false) {
        if (scene == null) {
            return null;
        }

        EnsureOverlay(scene);
        if (ensureVisible) {
            Overlay.ResetTransientUiState();
            Overlay.PrewarmLayout(scene as Level);
            Overlay.Visible = true;
            Overlay.Active = Overlay.Visible && Settings.ConsumeGameplayInputInMenu;
            UpdateOverlayCursorState();
        }

        return Overlay;
    }

    private static void UpdateOverlayCursorState() {
        bool shouldShowCursor = Overlay?.Visible == true || ShouldShowClickTeleportCursor() || ShouldShowCursorZoomCursor();
        if (shouldShowCursor) {
            if (!cursorVisibilityCaptured) {
                previousMouseVisible = Engine.Instance.IsMouseVisible;
                cursorVisibilityCaptured = true;
            }

            Engine.Instance.IsMouseVisible = true;
            return;
        }

        RestoreCursorVisibility();
    }

    private static bool ShouldShowClickTeleportCursor() {
        return Engine.Scene is Level &&
               Overlay?.Visible != true &&
               Settings.ClickTeleport &&
               (Settings.ClickTeleportCursor?.Check ?? false) &&
               AkronPolicy.CanUse(AkronFeatureKind.ClickTeleport).Allowed;
    }

    private static bool ShouldShowCursorZoomCursor() {
        AkronCursorZoomActivationMode activationMode = AkronModuleSettings.NormalizeCursorZoomActivationMode(Settings.CursorZoomActivationMode);
        return Engine.Scene is Level &&
               Overlay?.Visible != true &&
               Settings.CursorZoom &&
               ((activationMode == AkronCursorZoomActivationMode.Hold && (Settings.CursorZoomHold?.Check ?? false)) ||
                (activationMode == AkronCursorZoomActivationMode.Toggle && cursorZoomToggleActive)) &&
               AkronPolicy.CanUse(AkronFeatureKind.CursorZoom).Allowed;
    }

    private static void RestoreCursorVisibility() {
        if (!cursorVisibilityCaptured) {
            return;
        }

        Engine.Instance.IsMouseVisible = previousMouseVisible;
        cursorVisibilityCaptured = false;
    }

    private static bool IsOverlayTogglePressed() {
        AkronModuleSettings.EnsureCurrentOverlayToggleDefault(Settings);
        ButtonBinding binding = Settings.ToggleOverlay;
        if (binding == null) {
            return false;
        }

        KeyboardState keyboard = Keyboard.GetState();
        KeyboardState previousKeyboard = previousOverlayToggleKeyboard;
        previousOverlayToggleKeyboard = keyboard;

        if (IsOverlayToggleKeyboardPressed(binding.Keys, keyboard, previousKeyboard)) {
            return true;
        }

        if (binding.Buttons != null &&
            Input.Gamepad >= 0 &&
            Input.Gamepad < MInput.GamePads.Length) {
            foreach (Buttons button in binding.Buttons) {
                if (MInput.GamePads[Input.Gamepad].Pressed(button)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOverlayToggleKeyboardPressed(IReadOnlyCollection<Keys> bindingKeys, KeyboardState keyboard, KeyboardState previousKeyboard) {
        if (IsKeyboardBindingPressed(bindingKeys, keyboard, previousKeyboard)) {
            return true;
        }

        // Tab is Akron's canonical menu key. Keep it available even if an old
        // settings file or keybind edit left the overlay binding empty/custom.
        return IsRawKeyPressed(Keys.Tab, keyboard, previousKeyboard);
    }

    private static bool IsKeyboardBindingPressed(IReadOnlyCollection<Keys> keys, KeyboardState keyboard, KeyboardState previousKeyboard) {
        if (keys == null || keys.Count == 0) {
            return false;
        }

        List<Keys> nonModifierKeys = keys.Where(key => !IsModifierKey(key)).ToList();
        if (nonModifierKeys.Count == 0 || !nonModifierKeys.Any(key => IsRawKeyPressed(key, keyboard, previousKeyboard))) {
            return false;
        }

        return keys.All(key => keyboard.IsKeyDown(key));
    }

    private static bool IsStartPosBindingPressed(ButtonBinding binding, KeyboardState keyboard, KeyboardState previousKeyboard) {
        if (binding == null) {
            return false;
        }

        if (IsKeyboardBindingPressed(binding.Keys, keyboard, previousKeyboard)) {
            return true;
        }

        if (binding.Buttons != null &&
            Input.Gamepad >= 0 &&
            Input.Gamepad < MInput.GamePads.Length) {
            foreach (Buttons button in binding.Buttons) {
                if (MInput.GamePads[Input.Gamepad].Pressed(button)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static ButtonBinding GetStartPosSlotBinding(int slot) {
        return slot switch {
            1 => Settings.LoadStartPosSlot1,
            2 => Settings.LoadStartPosSlot2,
            3 => Settings.LoadStartPosSlot3,
            4 => Settings.LoadStartPosSlot4,
            5 => Settings.LoadStartPosSlot5,
            6 => Settings.LoadStartPosSlot6,
            7 => Settings.LoadStartPosSlot7,
            8 => Settings.LoadStartPosSlot8,
            9 => Settings.LoadStartPosSlot9,
            _ => null
        };
    }

    private static bool IsRawKeyPressed(Keys key, KeyboardState keyboard, KeyboardState previousKeyboard) {
        return keyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key);
    }

    private static bool IsKeyboardBindingHeld(IReadOnlyCollection<Keys> keys) {
        if (keys == null || keys.Count == 0) {
            return false;
        }

        KeyboardState keyboard = Keyboard.GetState();
        return keys.All(key => keyboard.IsKeyDown(key));
    }

    private static bool IsModifierKey(Keys key) {
        return key == Keys.LeftControl ||
               key == Keys.RightControl ||
               key == Keys.LeftAlt ||
               key == Keys.RightAlt ||
               key == Keys.LeftShift ||
               key == Keys.RightShift;
    }
}
