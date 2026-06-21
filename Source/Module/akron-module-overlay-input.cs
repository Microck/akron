using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private const string EverestModTogglerOuiTypeName = "Celeste.Mod.UI.OuiModToggler";
    private const string CelesteChapterSelectOuiTypeName = "Celeste.OuiChapterSelect";
    private const string CelesteJournalOuiTypeName = "Celeste.OuiJournal";
    private static KeyboardState previousOverlayToggleKeyboard;
    private static KeyboardState previousStartPosHotkeyKeyboard;
    private static bool cursorVisibilityCaptured;
    private static bool previousMouseVisible;

    private static void HandleHotkeys(Level level) {
        if (Overlay?.IsTransientMouseUiActive == true && IsOverlayTogglePressed()) {
            Overlay.CancelTransientMouseUiForOverlayToggle();
            Overlay.PrewarmLayout(level);
            bool wasVisible = Overlay.Visible;
            Overlay.Visible = true;
            Overlay.Active = false;
            UpdateOverlayCursorState();
            LogOverlayVisibilityChange(wasVisible, Overlay.Visible, "transient-toggle");
            return;
        }

        if (Overlay?.Visible == true) {
            if (IsOverlayTogglePressed()) {
                Overlay.ResetTransientUiState();
                bool wasVisible = Overlay.Visible;
                Overlay.Visible = false;
                Overlay.Active = false;
                UpdateOverlayCursorState();
                LogOverlayVisibilityChange(wasVisible, Overlay.Visible, "hotkey");
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
            bool wasVisible = Overlay.Visible;
            Overlay.Visible = nextVisible;
            Overlay.Active = false;
            UpdateOverlayCursorState();
            LogOverlayVisibilityChange(wasVisible, Overlay.Visible, "hotkey");
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

        if (Settings.StepFrame?.Pressed ?? false && Settings.FrameStepper && Session.FreezeGameplay) {
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
        if (Overlay?.IsTransientMouseUiActive == true && IsOverlayTogglePressed()) {
            Overlay.CancelTransientMouseUiForOverlayToggle();
            Overlay.PrewarmLayout(scene as Level);
            Overlay.Visible = true;
            Overlay.Active = false;
            UpdateOverlayCursorState();
            return;
        }

        if (Overlay?.Visible == true) {
            if (IsOverlayTogglePressed()) {
                Overlay.ResetTransientUiState();
                Overlay.Visible = false;
                Overlay.Active = false;
                UpdateOverlayCursorState();
            }

            return;
        }

        if (ShouldSuppressGlobalOverlayToggle(scene)) {
            RefreshOverlayToggleKeyboardState();
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

    internal static bool ShouldSuppressGlobalOverlayToggleForOuiType(string ouiTypeName) {
        return string.Equals(ouiTypeName, EverestModTogglerOuiTypeName, System.StringComparison.Ordinal);
    }

    internal static bool ShouldGiveJournalPriorityForOverlayToggle(string ouiTypeName, ButtonBinding binding) {
        return IsJournalPriorityOuiType(ouiTypeName) &&
               binding != null &&
               IsPlainTabKeyboardBinding(binding.Keys);
    }

    internal static bool ShouldGiveJournalPriorityForOverlayToggle(string ouiTypeName, IReadOnlyCollection<Keys> keys) {
        return IsJournalPriorityOuiType(ouiTypeName) && IsPlainTabKeyboardBinding(keys);
    }

    private static bool IsJournalPriorityOuiType(string ouiTypeName) {
        return string.Equals(ouiTypeName, CelesteChapterSelectOuiTypeName, System.StringComparison.Ordinal) ||
               string.Equals(ouiTypeName, CelesteJournalOuiTypeName, System.StringComparison.Ordinal);
    }

    private static bool ShouldSuppressGlobalOverlayToggle(Scene scene) {
        if (scene is not Overworld overworld) {
            return false;
        }

        string ouiTypeName = overworld.Current?.GetType().FullName;
        if (ShouldSuppressGlobalOverlayToggleForOuiType(ouiTypeName)) {
            return true;
        }

        AkronModuleSettings.EnsureCurrentOverlayToggleDefault(Settings);
        return ShouldGiveJournalPriorityForOverlayToggle(ouiTypeName, Settings.ToggleOverlay);
    }

    private static bool IsPlainTabKeyboardBinding(IReadOnlyCollection<Keys> keys) {
        if (keys == null || keys.Count == 0) {
            return false;
        }

        List<Keys> normalizedKeys = keys.Where(key => key != Keys.None).Distinct().ToList();
        return normalizedKeys.Count == 1 && normalizedKeys[0] == Keys.Tab;
    }

    private static void RefreshOverlayToggleKeyboardState() {
        AkronModuleSettings.EnsureCurrentOverlayToggleDefault(Settings);
        previousOverlayToggleKeyboard = Keyboard.GetState();
    }

    private static void UpdateStepHoldRepeat() {
        if (!Settings.FrameStepper || !Session.FreezeGameplay || !Settings.StepHoldRepeat || !IsKeyboardBindingHeld(Settings.StepFrame?.Keys)) {
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
        LogOverlayVisibilityChange(wasVisible, Overlay.Visible, "api");
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
            bool wasVisible = Overlay.Visible;
            Overlay.ResetTransientUiState();
            Overlay.PrewarmLayout(scene as Level);
            Overlay.Visible = true;
            Overlay.Active = Overlay.Visible && Settings.ConsumeGameplayInputInMenu;
            UpdateOverlayCursorState();
            LogOverlayVisibilityChange(wasVisible, Overlay.Visible, "ensure-visible");
        }

        return Overlay;
    }

    private static void UpdateOverlayCursorState() {
        bool shouldShowCursor = Overlay?.Visible == true ||
                                Overlay?.IsTransientMouseUiActive == true ||
                                ShouldShowEntityInspectorCursor() ||
                                ShouldShowClickTeleportCursor() ||
                                ShouldShowCursorZoomCursor() ||
                                ShouldShowFreeCameraMouseCursor();
        if (shouldShowCursor) {
            ShowManagedCursorForTransientUi();
            return;
        }

        RestoreCursorVisibility();
    }

    internal static void RefreshOverlayCursorState() {
        UpdateOverlayCursorState();
    }

    internal static void ShowManagedCursorForTransientUi() {
        CaptureCursorVisibilityIfNeeded();
        Engine.Instance.IsMouseVisible = true;
    }

    private static void CaptureCursorVisibilityIfNeeded() {
        if (cursorVisibilityCaptured) {
            return;
        }

        previousMouseVisible = Engine.Instance.IsMouseVisible;
        cursorVisibilityCaptured = true;
    }

    private static bool ShouldShowClickTeleportCursor() {
        return Engine.Scene is Level &&
               Overlay?.Visible != true &&
               IsClickTeleportCursorActive() &&
               AkronPolicy.CanUse(AkronFeatureKind.ClickTeleport).Allowed;
    }

    internal static bool ShouldShowEntityInspectorCursor() {
        return Engine.Scene is Level &&
               ShouldShowEntityInspectorCursor(
                   Settings.EntityInspector || Settings.CursorTools,
                   IsEntityInspectorCursorHoldActive() || IsCursorToolsInspectorPinActive(),
                   Overlay?.Visible == true,
                   AkronPolicy.CanUse(AkronFeatureKind.EntityInspector).Allowed);
    }

    internal static bool IsEntityInspectorCursorHoldActive() {
        return IsButtonBindingHeld(AkronModuleSettings.ResolveEntityInspectorCursorHoldBinding(Settings));
    }

    internal static bool ShouldShowEntityInspectorCursor(bool entityInspector, bool cursorHoldBindingHeld, bool overlayVisible, bool policyAllowed) {
        return entityInspector &&
               cursorHoldBindingHeld &&
               !overlayVisible &&
               policyAllowed;
    }

    private static bool IsButtonBindingHeld(ButtonBinding binding) {
        if (binding == null) {
            return false;
        }

        bool sawReadableRawBinding = false;
        if (TryGetButtonBindingKeys(binding, out IReadOnlyCollection<Keys> keys)) {
            sawReadableRawBinding = true;
            if (IsKeyboardBindingHeld(keys, Keyboard.GetState())) {
                return true;
            }
        }

        if (TryGetButtonBindingButtons(binding, out IReadOnlyCollection<Buttons> buttons)) {
            sawReadableRawBinding = true;
            if (IsGamepadBindingHeld(buttons)) {
                return true;
            }
        }

        if (sawReadableRawBinding) {
            return false;
        }

        try {
            if (binding.Check) {
                return true;
            }
        } catch (InvalidProgramException) {
            // Modifier-only defaults can be malformed in old persisted settings.
            // Keep the documented Left Alt hold usable instead of failing closed.
            return Keyboard.GetState().IsKeyDown(Keys.LeftAlt);
        }

        return false;
    }

    private static bool TryGetButtonBindingKeys(ButtonBinding binding, out IReadOnlyCollection<Keys> keys) {
        try {
            keys = binding.Keys;
            return true;
        } catch (InvalidProgramException) {
            keys = null;
            return false;
        }
    }

    private static bool TryGetButtonBindingButtons(ButtonBinding binding, out IReadOnlyCollection<Buttons> buttons) {
        try {
            buttons = binding.Buttons;
            return true;
        } catch (InvalidProgramException) {
            buttons = null;
            return false;
        }
    }

    private static bool IsGamepadBindingHeld(IReadOnlyCollection<Buttons> buttons) {
        if (buttons == null ||
            Input.Gamepad < 0 ||
            Input.Gamepad >= MInput.GamePads.Length) {
            return false;
        }

        return buttons
            .Where(button => button != 0)
            .All(button => MInput.GamePads[Input.Gamepad].CurrentState.IsButtonDown(button)) &&
               buttons.Any(button => button != 0);
    }

    private static bool ShouldShowCursorZoomCursor() {
        AkronCursorZoomActivationMode activationMode = AkronModuleSettings.NormalizeCursorZoomActivationMode(Settings.CursorZoomActivationMode);
        return Engine.Scene is Level &&
               Overlay?.Visible != true &&
               IsCursorZoomEffectiveEnabled() &&
               (IsCursorToolsHoldActive() ||
                (activationMode == AkronCursorZoomActivationMode.Hold && (Settings.CursorZoomHold?.Check ?? false)) ||
                (activationMode == AkronCursorZoomActivationMode.Toggle && cursorZoomToggleActive)) &&
               AkronPolicy.CanUse(AkronFeatureKind.CursorZoom).Allowed;
    }

    private static bool ShouldShowFreeCameraMouseCursor() {
        return Engine.Scene is Level &&
               Overlay?.Visible != true &&
               AkronRuntimeOptions.IsFreeCameraActive(Engine.Scene as Level) &&
               IsFreeCameraMouseControlEffectiveEnabled();
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

    private static void LogOverlayVisibilityChange(bool wasVisible, bool visible, string source) {
        if (wasVisible == visible) {
            return;
        }

        AkronLog.Verbose(nameof(AkronOverlay), "overlay " + (visible ? "opened" : "closed") + "; source=" + source);
        if (!visible) {
            SaveAkronSettingsNow("overlay-closed");
        }
    }

    private static bool IsKeyboardBindingPressed(IReadOnlyCollection<Keys> keys, KeyboardState keyboard, KeyboardState previousKeyboard) {
        if (keys == null || keys.Count == 0) {
            return false;
        }

        List<Keys> normalizedKeys = keys.Where(key => key != Keys.None).Distinct().ToList();
        if (normalizedKeys.Count == 0 || !normalizedKeys.Any(key => IsRawKeyPressed(key, keyboard, previousKeyboard))) {
            return false;
        }

        return normalizedKeys.All(key => keyboard.IsKeyDown(key));
    }

    private static bool IsKeyboardBindingHeld(IReadOnlyCollection<Keys> keys, KeyboardState keyboard) {
        if (keys == null || keys.Count == 0) {
            return false;
        }

        List<Keys> normalizedKeys = keys.Where(key => key != Keys.None).Distinct().ToList();
        return normalizedKeys.Count > 0 && normalizedKeys.All(key => keyboard.IsKeyDown(key));
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
