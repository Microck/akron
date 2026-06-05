using Celeste.Mod;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    public const string CustomLabelRowPrefix = "custom:";

    private static readonly string[] DefaultLabelRowOrder = {
        "Death Stats",
        "Room",
        "Status",
        "Toasts",
        "Cheat Indicator",
        "Input History",
        "Inputs per second",
        "Dash Stats",
        "Jump Stats",
        "StartPos HUD",
        "Room Timer",
        "Room Stat Tracker",
        "Attempts",
        "No Short Numbers"
    };

    public static ButtonBinding CreateDefaultOverlayToggleBinding() {
        ButtonBinding binding = new ButtonBinding(0, Keys.Tab);
        binding.Keys = new List<Keys> { Keys.Tab };
        binding.Buttons = new List<Buttons>();
        binding.MouseButtons = new List<MInput.MouseData.MouseButtons>();
        return binding;
    }

    public static ButtonBinding CreateEmptyButtonBinding() {
        ButtonBinding binding = new ButtonBinding(0, Keys.None);
        binding.Keys = new List<Keys>();
        binding.Buttons = new List<Buttons>();
        binding.MouseButtons = new List<MInput.MouseData.MouseButtons>();
        return binding;
    }

    public static ButtonBinding CreateLeftAltHoldBinding() {
        ButtonBinding binding = new ButtonBinding(0, Keys.LeftAlt);
        binding.Keys = new List<Keys> { Keys.LeftAlt };
        binding.Buttons = new List<Buttons>();
        binding.MouseButtons = new List<MInput.MouseData.MouseButtons>();
        return binding;
    }

    public static void EnsureCurrentOverlayToggleDefault(AkronModuleSettings settings) {
        if (settings == null) {
            return;
        }

        ButtonBinding binding = settings.ToggleOverlay;
        if (ShouldUseDefaultOverlayToggleBinding(binding?.Keys, binding?.Buttons)) {
            settings.ToggleOverlay = CreateDefaultOverlayToggleBinding();
        }
    }

    internal static bool ShouldUseDefaultOverlayToggleBinding(IReadOnlyCollection<Keys> keys, IReadOnlyCollection<Buttons> buttons) {
        return !HasUsableOverlayToggleBinding(keys, buttons) ||
               IsPlainTabDefaultWithExtraModifiers(keys, buttons);
    }

    internal static bool HasUsableOverlayToggleBinding(IReadOnlyCollection<Keys> keys, IReadOnlyCollection<Buttons> buttons) {
        return keys?.Any(key => key != Keys.None) == true ||
               buttons?.Any(button => button != 0) == true;
    }

    private static bool IsPlainTabDefaultWithExtraModifiers(IReadOnlyCollection<Keys> keys, IReadOnlyCollection<Buttons> buttons) {
        if (keys == null || buttons?.Any(button => button != 0) == true) {
            return false;
        }

        HashSet<Keys> normalizedKeys = keys
            .Where(key => key != Keys.None)
            .ToHashSet();
        return normalizedKeys.Contains(Keys.Tab) &&
               normalizedKeys.Count > 1 &&
               normalizedKeys.All(key => key == Keys.Tab || IsModifierKey(key));
    }

    private static bool IsModifierKey(Keys key) {
        return key == Keys.LeftControl ||
               key == Keys.RightControl ||
               key == Keys.LeftAlt ||
               key == Keys.RightAlt ||
               key == Keys.LeftShift ||
               key == Keys.RightShift;
    }

    public static void EnsureCurrentKeybindDefaults(AkronModuleSettings settings) {
        if (settings == null) {
            return;
        }

        EnsureCurrentOverlayToggleDefault(settings);
        EnsureCurrentButtonBindingDefaults(settings);
        settings.MenuActionBindings = new Dictionary<string, string>();
    }

    private static void EnsureCurrentButtonBindingDefaults(AkronModuleSettings settings) {
        settings.FastLookoutHold = CreateEmptyButtonBinding();
        settings.Retry = CreateEmptyButtonBinding();
        settings.ReloadRoom = CreateEmptyButtonBinding();
        settings.OpenDebugMap = CreateEmptyButtonBinding();
        settings.ReloadChapter = CreateEmptyButtonBinding();
        settings.SaveState = CreateEmptyButtonBinding();
        settings.LoadState = CreateEmptyButtonBinding();
        settings.PreviousSlot = CreateEmptyButtonBinding();
        settings.NextSlot = CreateEmptyButtonBinding();
        settings.CycleGrabMode = CreateEmptyButtonBinding();
        settings.FreezeGameplay = CreateEmptyButtonBinding();
        settings.StepFrame = CreateEmptyButtonBinding();
        settings.DecreaseTimescale = CreateEmptyButtonBinding();
        settings.IncreaseTimescale = CreateEmptyButtonBinding();
        settings.SetStartPos = CreateEmptyButtonBinding();
        settings.LoadStartPos = CreateEmptyButtonBinding();
        settings.ClearStartPos = CreateEmptyButtonBinding();
        settings.PreviousStartPos = CreateEmptyButtonBinding();
        settings.NextStartPos = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot1 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot2 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot3 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot4 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot5 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot6 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot7 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot8 = CreateEmptyButtonBinding();
        settings.LoadStartPosSlot9 = CreateEmptyButtonBinding();
        settings.ToggleHitboxes = CreateEmptyButtonBinding();
        settings.ToggleEntityInspector = CreateEmptyButtonBinding();
        settings.ToggleFrameBypass = CreateEmptyButtonBinding();
        settings.CycleFrameBypassCameraSmoothing = CreateEmptyButtonBinding();
        settings.ClickTeleportCursor = CreateLeftAltHoldBinding();
        settings.CursorZoomHold = CreateLeftAltHoldBinding();
    }

    public static List<string> BuildDefaultLabelRowOrder() {
        return DefaultLabelRowOrder.ToList();
    }

    public static string BuildCustomLabelRowKey(string id) {
        return CustomLabelRowPrefix + (id ?? string.Empty).Trim();
    }

    public static List<string> NormalizeLabelRowOrder(IEnumerable<string> order, IEnumerable<AkronCustomHudLabel> customLabels) {
        HashSet<string> allowed = new HashSet<string>(DefaultLabelRowOrder, StringComparer.OrdinalIgnoreCase);
        foreach (AkronCustomHudLabel label in customLabels ?? Enumerable.Empty<AkronCustomHudLabel>()) {
            if (!string.IsNullOrWhiteSpace(label?.Id)) {
                allowed.Add(BuildCustomLabelRowKey(label.Id));
            }
        }

        List<string> normalized = new List<string>();
        foreach (string key in order ?? Enumerable.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(key) || !allowed.Contains(key) || normalized.Contains(key, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            normalized.Add(key);
        }

        foreach (string key in DefaultLabelRowOrder) {
            if (!normalized.Contains(key, StringComparer.OrdinalIgnoreCase)) {
                normalized.Add(key);
            }
        }

        foreach (AkronCustomHudLabel label in customLabels ?? Enumerable.Empty<AkronCustomHudLabel>()) {
            if (string.IsNullOrWhiteSpace(label?.Id)) {
                continue;
            }

            string key = BuildCustomLabelRowKey(label.Id);
            if (!normalized.Contains(key, StringComparer.OrdinalIgnoreCase)) {
                normalized.Add(key);
            }
        }

        return normalized;
    }

}
