using Celeste.Mod;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

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
        binding.Keys = new List<Keys> { Keys.Tab, Keys.RightShift };
        binding.Buttons = new List<Buttons>();
        return binding;
    }

    public static void EnsureCurrentOverlayToggleDefault(AkronModuleSettings settings) {
        if (settings == null) {
            return;
        }

        ButtonBinding binding = settings.ToggleOverlay;
        if (binding == null) {
            settings.ToggleOverlay = CreateDefaultOverlayToggleBinding();
            return;
        }

        if (binding.Keys != null) {
            List<Keys> safeKeys = binding.Keys
                .Where(IsSafeOverlayToggleKey)
                .Distinct()
                .ToList();
            if (safeKeys.Count != binding.Keys.Count) {
                binding.Keys = safeKeys;
            }
        }

        if (binding.Buttons != null && binding.Buttons.Contains(Buttons.Back)) {
            binding.Buttons = binding.Buttons.Where(button => button != Buttons.Back).ToList();
        }

        bool hasKeys = binding?.Keys != null && binding.Keys.Count > 0;
        bool hasButtons = binding?.Buttons != null && binding.Buttons.Count > 0;
        bool tabOnlyKeyboard = binding?.Keys != null &&
                               binding.Keys.Count == 1 &&
                               binding.Keys.Contains(Keys.Tab);
        if (!hasKeys && !hasButtons || tabOnlyKeyboard) {
            settings.ToggleOverlay = CreateDefaultOverlayToggleBinding();
        }
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

    private static bool IsSafeOverlayToggleKey(Keys key) {
        return key != Keys.None &&
               key != Keys.Back &&
               key != Keys.Delete &&
               key != Keys.Escape;
    }
}
