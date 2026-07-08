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

    public static ButtonBinding ResolveEntityInspectorCursorHoldBinding(AkronModuleSettings settings) {
        ButtonBinding binding = settings?.EntityInspectorCursorHold;
        return IsUninitializedButtonBinding(binding)
            ? CreateLeftAltHoldBinding()
            : binding;
    }

    private static bool IsUninitializedButtonBinding(ButtonBinding binding) {
        if (binding == null) {
            return true;
        }

        try {
            return binding.Keys == null &&
                   binding.Buttons == null &&
                   (binding.MouseButtons == null || !binding.MouseButtons.Any());
        } catch (InvalidProgramException) {
            return false;
        }
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
        settings.MenuActionBindings ??= new Dictionary<string, string>();
    }

    private static void EnsureCurrentButtonBindingDefaults(AkronModuleSettings settings) {
        settings.FastLookoutHold = EnsureButtonBinding(settings.FastLookoutHold, CreateEmptyButtonBinding);
        settings.Retry = EnsureButtonBinding(settings.Retry, CreateEmptyButtonBinding);
        settings.ReloadRoom = EnsureButtonBinding(settings.ReloadRoom, CreateEmptyButtonBinding);
        settings.OpenDebugMap = EnsureButtonBinding(settings.OpenDebugMap, CreateEmptyButtonBinding);
        settings.ReloadChapter = EnsureButtonBinding(settings.ReloadChapter, CreateEmptyButtonBinding);
        settings.SaveState = EnsureButtonBinding(settings.SaveState, CreateEmptyButtonBinding);
        settings.LoadState = EnsureButtonBinding(settings.LoadState, CreateEmptyButtonBinding);
        settings.PreviousSlot = EnsureButtonBinding(settings.PreviousSlot, CreateEmptyButtonBinding);
        settings.NextSlot = EnsureButtonBinding(settings.NextSlot, CreateEmptyButtonBinding);
        settings.CycleGrabMode = EnsureButtonBinding(settings.CycleGrabMode, CreateEmptyButtonBinding);
        settings.FreezeGameplay = EnsureButtonBinding(settings.FreezeGameplay, CreateEmptyButtonBinding);
        settings.StepFrame = EnsureButtonBinding(settings.StepFrame, CreateEmptyButtonBinding);
        settings.DecreaseTimescale = EnsureButtonBinding(settings.DecreaseTimescale, CreateEmptyButtonBinding);
        settings.IncreaseTimescale = EnsureButtonBinding(settings.IncreaseTimescale, CreateEmptyButtonBinding);
        settings.SetStartPos = EnsureButtonBinding(settings.SetStartPos, CreateEmptyButtonBinding);
        settings.LoadStartPos = EnsureButtonBinding(settings.LoadStartPos, CreateEmptyButtonBinding);
        settings.ClearStartPos = EnsureButtonBinding(settings.ClearStartPos, CreateEmptyButtonBinding);
        settings.PreviousStartPos = EnsureButtonBinding(settings.PreviousStartPos, CreateEmptyButtonBinding);
        settings.NextStartPos = EnsureButtonBinding(settings.NextStartPos, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot1 = EnsureButtonBinding(settings.LoadStartPosSlot1, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot2 = EnsureButtonBinding(settings.LoadStartPosSlot2, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot3 = EnsureButtonBinding(settings.LoadStartPosSlot3, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot4 = EnsureButtonBinding(settings.LoadStartPosSlot4, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot5 = EnsureButtonBinding(settings.LoadStartPosSlot5, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot6 = EnsureButtonBinding(settings.LoadStartPosSlot6, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot7 = EnsureButtonBinding(settings.LoadStartPosSlot7, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot8 = EnsureButtonBinding(settings.LoadStartPosSlot8, CreateEmptyButtonBinding);
        settings.LoadStartPosSlot9 = EnsureButtonBinding(settings.LoadStartPosSlot9, CreateEmptyButtonBinding);
        settings.ToggleHitboxes = EnsureButtonBinding(settings.ToggleHitboxes, CreateEmptyButtonBinding);
        settings.ToggleEntityInspector = EnsureButtonBinding(settings.ToggleEntityInspector, CreateEmptyButtonBinding);
        settings.EntityInspectorCursorHold = EnsureButtonBinding(settings.EntityInspectorCursorHold, CreateLeftAltHoldBinding);
        settings.ToggleFrameBypass = EnsureButtonBinding(settings.ToggleFrameBypass, CreateEmptyButtonBinding);
        settings.CycleFrameBypassCameraSmoothing = EnsureButtonBinding(settings.CycleFrameBypassCameraSmoothing, CreateEmptyButtonBinding);
        settings.ClickTeleportCursor = EnsureButtonBinding(settings.ClickTeleportCursor, CreateLeftAltHoldBinding);
        settings.CursorZoomHold = EnsureButtonBinding(settings.CursorZoomHold, CreateLeftAltHoldBinding);
        settings.CursorToolsHold = EnsureButtonBinding(settings.CursorToolsHold, CreateLeftAltHoldBinding);
    }

    private static ButtonBinding EnsureButtonBinding(ButtonBinding binding, Func<ButtonBinding> defaultFactory) {
        return IsUninitializedButtonBinding(binding) ? defaultFactory() : binding;
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
