using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    public static void ExecuteCustomBoundActions(Level level) {
        foreach (BindableAction action in GetBindableActions(level)) {
            if (IsMenuBindingPressed(action.ActionKey)) {
                action.Execute?.Invoke();
            }
        }
    }

    public static void ExecuteCustomBoundActions(Scene scene) {
        if (AkronModule.Settings.MenuBindingsInGameOnly) {
            return;
        }

        foreach (BindableAction action in GetBindableActions(scene as Level)) {
            if (IsMenuBindingPressed(action.ActionKey)) {
                action.Execute?.Invoke();
            }
        }
    }

    private static List<BindableAction> GetBindableActions(Level level) {
        if (ReferenceEquals(bindableActionCacheLevel, level) &&
            bindableActionCacheRevision == menuBindingRevision &&
            bindableActionCache.Count > 0) {
            return bindableActionCache;
        }

        bindableActionCache.Clear();
        bindableActionCache.AddRange(BuildBindableActions(level));
        bindableActionCacheLevel = level;
        bindableActionCacheRevision = menuBindingRevision;
        return bindableActionCache;
    }

    private static IEnumerable<BindableAction> BuildBindableActions(Level level) {
        foreach (string tabName in GetVisibleTabs()) {
            foreach (OverlayEntry entry in BuildDisplayEntriesForTab(tabName, level)) {
                if (!IsBindableOverlayEntry(entry)) {
                    continue;
                }

                Action execute = string.Equals(entry.Label, "Frame Stepper", StringComparison.OrdinalIgnoreCase)
                    ? ExecuteFrameStepOnce
                    : entry.Execute;
                yield return new BindableAction(BuildActionKey(tabName, entry.Label), tabName + " / " + entry.Label, execute);
            }
        }

        foreach (BindableAction action in BuildPopupBindableActions(level)) {
            yield return action;
        }
    }

    private static bool IsBindableOverlayEntry(OverlayEntry entry) {
        return entry != null && entry.Control != OverlayEntryControl.GroupHeader;
    }

    private static IEnumerable<BindableAction> BuildPopupBindableActions(Level level) {
        yield return new BindableAction(PopupActionKey("Timescale", "Toggle"), "Timescale / Enabled", () => {
            AkronModuleSession session = AkronModule.Session;
            if (session == null) {
                return;
            }

            bool next = !session.TimescaleEnabled;
            if (next && session.TimescaleMultiplier != 1f && !AkronModule.TryUse(AkronFeatureKind.Timescale)) {
                return;
            }

            session.TimescaleEnabled = next;
        });
        yield return new BindableAction(PopupActionKey("Timescale", "Decrease"), "Timescale / Decrease", () => ApplyOptionsPopupDelta("Timescale", -1));
        yield return new BindableAction(PopupActionKey("Timescale", "Increase"), "Timescale / Increase", () => ApplyOptionsPopupDelta("Timescale", 1));
        yield return new BindableAction(PopupActionKey("Timescale", "Reset"), "Timescale / Reset", () => {
            AkronModuleSession session = AkronModule.Session;
            if (session == null) {
                return;
            }

            session.TimescaleMultiplier = 1f;
            session.TimescaleEnabled = false;
#pragma warning disable CS0618
            Engine.TimeRate = 1f;
#pragma warning restore CS0618
            Engine.Scene?.Add(new AkronToast("Timescale reset."));
        });

        yield return new BindableAction(PopupActionKey("StartPos Snapshot Slot", "Previous"), "StartPos Snapshot Slot / Previous", () => ApplyOptionsPopupDelta("StartPos Snapshot Slot", -1));
        yield return new BindableAction(PopupActionKey("StartPos Snapshot Slot", "Next"), "StartPos Snapshot Slot / Next", () => ApplyOptionsPopupDelta("StartPos Snapshot Slot", 1));
        yield return new BindableAction(PopupActionKey("StartPos Snapshot Slot", "Capture"), "StartPos Snapshot Slot / Capture", () => {
            if (level != null) {
                AkronModule.PerformSaveState(level);
            }
        });
        yield return new BindableAction(PopupActionKey("StartPos Snapshot Slot", "Restore"), "StartPos Snapshot Slot / Restore", () => {
            if (level != null) {
                AkronModule.PerformLoadState(level);
            }
        });

        yield return new BindableAction(PopupActionKey("Grab Mode", "Hold"), "Grab Mode / Hold", () => SetConfiguredGrabMode(GrabModes.Hold));
        yield return new BindableAction(PopupActionKey("Grab Mode", "Toggle"), "Grab Mode / Toggle", () => SetConfiguredGrabMode(GrabModes.Toggle));
        yield return new BindableAction(PopupActionKey("Grab Mode", "Invert"), "Grab Mode / Invert", () => SetConfiguredGrabMode(GrabModes.Invert));

        yield return new BindableAction(PopupActionKey("Noclip", "Toggle"), "Noclip / Enabled", () => {
            bool next = !AkronModule.Settings.Noclip;
            if (next && !AkronModule.TryUse(AkronFeatureKind.Noclip)) {
                return;
            }

            AkronModule.Settings.Noclip = next;
        });
        yield return new BindableAction(PopupActionKey("Noclip", "Speed Down"), "Noclip / Speed Down", () => AdjustNoclipSpeed(-24));
        yield return new BindableAction(PopupActionKey("Noclip", "Speed Up"), "Noclip / Speed Up", () => AdjustNoclipSpeed(24));
        yield return new BindableAction(PopupActionKey("Noclip", "Float Down"), "Noclip / Grab Speed Down", () => AdjustNoclipFloatSpeed(-9));
        yield return new BindableAction(PopupActionKey("Noclip", "Float Up"), "Noclip / Grab Speed Up", () => AdjustNoclipFloatSpeed(9));
        yield return new BindableAction(PopupActionKey("Noclip", "Draw On Top"), "Noclip / Draw Madeline On Top", () => AkronModule.Settings.NoclipDrawOnTop = !AkronModule.Settings.NoclipDrawOnTop);
        yield return new BindableAction(PopupActionKey("Hazard Accuracy", "Toggle"), "Hazard Accuracy / Enabled", () => {
            bool next = !AkronModule.Settings.NoclipAccuracy;
            if (next && !AkronModule.TryUse(AkronFeatureKind.HazardAccuracy)) {
                return;
            }

            AkronModule.Settings.NoclipAccuracy = next;
            if (!next) {
                AkronModule.ResetNoclipAccuracy();
            }
        });
        yield return new BindableAction(PopupActionKey("Hazard Accuracy", "Reset"), "Hazard Accuracy / Reset", AkronModule.ResetNoclipAccuracy);

        yield return new BindableAction(PopupActionKey("Frame Stepper", "Step Once"), "Frame Stepper / Step Once", ExecuteFrameStepOnce);
        yield return new BindableAction(PopupActionKey("Frame Stepper", "Repeat"), "Frame Stepper / Hold Repeat", () => AkronModule.Settings.StepHoldRepeat = !AkronModule.Settings.StepHoldRepeat);
        yield return new BindableAction(PopupActionKey("Frame Stepper", "Delay Down"), "Frame Stepper / Delay Down", () => AkronModule.Settings.StepHoldDelayFrames = CycleInt(AkronModule.Settings.StepHoldDelayFrames - 6, 6, 60));
        yield return new BindableAction(PopupActionKey("Frame Stepper", "Delay Up"), "Frame Stepper / Delay Up", () => AkronModule.Settings.StepHoldDelayFrames = CycleInt(AkronModule.Settings.StepHoldDelayFrames + 6, 6, 60));
        yield return new BindableAction(PopupActionKey("Frame Stepper", "Interval Down"), "Frame Stepper / Interval Down", () => AkronModule.Settings.StepHoldIntervalFrames = CycleInt(AkronModule.Settings.StepHoldIntervalFrames - 1, 1, 12));
        yield return new BindableAction(PopupActionKey("Frame Stepper", "Interval Up"), "Frame Stepper / Interval Up", () => AkronModule.Settings.StepHoldIntervalFrames = CycleInt(AkronModule.Settings.StepHoldIntervalFrames + 1, 1, 12));

        yield return new BindableAction(PopupActionKey("Pause Timer", "Seconds Down"), "Pause Timer / Seconds Down", () => ApplyOptionsPopupDelta("Pause Timer", -1));
        yield return new BindableAction(PopupActionKey("Pause Timer", "Seconds Up"), "Pause Timer / Seconds Up", () => ApplyOptionsPopupDelta("Pause Timer", 1));
        yield return new BindableAction(PopupActionKey("Show Trajectory", "Frames Down"), "Show Trajectory / Frames Down", () => ApplyOptionsPopupDelta("Show Trajectory", -1));
        yield return new BindableAction(PopupActionKey("Show Trajectory", "Frames Up"), "Show Trajectory / Frames Up", () => ApplyOptionsPopupDelta("Show Trajectory", 1));

        yield return new BindableAction(PopupActionKey("StartPos", "Previous"), "StartPos / Previous", () => {
            if (level != null) {
                AkronActions.ShiftStartPos(level, -1);
            }
        });
        yield return new BindableAction(PopupActionKey("StartPos", "Next"), "StartPos / Next", () => {
            if (level != null) {
                AkronActions.ShiftStartPos(level, 1);
            }
        });
        yield return new BindableAction(PopupActionKey("StartPos", "Set"), "StartPos / Set", () => {
            if (level != null) {
                AkronActions.SetStartPos(level);
            }
        });
        yield return new BindableAction(PopupActionKey("StartPos", "Load"), "StartPos / Load", () => {
            if (level != null) {
                AkronActions.LoadStartPos(level);
            }
        });
        for (int slot = 1; slot <= 9; slot++) {
            int capturedSlot = slot;
            yield return new BindableAction(PopupActionKey("StartPos", "Load Slot " + capturedSlot), "StartPos / Load Slot " + capturedSlot, () => {
                if (level != null) {
                    AkronActions.LoadStartPosSlot(level, capturedSlot);
                }
            });
        }
        yield return new BindableAction(PopupActionKey("StartPos", "Clear"), "StartPos / Clear", AkronActions.ClearActiveStartPos);
        yield return new BindableAction(PopupActionKey("StartPos", "Place"), "StartPos / Place", () => AkronModule.Settings.StartPosMousePlacement = !AkronModule.Settings.StartPosMousePlacement);
        yield return new BindableAction(PopupActionKey("StartPos", "Respawn"), "StartPos / Respawn Here", () => AkronModule.Settings.RespawnAtStartPos = !AkronModule.Settings.RespawnAtStartPos);
    }

    private static void ExecuteFrameStepOnce() {
        AkronModuleSession session = AkronModule.Session;
        if (AkronModule.Settings.FrameStepper && session?.FreezeGameplay == true) {
            session.StepFrameRequested = true;
        }
    }

    private static void SetGrabMode(GrabModes mode) {
        if (Settings.Instance.GrabMode != mode && !AkronModule.TryUse(AkronFeatureKind.GrabModeHotkey)) {
            return;
        }

        Settings.Instance.GrabMode = mode;
        Engine.Scene?.Add(new AkronToast("Grab mode: " + Settings.Instance.GrabMode));
    }

    private static void SetConfiguredGrabMode(GrabModes mode) {
        AkronModule.Settings.GrabModeOverrideMode = mode;
        ApplyGrabModeOverrideIfEnabled();
    }

    private static void SetGrabModeOverrideEnabled(bool enabled) {
        if (enabled) {
            AkronModule.Settings.GrabModeOverrideEnabled = true;
            ApplyGrabModeOverrideIfEnabled();
            return;
        }

        AkronModule.Settings.GrabModeOverrideEnabled = false;
        if (Settings.Instance.GrabMode != GrabModes.Hold) {
            Settings.Instance.GrabMode = GrabModes.Hold;
            Engine.Scene?.Add(new AkronToast("Grab mode: Hold"));
        }
    }

    private static void ApplyGrabModeOverrideIfEnabled() {
        if (AkronModule.Settings.GrabModeOverrideEnabled) {
            SetGrabMode(AkronModule.Settings.GrabModeOverrideMode);
        }
    }

    private static string DescribeOverlayBindingCaveat() {
        string binding = AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay);
        bool usesTab = AkronModule.Settings.ToggleOverlay?.Keys?.Contains(Keys.Tab) == true;
        if (usesTab && AkronInterop.SpeedrunToolTabConflictMitigated) {
            return binding + " | Speedrun Tool Tab disabled";
        }

        if (usesTab && AkronInterop.SpeedrunToolLoaded) {
            return binding + " | Tab conflicts with Speedrun Tool slots";
        }

        return binding;
    }

    private static string DescribeAliases(string label) {
        if (!string.IsNullOrWhiteSpace(label) &&
            label.StartsWith("StartPos Slot ", StringComparison.OrdinalIgnoreCase)) {
            return "startpos, slot, keybind";
        }

        return SearchAliases.TryGetValue(label, out string[] aliases) && aliases.Length > 0
            ? string.Join(", ", aliases)
            : "No extra aliases";
    }

    private static string DescribeBindingForAction(string label) {
        AkronModuleSettings settings = AkronModule.Settings;
        return label switch {
            "Retry" => AkronModuleSettings.DescribeBinding(settings.Retry),
            "Capture StartPos State" => AkronModuleSettings.DescribeBinding(settings.SaveState),
            "Restore StartPos State" => AkronModuleSettings.DescribeBinding(settings.LoadState),
            "StartPos Snapshot Slot" => AkronModuleSettings.DescribeBinding(settings.PreviousSlot) + " / " + AkronModuleSettings.DescribeBinding(settings.NextSlot),
            "Grab Mode" => AkronModuleSettings.DescribeBinding(settings.CycleGrabMode),
            "Freeze Gameplay" => AkronModuleSettings.DescribeBinding(settings.FreezeGameplay),
            "Frame Stepper" => AkronModuleSettings.DescribeBinding(settings.StepFrame),
            "Timescale" => AkronModuleSettings.DescribeBinding(settings.DecreaseTimescale) + " / " + AkronModuleSettings.DescribeBinding(settings.IncreaseTimescale),
            "StartPos" => AkronModuleSettings.DescribeBinding(settings.SetStartPos) + " / " + AkronModuleSettings.DescribeBinding(settings.LoadStartPos) + " / " + AkronModuleSettings.DescribeBinding(settings.ClearStartPos),
            "Set StartPos" => AkronModuleSettings.DescribeBinding(settings.SetStartPos),
            "Load StartPos" => AkronModuleSettings.DescribeBinding(settings.LoadStartPos),
            "Clear StartPos" => AkronModuleSettings.DescribeBinding(settings.ClearStartPos),
            "Previous StartPos" => AkronModuleSettings.DescribeBinding(settings.PreviousStartPos),
            "Next StartPos" => AkronModuleSettings.DescribeBinding(settings.NextStartPos),
            "Click Teleport" => "Hold " + AkronModuleSettings.DescribeBinding(settings.ClickTeleportCursor) + " and click",
            "Cursor Zoom" => "Hold " + AkronModuleSettings.DescribeBinding(settings.CursorZoomHold) + " and scroll",
            "Cursor Tools" => "Hold " + AkronModuleSettings.DescribeBinding(settings.CursorToolsHold),
            "Reload Room" => AkronModuleSettings.DescribeBinding(settings.ReloadRoom),
            "Reload Chapter" => AkronModuleSettings.DescribeBinding(settings.ReloadChapter),
            "Open Debug Map" => AkronModuleSettings.DescribeBinding(settings.OpenDebugMap),
            _ => "Menu action"
        };
    }

    private static string DescribeBindingForEntry(ActionEntry entry) {
        if (string.Equals(entry.ActionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            return AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay);
        }

        return DescribeBindingForAction(entry.Label);
    }

    private static string DescribeEffectiveMenuBinding(string actionKey, string label) {
        if (string.Equals(actionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            return AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay);
        }

        return HasMenuBinding(actionKey)
            ? DescribeMenuBinding(actionKey)
            : DescribeBindingForAction(label);
    }

    private static string DescribeOverviewBinding(string actionKey, string label) {
        if (string.Equals(actionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            return AkronModuleSettings.DescribeBinding(AkronModule.Settings.ToggleOverlay);
        }

        if (HasMenuBinding(actionKey)) {
            return DescribeMenuBinding(actionKey);
        }

        string builtIn = DescribeBindingForAction(label);
        return string.IsNullOrWhiteSpace(builtIn) || string.Equals(builtIn, "Unbound", StringComparison.OrdinalIgnoreCase)
            ? "Unbound"
            : builtIn;
    }

    private static string SimplifyKeyToken(Keys key) {
        return key switch {
            Keys.OemPlus => "+",
            Keys.OemMinus => "-",
            Keys.OemPipe => "\\",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemComma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemQuotes => "'",
            Keys.OemSemicolon => ";",
            Keys.OemTilde => "~",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            _ => key.ToString().Replace("NumPad", "Num ").Replace("Oem", string.Empty).Trim()
        };
    }

    private static string SimplifyButtonToken(Buttons button) {
        return button == 0
            ? string.Empty
            : button.ToString()
                .Replace("LeftShoulder", "LB")
                .Replace("RightShoulder", "RB")
                .Replace("LeftTrigger", "LT")
                .Replace("RightTrigger", "RT")
                .Replace("LeftStick", "LStick")
                .Replace("RightStick", "RStick")
                .Replace("DPad", "DPad ")
                .Trim();
    }

    private readonly struct MenuBinding {
        private MenuBinding(IEnumerable<Keys> keys) {
            KeyList = NormalizeKeys(keys);
            Key = KeyList.Count == 0 ? Keys.None : KeyList[^1];
            Button = 0;
        }

        public Keys Key { get; }
        public List<Keys> KeyList { get; }
        public Buttons Button { get; }

        private MenuBinding(Buttons button) {
            Key = Keys.None;
            KeyList = new List<Keys>();
            Button = button;
        }

        public static MenuBinding FromKeyboardState(Keys key, Keys[] pressedKeys) {
            IEnumerable<Keys> modifiers = pressedKeys.Where(IsModifierKey);
            return IsModifierKey(key)
                ? new MenuBinding(modifiers)
                : new MenuBinding(modifiers.Concat(new[] { key }));
        }

        public static MenuBinding FromButton(Buttons button) {
            return new MenuBinding(button);
        }

        public static bool TryParse(string value, out MenuBinding binding) {
            binding = default;
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            string trimmed = value.Trim();
            if (TryParseButton(trimmed, out Buttons button)) {
                binding = FromButton(button);
                return true;
            }

            string[] parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) {
                return false;
            }

            List<Keys> keys = new List<Keys>();
            foreach (string rawPart in parts) {
                string part = rawPart.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase)) {
                    keys.Add(Keys.LeftControl);
                    continue;
                }

                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) {
                    keys.Add(Keys.LeftAlt);
                    continue;
                }

                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) {
                    keys.Add(Keys.LeftShift);
                    continue;
                }

                if (!TryParseKeyToken(part, out Keys key) || key == Keys.None) {
                    return false;
                }

                keys.Add(key);
            }

            if (keys.Count == 0) {
                return false;
            }

            binding = new MenuBinding(keys);
            return true;
        }

        public bool Pressed() {
            if (Button != 0) {
                return IsGamePadPressed(Button);
            }

            return KeyList.Count > 0 &&
                   KeyList.Any(key => MInput.Keyboard.Pressed(key)) &&
                   KeyList.All(key => Keyboard.GetState().IsKeyDown(key));
        }

        public string ToStorageString() {
            if (Button != 0) {
                return "Button:" + Button;
            }

            List<string> parts = new List<string>();
            parts.AddRange(KeyList.Select(key => key.ToString()));
            return string.Join("+", parts);
        }

        public string ToDisplayString() {
            if (Button != 0) {
                return SimplifyButtonToken(Button);
            }

            List<string> parts = new List<string>();
            parts.AddRange(KeyList.Select(SimplifyKeyToken));
            return string.Join("+", parts);
        }

        public List<Keys> ToKeyList() {
            if (Button != 0) {
                return new List<Keys>();
            }

            return KeyList.ToList();
        }

        public List<Buttons> ToButtonList() {
            return Button == 0 ? new List<Buttons>() : new List<Buttons> { Button };
        }

        private static bool TryParseButton(string value, out Buttons button) {
            button = 0;
            const string buttonPrefix = "Button:";
            const string controllerPrefix = "Controller:";
            string token = value;
            if (value.StartsWith(buttonPrefix, StringComparison.OrdinalIgnoreCase)) {
                token = value.Substring(buttonPrefix.Length);
            } else if (value.StartsWith(controllerPrefix, StringComparison.OrdinalIgnoreCase)) {
                token = value.Substring(controllerPrefix.Length);
            } else {
                return false;
            }

            return Enum.TryParse(token.Trim(), out button) && IsBindableButton(button);
        }

        private static List<Keys> NormalizeKeys(IEnumerable<Keys> keys) {
            List<Keys> normalized = new List<Keys>();
            foreach (Keys key in keys ?? Enumerable.Empty<Keys>()) {
                if (key == Keys.None || normalized.Contains(key)) {
                    continue;
                }

                normalized.Add(key);
            }

            return normalized;
        }

        private static bool TryParseKeyToken(string token, out Keys key) {
            key = Keys.None;
            string normalized = token.Trim();
            normalized = normalized switch {
                "LCtrl" => nameof(Keys.LeftControl),
                "RCtrl" => nameof(Keys.RightControl),
                "LAlt" => nameof(Keys.LeftAlt),
                "RAlt" => nameof(Keys.RightAlt),
                "LShift" => nameof(Keys.LeftShift),
                "RShift" => nameof(Keys.RightShift),
                _ => normalized
            };

            return Enum.TryParse(normalized, ignoreCase: true, out key);
        }
    }

    private bool UpdateBindingCapture() {
        if (string.IsNullOrWhiteSpace(bindingCaptureActionKey)) {
            return false;
        }

        SearchInputConsumedThisFrame = true;
        SearchOwnsGameplayInputThisFrame = true;

        Keys[] pressedKeys = Keyboard.GetState().GetPressedKeys();
        if (bindingCaptureWaitingForRelease) {
            if (pressedKeys.Length == 0 && !IsAnyGamePadButtonDown()) {
                bindingCaptureWaitingForRelease = false;
            }
            return true;
        }

        if (pressedKeys.Contains(Keys.Escape)) {
            CancelBindingCapture();
            return true;
        }

        if (pressedKeys.Contains(Keys.Back) || pressedKeys.Contains(Keys.Delete)) {
            if (bindingCaptureOverlayToggle) {
                ResetOverlayToggleBinding();
            } else if (bindingCaptureAutoDeafenHotkey) {
                AkronActions.RestoreAutoDeafen();
                AkronModule.Settings.AutoDeafenHotkey = string.Empty;
            } else {
                ClearMenuBinding(bindingCaptureActionKey);
            }
            CancelBindingCapture();
            return true;
        }

        if (!bindingCaptureAutoDeafenHotkey && TryGetPressedGamePadButton(out Buttons button)) {
            MenuBinding binding = MenuBinding.FromButton(button);
            if (bindingCaptureOverlayToggle) {
                SetOverlayToggleBinding(binding);
            } else {
                SetMenuBinding(bindingCaptureActionKey, binding);
            }
            CancelBindingCapture();
            return true;
        }

        Keys key = pressedKeys.FirstOrDefault(IsBindableKey);
        if (key != Keys.None) {
            if (bindingCaptureAutoDeafenHotkey) {
                if (AkronHotkey.TryFromKeyboardState(pressedKeys, out AkronHotkey hotkey)) {
                    AkronActions.SetAutoDeafenHotkey(hotkey.ToStorageString(), out _);
                }
            } else {
                MenuBinding binding = MenuBinding.FromKeyboardState(key, pressedKeys);
                if (bindingCaptureOverlayToggle) {
                    SetOverlayToggleBinding(binding);
                } else {
                    SetMenuBinding(bindingCaptureActionKey, binding);
                }
            }
            CancelBindingCapture();
        }

        return true;
    }

    private void StartBindingCapture(ActionEntry entry) {
        StartBindingCapture(entry.ActionKey, entry.Tab + " / " + entry.Label);
    }

    private void StartBindingCapture(string actionKey, string displayName) {
        bindingCaptureActionKey = actionKey;
        bindingCaptureDisplayName = displayName;
        bindingCaptureOverlayToggle = false;
        bindingCaptureAutoDeafenHotkey = false;
        bindingCaptureWaitingForRelease = true;
    }

    private void StartOverlayToggleBindingCapture() {
        bindingCaptureActionKey = OverlayToggleActionKey;
        bindingCaptureDisplayName = "Open Overlay";
        bindingCaptureOverlayToggle = true;
        bindingCaptureWaitingForRelease = true;
    }

    private void StartAutoDeafenHotkeyCapture() {
        bindingCaptureActionKey = "__akron_auto_deafen_hotkey";
        bindingCaptureDisplayName = "Auto Deafen Discord hotkey";
        bindingCaptureOverlayToggle = false;
        bindingCaptureAutoDeafenHotkey = true;
        bindingCaptureWaitingForRelease = true;
    }

    private void CancelBindingCapture() {
        bindingCaptureActionKey = string.Empty;
        bindingCaptureDisplayName = string.Empty;
        bindingCaptureOverlayToggle = false;
        bindingCaptureAutoDeafenHotkey = false;
        bindingCaptureWaitingForRelease = false;
    }

    private void StartBindingCaptureForEntry(ActionEntry entry) {
        if (string.Equals(entry.ActionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            StartOverlayToggleBindingCapture();
            return;
        }

        StartBindingCapture(entry);
    }

    private static bool HasClearableBinding(ActionEntry entry) {
        if (string.Equals(entry.ActionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            return HasOverlayToggleKeyboardBinding();
        }

        return HasMenuBinding(entry.ActionKey) || TryGetDefaultButtonBinding(entry.ActionKey, out ButtonBinding binding) && !IsEmptyBinding(binding);
    }

    private static void ClearBindingForEntry(ActionEntry entry) {
        if (string.Equals(entry.ActionKey, OverlayToggleActionKey, StringComparison.Ordinal)) {
            ResetOverlayToggleBinding();
            return;
        }

        ClearMenuBinding(entry.ActionKey);
        if (ClearDefaultButtonBinding(entry.ActionKey)) {
            menuBindingRevision++;
        }
    }

    private static bool TryGetDefaultButtonBinding(string actionKey, out ButtonBinding binding) {
        AkronModuleSettings settings = CurrentSettingsOrDefault();
        binding = actionKey switch {
            "Shortcuts/Retry" => settings.Retry,
            "Shortcuts/Reload Room" => settings.ReloadRoom,
            "Shortcuts/Reload Chapter" => settings.ReloadChapter,
            "StartPos/Capture State" => settings.SaveState,
            "StartPos/Restore State" => settings.LoadState,
            "popup/StartPos/Set" => settings.SetStartPos,
            "popup/StartPos/Load" => settings.LoadStartPos,
            "popup/StartPos/Clear" => settings.ClearStartPos,
            "popup/StartPos/Previous" => settings.PreviousStartPos,
            "popup/StartPos/Next" => settings.NextStartPos,
            "popup/StartPos/Load Slot 1" => settings.LoadStartPosSlot1,
            "popup/StartPos/Load Slot 2" => settings.LoadStartPosSlot2,
            "popup/StartPos/Load Slot 3" => settings.LoadStartPosSlot3,
            "popup/StartPos/Load Slot 4" => settings.LoadStartPosSlot4,
            "popup/StartPos/Load Slot 5" => settings.LoadStartPosSlot5,
            "popup/StartPos/Load Slot 6" => settings.LoadStartPosSlot6,
            "popup/StartPos/Load Slot 7" => settings.LoadStartPosSlot7,
            "popup/StartPos/Load Slot 8" => settings.LoadStartPosSlot8,
            "popup/StartPos/Load Slot 9" => settings.LoadStartPosSlot9,
            "Player/Noclip" => null,
            "Player/Hazard Accuracy" => null,
            "Creator/Cursor Zoom" => settings.CursorZoomHold,
            "Creator/Cursor Tools" => settings.CursorToolsHold,
            "Level/Show Hitboxes" => settings.ToggleHitboxes,
            "Level/Freeze Gameplay" => settings.FreezeGameplay,
            "Shortcuts/Neutral Drop" => null,
            "Shortcuts/Backboost" => null,
            _ => null
        };

        return binding != null;
    }

    private static bool ClearDefaultButtonBinding(string actionKey) {
        AkronModuleSettings settings = AkronModule.Settings;
        switch (actionKey) {
            case "Shortcuts/Retry": settings.Retry = EmptyButtonBinding(); return true;
            case "Shortcuts/Reload Room": settings.ReloadRoom = EmptyButtonBinding(); return true;
            case "Shortcuts/Reload Chapter": settings.ReloadChapter = EmptyButtonBinding(); return true;
            case "StartPos/Capture State": settings.SaveState = EmptyButtonBinding(); return true;
            case "StartPos/Restore State": settings.LoadState = EmptyButtonBinding(); return true;
            case "popup/StartPos/Set": settings.SetStartPos = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load": settings.LoadStartPos = EmptyButtonBinding(); return true;
            case "popup/StartPos/Clear": settings.ClearStartPos = EmptyButtonBinding(); return true;
            case "popup/StartPos/Previous": settings.PreviousStartPos = EmptyButtonBinding(); return true;
            case "popup/StartPos/Next": settings.NextStartPos = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 1": settings.LoadStartPosSlot1 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 2": settings.LoadStartPosSlot2 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 3": settings.LoadStartPosSlot3 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 4": settings.LoadStartPosSlot4 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 5": settings.LoadStartPosSlot5 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 6": settings.LoadStartPosSlot6 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 7": settings.LoadStartPosSlot7 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 8": settings.LoadStartPosSlot8 = EmptyButtonBinding(); return true;
            case "popup/StartPos/Load Slot 9": settings.LoadStartPosSlot9 = EmptyButtonBinding(); return true;
            case "Creator/Cursor Zoom": settings.CursorZoomHold = EmptyButtonBinding(); return true;
            case "Creator/Cursor Tools": settings.CursorToolsHold = EmptyButtonBinding(); return true;
            case "Level/Show Hitboxes": settings.ToggleHitboxes = EmptyButtonBinding(); return true;
            case "Level/Freeze Gameplay": settings.FreezeGameplay = EmptyButtonBinding(); return true;
        }

        return false;
    }

    private static ButtonBinding EmptyButtonBinding() {
        return AkronModuleSettings.CreateEmptyButtonBinding();
    }

    private static bool IsEmptyBinding(ButtonBinding binding) {
        return binding == null ||
               (binding.Keys == null || binding.Keys.Count == 0 || binding.Keys.All(key => key == Keys.None)) &&
               (binding.MouseButtons == null || binding.MouseButtons.Count == 0) &&
               (binding.Buttons == null || binding.Buttons.Count == 0 || binding.Buttons.All(button => button == 0));
    }

    private static bool IsShiftDown() {
        return Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
    }

    private static bool IsBindableKey(Keys key) {
        return key != Keys.None;
    }

    private static bool IsModifierKey(Keys key) {
        return key == Keys.LeftControl ||
               key == Keys.RightControl ||
               key == Keys.LeftAlt ||
               key == Keys.RightAlt ||
               key == Keys.LeftShift ||
               key == Keys.RightShift;
    }

    private static bool IsBindableButton(Buttons button) {
        return button != 0;
    }

    private static AkronModuleSettings CurrentSettingsOrDefault() {
        return AkronModule.Instance == null ? new AkronModuleSettings() : AkronModule.Settings;
    }

    private static bool IsMenuBindingPressed(string actionKey) {
        return TryGetMenuBinding(actionKey, out MenuBinding binding) && binding.Pressed();
    }

    private static bool HasMenuBinding(string actionKey) {
        return TryGetMenuBinding(actionKey, out _);
    }

    private static string DescribeMenuBinding(string actionKey) {
        return TryGetMenuBinding(actionKey, out MenuBinding binding) ? binding.ToDisplayString() : "Unbound";
    }

    private static bool TryGetMenuBinding(string actionKey, out MenuBinding binding) {
        binding = default;
        if (AkronModule.Instance == null) {
            return false;
        }

        Dictionary<string, string> bindings = AkronModule.Settings.MenuActionBindings;
        if (bindings == null ||
            !bindings.TryGetValue(actionKey, out string keyName) ||
            string.IsNullOrWhiteSpace(keyName) ||
            !MenuBinding.TryParse(keyName, out binding)) {
            return false;
        }

        return true;
    }

    private static void SetMenuBinding(string actionKey, MenuBinding binding) {
        AkronModule.Settings.MenuActionBindings ??= new Dictionary<string, string>();
        AkronModule.Settings.MenuActionBindings[actionKey] = binding.ToStorageString();
        menuBindingRevision++;
    }

    private static void ClearMenuBinding(string actionKey) {
        if (AkronModule.Settings.MenuActionBindings?.Remove(actionKey) == true) {
            menuBindingRevision++;
        }
    }

    private static bool HasOverlayToggleKeyboardBinding() {
        return AkronModule.Settings.ToggleOverlay?.Keys?.Count > 0;
    }

    private static void SetOverlayToggleBinding(MenuBinding binding) {
        AkronModule.Settings.ToggleOverlay ??= AkronModuleSettings.CreateDefaultOverlayToggleBinding();
        AkronModule.Settings.ToggleOverlay.Keys = binding.ToKeyList();
        AkronModule.Settings.ToggleOverlay.Buttons = binding.ToButtonList();
        AkronModuleSettings.EnsureCurrentOverlayToggleDefault(AkronModule.Settings);
        menuBindingRevision++;
    }

    private static void ResetOverlayToggleBinding() {
        AkronModule.Settings.ToggleOverlay = AkronModuleSettings.CreateDefaultOverlayToggleBinding();
        menuBindingRevision++;
    }

    private static string BuildActionKey(string tab, string label) {
        return tab + "/" + label;
    }

    private static string PopupActionKey(string label, string action) {
        return "popup/" + label + "/" + action;
    }

    private static bool IsControlDown() {
        return Keyboard.GetState().IsKeyDown(Keys.LeftControl) || Keyboard.GetState().IsKeyDown(Keys.RightControl);
    }

    private static bool IsAltDown() {
        return Keyboard.GetState().IsKeyDown(Keys.LeftAlt) || Keyboard.GetState().IsKeyDown(Keys.RightAlt);
    }

}
